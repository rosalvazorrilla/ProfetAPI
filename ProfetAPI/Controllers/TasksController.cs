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

    public TasksController(ApplicationDbContext context) => _context = context;

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
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        var resolvedAccountId = await ResolveAccountId(accountId);
        if (resolvedAccountId == null) return BadRequest("No se pudo determinar la cuenta.");

        var q = _context.Activities
            .Where(a => a.ActivityType == "Task" && a.AccountId == resolvedAccountId);

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

        return Ok(new
        {
            total,
            page,
            pageSize,
            stats = new { totalAll, totalPending, totalCompleted, totalOverdue },
            data,
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

        var valid = new[] { "Pendiente", "En progreso", "Completada", "Cancelada" };
        if (!valid.Contains(dto.Status)) return BadRequest($"Estado inválido. Valores permitidos: {string.Join(", ", valid)}");

        task.TaskStatus  = dto.Status;
        task.IsCompleted = dto.Status == "Completada";
        await _context.SaveChangesAsync();
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
    public string Status { get; set; } = "";
}
