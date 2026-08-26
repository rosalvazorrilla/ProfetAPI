using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Models;

namespace ProfetAPI.Services;

/// <summary>
/// Aplica "playbooks" (secuencias ordenadas de tareas) a un lead o a la etapa de un deal,
/// generando Activities reales. El playbook predeterminado de la cuenta se aplica
/// automáticamente al crear un lead (fase Lead) y al entrar/mover un deal a una etapa
/// (fase Deal). También resuelve el gating: si hay tareas abiertas y el modo es "Block",
/// no se debe dejar convertir/avanzar.
/// </summary>
public class PlaybookService(ApplicationDbContext db, ILogger<PlaybookService> logger)
{
    private static readonly string[] OpenStatuses = ["Pendiente", "En progreso"];

    /// <summary>
    /// Genera las tareas de fase Lead (StageId == null) del playbook predeterminado
    /// de la cuenta para un lead recién creado. No hace nada si la cuenta no tiene
    /// un playbook predeterminado activo.
    /// </summary>
    public async Task ApplyDefaultAsync(int accountId, long leadId, string? ownerUserId)
    {
        var playbook = await GetDefaultPlaybookAsync(accountId);
        if (playbook == null) return;
        await GenerateTasksAsync(playbook, accountId, "Lead", leadId, ownerUserId, stageId: null);
    }

    /// <summary>
    /// Aplica un playbook específico a un lead (uso manual). Retorna cuántas tareas se crearon.
    /// </summary>
    public async Task<int> ApplyPlaybookAsync(int playbookId, int accountId, long leadId, string? ownerUserId)
    {
        var playbook = await db.ActivityPlaybooks
            .Where(p => p.PlaybookId == playbookId && p.AccountId == accountId && !p.Deleted)
            .Include(p => p.Tasks)
            .FirstOrDefaultAsync();

        if (playbook == null) return 0;
        return await GenerateTasksAsync(playbook, accountId, "Lead", leadId, ownerUserId, stageId: null);
    }

    /// <summary>
    /// Genera las tareas del playbook predeterminado que apliquen a esta etapa del deal
    /// (StageId == stageId), al crearse el deal o al moverlo de etapa. No duplica si ya
    /// se generaron antes para esa combinación deal+etapa.
    /// </summary>
    public async Task ApplyDealStageAsync(int accountId, int dealId, int stageId, string? ownerUserId)
    {
        var playbook = await GetDefaultPlaybookAsync(accountId);
        if (playbook == null) return;

        var alreadyApplied = await db.Activities.AnyAsync(a =>
            a.ActivityType == "Task" && a.EntityType == "Deal" && a.EntityId == dealId && a.StageId == stageId);
        if (alreadyApplied) return;

        await GenerateTasksAsync(playbook, accountId, "Deal", dealId, ownerUserId, stageId);
    }

    /// <summary>
    /// Tareas de tipo Task todavía abiertas (Pendiente/En progreso) para un lead/deal,
    /// opcionalmente acotadas a una etapa. Es lo que se consulta para el gating.
    /// </summary>
    public async Task<List<Activity>> GetOpenGatingTasksAsync(string entityType, long entityId, int? stageId = null)
    {
        var query = db.Activities.Where(a =>
            a.ActivityType == "Task" && a.EntityType == entityType && a.EntityId == entityId
            && OpenStatuses.Contains(a.TaskStatus));

        query = stageId.HasValue ? query.Where(a => a.StageId == stageId) : query.Where(a => a.StageId == null);

        return await query.AsNoTracking().ToListAsync();
    }

    /// <summary>
    /// Cuando se completa/omite una tarea generada por una secuencia, el plazo de la SIGUIENTE
    /// tarea pendiente de esa misma secuencia (mismo lead/deal + etapa) arranca a contar desde
    /// ahora, no desde que se generaron todas juntas — así una tarea atrasada no le come el
    /// plazo a la que sigue.
    /// </summary>
    public async Task AdvanceNextDueDateAsync(Activity completedTask)
    {
        if (completedTask.SourcePlaybookTaskId == null || completedTask.EntityType == null || completedTask.EntityId == null)
            return;

        var currentStep = await db.PlaybookTasks.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TaskId == completedTask.SourcePlaybookTaskId.Value);
        if (currentStep == null) return;

        var openTasks = await db.Activities
            .Where(a => a.ActivityType == "Task" && a.EntityType == completedTask.EntityType
                     && a.EntityId == completedTask.EntityId && a.StageId == completedTask.StageId
                     && OpenStatuses.Contains(a.TaskStatus) && a.SourcePlaybookTaskId != null)
            .ToListAsync();
        if (openTasks.Count == 0) return;

        var stepIds = openTasks.Select(a => a.SourcePlaybookTaskId!.Value).ToList();
        var stepInfo = await db.PlaybookTasks.AsNoTracking()
            .Where(t => stepIds.Contains(t.TaskId))
            .ToDictionaryAsync(t => t.TaskId, t => t);

        var next = openTasks
            .Where(a => stepInfo.TryGetValue(a.SourcePlaybookTaskId!.Value, out var s) && s.Order > currentStep.Order)
            .OrderBy(a => stepInfo[a.SourcePlaybookTaskId!.Value].Order)
            .FirstOrDefault();
        if (next == null) return;

        next.DueDate = DateTime.UtcNow.AddDays(Math.Max(0, stepInfo[next.SourcePlaybookTaskId!.Value].OffsetDays));
        await db.SaveChangesAsync();
    }

    /// <summary>Modo de gating ("Block"/"Warn") del playbook predeterminado de la cuenta. "Warn" si no hay uno.</summary>
    public async Task<string> GetGatingModeAsync(int accountId)
    {
        var playbook = await GetDefaultPlaybookAsync(accountId);
        return playbook?.GatingMode ?? "Warn";
    }

    private Task<ActivityPlaybook?> GetDefaultPlaybookAsync(int accountId) =>
        db.ActivityPlaybooks
            .Where(p => p.AccountId == accountId && p.IsDefault && p.IsActive && !p.Deleted)
            .Include(p => p.Tasks)
            .FirstOrDefaultAsync();

    private async Task<int> GenerateTasksAsync(ActivityPlaybook playbook, int accountId, string entityType, long entityId, string? ownerUserId, int? stageId)
    {
        var now   = DateTime.UtcNow;
        var steps = playbook.Tasks.Where(t => t.StageId == stageId).OrderBy(t => t.Order).ToList();
        if (steps.Count == 0) return 0;

        // Resolver nombres de las etapas destino (pasos "Avanzar a etapa")
        var stageIds = steps.Where(s => s.TargetStageId.HasValue)
                            .Select(s => s.TargetStageId!.Value).Distinct().ToList();
        var stageNames = stageIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.Stages.Where(s => stageIds.Contains(s.StageId))
                             .ToDictionaryAsync(s => s.StageId, s => s.Name);

        // El plazo de cada paso cuenta desde que termina el anterior, no desde un mismo
        // punto de partida — si el paso 1 se atrasa, el reloj del paso 2 arranca cuando
        // el 1 se completa (ver AdvanceNextDueDateAsync). Aquí, al generarse todas juntas,
        // se calcula la estimación inicial acumulando los offsets en orden.
        var cursor = now;
        foreach (var step in steps)
        {
            cursor = cursor.AddDays(Math.Max(0, step.OffsetDays));
            db.Activities.Add(new Activity
            {
                ActivityType         = "Task",
                AccountId            = accountId,
                Subject              = BuildSubject(step, stageNames),
                Notes                = step.Description,
                Priority             = string.IsNullOrWhiteSpace(step.Priority) ? "Media" : step.Priority,
                TaskStatus           = "Pendiente",
                DueDate              = cursor,
                OwnerUserId          = ownerUserId,
                AssignedToUserId     = ownerUserId,
                EntityType           = entityType,
                EntityId             = entityId,
                StageId              = stageId,
                SourcePlaybookTaskId = step.TaskId,
                CreatedOn            = now,
            });
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Playbook {PlaybookId} aplicado a {EntityType} {EntityId} (etapa {StageId}): {Count} tareas creadas",
            playbook.PlaybookId, entityType, entityId, stageId, steps.Count);
        return steps.Count;
    }

    /// <summary>
    /// Construye el asunto de la tarea generada según el tipo de acción del paso.
    /// Si el paso tiene nombre propio se respeta; si no, se genera uno legible.
    /// </summary>
    private static string BuildSubject(PlaybookTask step, IReadOnlyDictionary<int, string> stageNames)
    {
        if (!string.IsNullOrWhiteSpace(step.TaskName)) return step.TaskName.Trim();

        return step.ActionType switch
        {
            "Call"         => "Llamar al prospecto",
            "WhatsApp"     => "Escribir por WhatsApp",
            "Email"        => "Enviar email",
            "Meeting"      => "Agendar reunión",
            "AdvanceStage" => step.TargetStageId.HasValue && stageNames.TryGetValue(step.TargetStageId.Value, out var n)
                                ? $"Avanzar a etapa: {n}"
                                : "Avanzar de etapa",
            _              => "Tarea de seguimiento",
        };
    }
}
