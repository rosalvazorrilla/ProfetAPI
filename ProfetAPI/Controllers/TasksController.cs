using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Models;
using Swashbuckle.AspNetCore.Annotations;

namespace ProfetAPI.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
[SwaggerTag("CRM — Tareas")]
public class TasksController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ProfetAPI.Services.PlaybookService _playbooks;
    private readonly ProfetAPI.Services.ITimelineLogger _timeline;

    public TasksController(ApplicationDbContext context, ProfetAPI.Services.PlaybookService playbooks, ProfetAPI.Services.ITimelineLogger timeline)
    {
        _context   = context;
        _playbooks = playbooks;
        _timeline  = timeline;
    }

    private string? CurrentUserId   => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    private string? CurrentUserRole => User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
    private bool IsAdminGlobal      => CurrentUserRole == "AdminGlobal";

    // ─── helpers ────────────────────────────────────────────────────────────────

    private async Task<int?> ResolveAccountId(int? accountId)
    {
        if (IsAdminGlobal && accountId.HasValue) return accountId;
        if (!IsAdminGlobal)
        {
            var aiu = await _context.AccountInternalUsers
                .Where(u => u.UserId == CurrentUserId)
                .Select(u => (int?)u.AccountId)
                .FirstOrDefaultAsync();
            return aiu;
        }
        return accountId;
    }

    // ─── GET /api/tasks ──────────────────────────────────────────────────────────

    [HttpGet]
    [SwaggerOperation(Summary = "Listar tareas con filtros y paginación")]
    [SwaggerResponse(200, "Lista paginada de tareas")]
    public async Task<IActionResult> GetTasks(
        [FromQuery] int? accountId,
        [FromQuery] string? status,
        [FromQuery] string? priority,
        [FromQuery] string? assignedTo,
        [FromQuery] string? search,
        [FromQuery] DateTime? dueDateFrom,
        [FromQuery] DateTime? dueDateTo,
        [FromQuery] string? entityType,
        [FromQuery] long? entityId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        var resolvedAccountId = await ResolveAccountId(accountId);
        if (resolvedAccountId == null) return BadRequest("No se pudo determinar la cuenta.");

        var q = _context.Activities
            .Where(a => a.ActivityType == "Task" && a.AccountId == resolvedAccountId);

        if (!string.IsNullOrWhiteSpace(entityType))
            q = q.Where(a => a.EntityType == entityType);
        if (entityId.HasValue)
            q = q.Where(a => a.EntityId == entityId.Value);

        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(a => a.TaskStatus == status);
        if (dueDateFrom.HasValue)
            q = q.Where(a => a.DueDate >= dueDateFrom.Value);
        if (dueDateTo.HasValue)
            q = q.Where(a => a.DueDate <= dueDateTo.Value);

        if (!string.IsNullOrWhiteSpace(priority))
            q = q.Where(a => a.Priority == priority);

        if (!string.IsNullOrWhiteSpace(assignedTo))
            q = q.Where(a => a.AssignedToUserId == assignedTo);

        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(a => a.Subject != null && a.Subject.Contains(search));

        var total = await q.CountAsync();

        // Stats
        var allForAccount = _context.Activities
            .Where(a => a.ActivityType == "Task" && a.AccountId == resolvedAccountId);
        var totalAll       = await allForAccount.CountAsync();
        var totalPending   = await allForAccount.CountAsync(a => a.TaskStatus == "Pendiente" || a.TaskStatus == "En progreso");
        var totalCompleted = await allForAccount.CountAsync(a => a.TaskStatus == "Completada");
        var totalOverdue   = await allForAccount.CountAsync(a =>
            a.DueDate < DateTime.UtcNow &&
            a.TaskStatus != "Completada" &&
            a.TaskStatus != "Cancelada");

        var data = await q
            .OrderByDescending(a => a.CreatedOn)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new
            {
                a.ActivityId,
                a.Subject,
                a.Notes,
                a.Priority,
                a.TaskStatus,
                a.DueDate,
                a.CreatedOn,
                a.EntityType,
                a.EntityId,
                a.StageId,
                a.ResolutionNote,
                a.ActionType,
                a.SourcePlaybookTaskId,
                OwnerUserId = a.OwnerUserId,
                AssignedToUserId = a.AssignedToUserId,
                AssignedToName = _context.UserProfiles
                    .Where(p => p.UserId == a.AssignedToUserId)
                    .Select(p => p.FirstName + " " + p.LastName)
                    .FirstOrDefault(),
                OwnerName = _context.UserProfiles
                    .Where(p => p.UserId == a.OwnerUserId)
                    .Select(p => p.FirstName + " " + p.LastName)
                    .FirstOrDefault(),
            })
            .ToListAsync();

        // EntityType es polimórfico (Lead o Deal) — se resuelve el nombre aparte en dos
        // consultas por lote en vez de un join, para no repetir "de quién es" tarea por
        // tarea (antes el calendario/checklist solo mostraba el Subject, sin decir a qué
        // prospecto u oportunidad pertenecía — con el mismo Subject repetido es imposible
        // distinguirlas).
        var leadIds = data.Where(a => a.EntityType == "Lead" && a.EntityId.HasValue).Select(a => a.EntityId!.Value).Distinct().ToList();
        var dealIds = data.Where(a => a.EntityType == "Deal" && a.EntityId.HasValue).Select(a => (int)a.EntityId!.Value).Distinct().ToList();

        var leadNames = leadIds.Count == 0 ? new Dictionary<long, string?>() : await _context.Leads
            .Where(l => leadIds.Contains(l.LeadId))
            .Select(l => new { l.LeadId, l.Name })
            .ToDictionaryAsync(l => l.LeadId, l => l.Name);
        var dealNames = dealIds.Count == 0 ? new Dictionary<int, string?>() : await _context.Deals
            .Where(d => dealIds.Contains(d.DealId))
            .Select(d => new { d.DealId, d.DealName })
            .ToDictionaryAsync(d => d.DealId, d => (string?)d.DealName);

        var enriched = data.Select(a => new
        {
            a.ActivityId, a.Subject, a.Notes, a.Priority, a.TaskStatus, a.DueDate, a.CreatedOn,
            a.EntityType, a.EntityId, a.StageId, a.ResolutionNote, a.ActionType, a.SourcePlaybookTaskId,
            a.OwnerUserId, a.AssignedToUserId, a.AssignedToName, a.OwnerName,
            EntityName = a.EntityType == "Lead" && a.EntityId.HasValue ? leadNames.GetValueOrDefault(a.EntityId.Value)
                       : a.EntityType == "Deal" && a.EntityId.HasValue ? dealNames.GetValueOrDefault((int)a.EntityId.Value)
                       : null,
        }).ToList();

        return Ok(new
        {
            total,
            page,
            pageSize,
            stats = new { totalAll, totalPending, totalCompleted, totalOverdue },
            data = enriched,
        });
    }

    // ─── GET /api/tasks/{id} ─────────────────────────────────────────────────────

    [HttpGet("{id}")]
    [SwaggerOperation(Summary = "Detalle de una tarea")]
    [SwaggerResponse(200, "Detalle de tarea")]
    [SwaggerResponse(404, "No encontrada")]
    public async Task<IActionResult> GetTask(int id)
    {
        var resolvedAccountId = await ResolveAccountId(null);

        var task = await _context.Activities
            .Where(a => a.ActivityId == id && a.ActivityType == "Task"
                        && (IsAdminGlobal || a.AccountId == resolvedAccountId))
            .Select(a => new
            {
                a.ActivityId,
                a.Subject,
                a.Notes,
                a.Priority,
                a.TaskStatus,
                a.DueDate,
                a.CreatedOn,
                a.EntityType,
                a.EntityId,
                a.OwnerUserId,
                a.AssignedToUserId,
                AssignedToName = _context.UserProfiles
                    .Where(p => p.UserId == a.AssignedToUserId)
                    .Select(p => p.FirstName + " " + p.LastName)
                    .FirstOrDefault(),
                OwnerName = _context.UserProfiles
                    .Where(p => p.UserId == a.OwnerUserId)
                    .Select(p => p.FirstName + " " + p.LastName)
                    .FirstOrDefault(),
            })
            .FirstOrDefaultAsync();

        if (task == null) return NotFound();
        return Ok(task);
    }

    // ─── POST /api/tasks ─────────────────────────────────────────────────────────

    [HttpPost]
    [SwaggerOperation(Summary = "Crear nueva tarea")]
    [SwaggerResponse(201, "Tarea creada")]
    public async Task<IActionResult> CreateTask([FromBody] TaskUpsertDto dto)
    {
        var resolvedAccountId = await ResolveAccountId(dto.AccountId);
        if (resolvedAccountId == null) return BadRequest("No se pudo determinar la cuenta.");

        if (string.IsNullOrWhiteSpace(dto.Subject))
            return BadRequest("El nombre de la tarea es obligatorio.");

        var task = new Activity
        {
            ActivityType     = "Task",
            AccountId        = resolvedAccountId,
            Subject          = dto.Subject.Trim(),
            Notes            = dto.Notes?.Trim(),
            Priority         = dto.Priority ?? "Media",
            TaskStatus       = "Pendiente",
            DueDate          = dto.DueDate,
            OwnerUserId      = CurrentUserId,
            AssignedToUserId = dto.AssignedToUserId ?? CurrentUserId,
            EntityType       = dto.EntityType,
            EntityId         = dto.EntityId,
            CreatedOn        = DateTime.UtcNow,
        };

        _context.Activities.Add(task);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetTask), new { id = task.ActivityId },
            new { task.ActivityId, task.Subject, task.TaskStatus });
    }

    // ─── PUT /api/tasks/{id} ─────────────────────────────────────────────────────

    [HttpPut("{id}")]
    [SwaggerOperation(Summary = "Actualizar tarea")]
    [SwaggerResponse(200, "Tarea actualizada")]
    [SwaggerResponse(404, "No encontrada")]
    public async Task<IActionResult> UpdateTask(int id, [FromBody] TaskUpsertDto dto)
    {
        var resolvedAccountId = await ResolveAccountId(null);

        var task = await _context.Activities
            .Where(a => a.ActivityId == id && a.ActivityType == "Task"
                        && (IsAdminGlobal || a.AccountId == resolvedAccountId))
            .FirstOrDefaultAsync();

        if (task == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(dto.Subject))   task.Subject          = dto.Subject.Trim();
        if (dto.Notes    != null)                       task.Notes            = dto.Notes.Trim();
        if (dto.Priority != null)                       task.Priority         = dto.Priority;
        if (dto.DueDate  != null)                       task.DueDate          = dto.DueDate;
        if (dto.AssignedToUserId != null)               task.AssignedToUserId = dto.AssignedToUserId;
        if (dto.EntityType != null)                     task.EntityType       = dto.EntityType;
        if (dto.EntityId   != null)                     task.EntityId         = dto.EntityId;

        await _context.SaveChangesAsync();
        return Ok(new { task.ActivityId, updated = true });
    }

    // ─── PATCH /api/tasks/{id}/status ────────────────────────────────────────────

    [HttpPatch("{id}/status")]
    [SwaggerOperation(Summary = "Cambiar estado de una tarea")]
    [SwaggerResponse(200, "Estado actualizado")]
    [SwaggerResponse(404, "No encontrada")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] TaskStatusDto dto)
    {
        var resolvedAccountId = await ResolveAccountId(null);

        var task = await _context.Activities
            .Where(a => a.ActivityId == id && a.ActivityType == "Task"
                        && (IsAdminGlobal || a.AccountId == resolvedAccountId))
            .FirstOrDefaultAsync();

        if (task == null) return NotFound();

        var valid = new[] { "Pendiente", "En progreso", "Completada", "Cancelada", "Omitida" };
        if (!valid.Contains(dto.Status)) return BadRequest($"Estado inválido. Valores permitidos: {string.Join(", ", valid)}");

        // "Omitida" (se saltó para poder avanzar) y "Cancelada" (ya no aplica) exigen
        // motivo por igual — ambas cierran la tarea sin haberla completado de verdad.
        if ((dto.Status == "Omitida" || dto.Status == "Cancelada") && string.IsNullOrWhiteSpace(dto.Note))
            return BadRequest(new { message = dto.Status == "Cancelada"
                ? "Explica brevemente por qué se cancela esta tarea."
                : "Explica brevemente por qué se omite esta tarea." });

        var wasOpen = task.TaskStatus == "Pendiente" || task.TaskStatus == "En progreso";

        task.TaskStatus     = dto.Status;
        task.IsCompleted    = dto.Status == "Completada" || dto.Status == "Omitida";
        task.ResolutionNote = (dto.Status == "Omitida" || dto.Status == "Cancelada") ? dto.Note!.Trim() : task.ResolutionNote;
        await _context.SaveChangesAsync();

        // El plazo de la siguiente tarea de la secuencia arranca a contar desde ahora,
        // no desde que se generaron todas juntas.
        if (wasOpen && (dto.Status == "Completada" || dto.Status == "Omitida"))
            await _playbooks.AdvanceNextDueDateAsync(task);

        // Dejar rastro en el timeline del lead/deal — una tarea suelta sin entidad
        // asociada (creada desde el Calendario general) no tiene timeline a dónde loguear.
        if (wasOpen && task.AccountId.HasValue && task.EntityType != null && task.EntityId.HasValue)
        {
            await _timeline.LogAsync(task.AccountId.Value, task.EntityType, task.EntityId.Value,
                "task_status", $"Tarea \"{task.Subject}\": {dto.Status}",
                detail: dto.Note, userId: CurrentUserId);
        }

        return Ok(new { task.ActivityId, task.TaskStatus });
    }

    // ─── DELETE /api/tasks/{id} ───────────────────────────────────────────────────

    [HttpDelete("{id}")]
    [SwaggerOperation(Summary = "Eliminar tarea")]
    [SwaggerResponse(200, "Tarea eliminada")]
    [SwaggerResponse(404, "No encontrada")]
    public async Task<IActionResult> DeleteTask(int id)
    {
        var resolvedAccountId = await ResolveAccountId(null);

        var task = await _context.Activities
            .Where(a => a.ActivityId == id && a.ActivityType == "Task"
                        && (IsAdminGlobal || a.AccountId == resolvedAccountId))
            .FirstOrDefaultAsync();

        if (task == null) return NotFound();

        // Soft-delete: marcamos como cancelada para no romper historial
        task.TaskStatus  = "Cancelada";
        task.IsCompleted = false;
        await _context.SaveChangesAsync();
        return Ok(new { deleted = true });
    }
}

// ─── DTOs ────────────────────────────────────────────────────────────────────

public class TaskUpsertDto
{
    public int?      AccountId        { get; set; }
    public string?   Subject          { get; set; }
    public string?   Notes            { get; set; }
    public string?   Priority         { get; set; }   // Alta / Media / Baja
    public DateTime? DueDate          { get; set; }
    public string?   AssignedToUserId { get; set; }
    public string?   EntityType       { get; set; }   // Lead / Deal / Contact / null
    public long?     EntityId         { get; set; }
}

public class TaskStatusDto
{
    public string  Status { get; set; } = "";
    public string? Note   { get; set; }
}
