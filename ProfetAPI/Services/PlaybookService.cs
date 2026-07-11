using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Models;

namespace ProfetAPI.Services;

/// <summary>
/// Aplica "playbooks" (secuencias ordenadas de tareas) a un lead, generando Activities reales.
/// El playbook predeterminado de la cuenta se aplica automáticamente al crear un lead.
/// </summary>
public class PlaybookService(ApplicationDbContext db, ILogger<PlaybookService> logger)
{
    /// <summary>
    /// Genera las tareas del playbook predeterminado de la cuenta para un lead recién creado.
    /// No hace nada si la cuenta no tiene un playbook predeterminado activo.
    /// </summary>
    public async Task ApplyDefaultAsync(int accountId, long leadId, string? ownerUserId)
    {
        var playbook = await db.ActivityPlaybooks
            .Where(p => p.AccountId == accountId && p.IsDefault && p.IsActive && !p.Deleted)
            .Include(p => p.Tasks)
            .FirstOrDefaultAsync();

        if (playbook == null) return;
        await GenerateTasksAsync(playbook, accountId, leadId, ownerUserId);
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
        return await GenerateTasksAsync(playbook, accountId, leadId, ownerUserId);
    }

    private async Task<int> GenerateTasksAsync(ActivityPlaybook playbook, int accountId, long leadId, string? ownerUserId)
    {
        var now   = DateTime.UtcNow;
        var steps = playbook.Tasks.OrderBy(t => t.Order).ToList();
        if (steps.Count == 0) return 0;

        // Resolver nombres de las etapas destino (pasos "Avanzar a etapa")
        var stageIds = steps.Where(s => s.TargetStageId.HasValue)
                            .Select(s => s.TargetStageId!.Value).Distinct().ToList();
        var stageNames = stageIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.Stages.Where(s => stageIds.Contains(s.StageId))
                             .ToDictionaryAsync(s => s.StageId, s => s.Name);

        foreach (var step in steps)
        {
            db.Activities.Add(new Activity
            {
                ActivityType     = "Task",
                AccountId        = accountId,
                Subject          = BuildSubject(step, stageNames),
                Notes            = step.Description,
                Priority         = string.IsNullOrWhiteSpace(step.Priority) ? "Media" : step.Priority,
                TaskStatus       = "Pendiente",
                DueDate          = now.AddDays(Math.Max(0, step.OffsetDays)),
                OwnerUserId      = ownerUserId,
                AssignedToUserId = ownerUserId,
                EntityType       = "Lead",
                EntityId         = leadId,
                CreatedOn        = now,
            });
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Playbook {PlaybookId} aplicado al lead {LeadId}: {Count} tareas creadas",
            playbook.PlaybookId, leadId, steps.Count);
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
