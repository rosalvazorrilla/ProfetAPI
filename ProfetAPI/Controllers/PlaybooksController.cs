using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Models;
using ProfetAPI.Services;
using Swashbuckle.AspNetCore.Annotations;
using System.Security.Claims;

namespace ProfetAPI.Controllers;

// ── CRUD de playbooks (secuencias de tareas por cuenta) ──────────────────────────

[Route("api/playbooks")]
[ApiController]
[Authorize]
[SwaggerTag("CRM — Playbooks (secuencias de tareas por cuenta)")]
public class PlaybooksController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly PlaybookService _playbooks;

    private string? UserId   => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    private string? UserRole => User.FindFirst(ClaimTypes.Role)?.Value;
    private bool IsAdmin     => UserRole == "AdminGlobal";

    public PlaybooksController(ApplicationDbContext db, PlaybookService playbooks)
    {
        _db        = db;
        _playbooks = playbooks;
    }

    private async Task<int?> ResolveAccountId(int? accountId)
    {
        if (IsAdmin && accountId.HasValue) return accountId;
        if (!IsAdmin)
            return await _db.AccountInternalUsers
                .Where(u => u.UserId == UserId)
                .Select(u => (int?)u.AccountId)
                .FirstOrDefaultAsync();
        return accountId;
    }

    // GET /api/playbooks
    [HttpGet]
    [SwaggerOperation(Summary = "Listar playbooks de la cuenta")]
    public async Task<IActionResult> List([FromQuery] int? accountId)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound(new { message = "Sin cuenta asignada." });

        var playbooks = await _db.ActivityPlaybooks
            .Where(p => p.AccountId == acId && !p.Deleted)
            .OrderByDescending(p => p.IsDefault).ThenBy(p => p.Name)
            .Select(p => new
            {
                p.PlaybookId, p.Name, p.Description, p.IsActive, p.IsDefault,
                tasks = p.Tasks.OrderBy(t => t.Order).Select(t => new
                {
                    t.TaskId, t.TaskName, t.ActionType, t.TargetStageId,
                    t.Description, t.Order, t.Priority, t.OffsetDays,
                }),
            })
            .ToListAsync();

        return Ok(playbooks);
    }

    // GET /api/playbooks/stages
    [HttpGet("stages")]
    [SwaggerOperation(Summary = "Etapas del embudo de la cuenta (para el paso 'Avanzar a etapa')")]
    public async Task<IActionResult> Stages([FromQuery] int? accountId)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return Ok(Array.Empty<object>());

        var funnel = await _db.Funnels.AsNoTracking()
            .Where(f => f.AccountId == acId)
            .Select(f => (int?)f.FunnelId)
            .FirstOrDefaultAsync();

        if (funnel == null) return Ok(Array.Empty<object>());

        var stages = await _db.Stages.AsNoTracking()
            .Where(s => s.FunnelId == funnel.Value)
            .OrderBy(s => s.Order)
            .Select(s => new { s.StageId, s.Name, s.Order, s.Color })
            .ToListAsync();

        return Ok(stages);
    }

    // GET /api/playbooks/{id}
    [HttpGet("{id:int}")]
    [SwaggerOperation(Summary = "Obtener un playbook con sus pasos")]
    public async Task<IActionResult> Get(int id, [FromQuery] int? accountId)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound();

        var playbook = await _db.ActivityPlaybooks
            .Where(p => p.PlaybookId == id && p.AccountId == acId && !p.Deleted)
            .Include(p => p.Tasks)
            .FirstOrDefaultAsync();

        if (playbook == null) return NotFound();
        return Ok(ToDto(playbook));
    }

    // POST /api/playbooks
    [HttpPost]
    [SwaggerOperation(Summary = "Crear playbook")]
    public async Task<IActionResult> Create([FromQuery] int? accountId, [FromBody] SavePlaybookRequest req)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound(new { message = "Sin cuenta asignada." });
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { message = "El nombre es obligatorio." });

        var playbook = new ActivityPlaybook
        {
            AccountId   = acId.Value,
            Name        = req.Name.Trim(),
            Description = req.Description?.Trim(),
            IsActive    = req.IsActive,
            IsDefault   = req.IsDefault,
            Deleted     = false,
        };
        _db.ActivityPlaybooks.Add(playbook);
        await _db.SaveChangesAsync();

        ApplySteps(playbook.PlaybookId, req.Tasks ?? []);
        await _db.SaveChangesAsync();

        // Si se marca como predeterminado, desmarcar los demás de la cuenta
        if (req.IsDefault)
            await SetDefaultExclusive(acId.Value, playbook.PlaybookId);

        var saved = await _db.ActivityPlaybooks.Include(p => p.Tasks)
            .FirstAsync(p => p.PlaybookId == playbook.PlaybookId);
        return CreatedAtAction(nameof(Get), new { id = playbook.PlaybookId }, ToDto(saved));
    }

    // PUT /api/playbooks/{id}
    [HttpPut("{id:int}")]
    [SwaggerOperation(Summary = "Actualizar playbook y sus pasos")]
    public async Task<IActionResult> Update(int id, [FromQuery] int? accountId, [FromBody] SavePlaybookRequest req)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound();
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { message = "El nombre es obligatorio." });

        var playbook = await _db.ActivityPlaybooks
            .Include(p => p.Tasks)
            .FirstOrDefaultAsync(p => p.PlaybookId == id && p.AccountId == acId && !p.Deleted);
        if (playbook == null) return NotFound();

        playbook.Name        = req.Name.Trim();
        playbook.Description = req.Description?.Trim();
        playbook.IsActive    = req.IsActive;
        playbook.IsDefault   = req.IsDefault;

        _db.PlaybookTasks.RemoveRange(playbook.Tasks);
        ApplySteps(playbook.PlaybookId, req.Tasks ?? []);
        await _db.SaveChangesAsync();

        if (req.IsDefault)
            await SetDefaultExclusive(acId.Value, playbook.PlaybookId);

        var saved = await _db.ActivityPlaybooks.Include(p => p.Tasks)
            .FirstAsync(p => p.PlaybookId == id);
        return Ok(ToDto(saved));
    }

    // PATCH /api/playbooks/{id}/default
    [HttpPatch("{id:int}/default")]
    [SwaggerOperation(Summary = "Marcar como playbook predeterminado de la cuenta")]
    public async Task<IActionResult> SetDefault(int id, [FromQuery] int? accountId)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound();

        var playbook = await _db.ActivityPlaybooks
            .FirstOrDefaultAsync(p => p.PlaybookId == id && p.AccountId == acId && !p.Deleted);
        if (playbook == null) return NotFound();

        playbook.IsDefault = true;
        playbook.IsActive  = true;
        await _db.SaveChangesAsync();
        await SetDefaultExclusive(acId.Value, id);

        return Ok(new { playbook.PlaybookId, playbook.IsDefault });
    }

    // PATCH /api/playbooks/{id}/toggle
    [HttpPatch("{id:int}/toggle")]
    [SwaggerOperation(Summary = "Activar / desactivar un playbook")]
    public async Task<IActionResult> Toggle(int id, [FromQuery] int? accountId)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound();

        var playbook = await _db.ActivityPlaybooks
            .FirstOrDefaultAsync(p => p.PlaybookId == id && p.AccountId == acId && !p.Deleted);
        if (playbook == null) return NotFound();

        playbook.IsActive = !playbook.IsActive;
        // Un playbook inactivo no puede seguir siendo el predeterminado
        if (!playbook.IsActive) playbook.IsDefault = false;
        await _db.SaveChangesAsync();

        return Ok(new { playbook.PlaybookId, playbook.IsActive, playbook.IsDefault });
    }

    // DELETE /api/playbooks/{id}
    [HttpDelete("{id:int}")]
    [SwaggerOperation(Summary = "Eliminar playbook (soft delete)")]
    public async Task<IActionResult> Delete(int id, [FromQuery] int? accountId)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound();

        var playbook = await _db.ActivityPlaybooks
            .FirstOrDefaultAsync(p => p.PlaybookId == id && p.AccountId == acId && !p.Deleted);
        if (playbook == null) return NotFound();

        playbook.Deleted   = true;
        playbook.IsDefault = false;
        await _db.SaveChangesAsync();
        return Ok(new { deleted = true });
    }

    // POST /api/playbooks/{id}/apply/{leadId}
    [HttpPost("{id:int}/apply/{leadId:long}")]
    [SwaggerOperation(Summary = "Aplicar manualmente un playbook a un lead (genera las tareas)")]
    public async Task<IActionResult> Apply(int id, long leadId, [FromQuery] int? accountId)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound();

        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.LeadId == leadId && l.AccountId == acId);
        if (lead == null) return NotFound(new { message = "Lead no encontrado en la cuenta." });

        var count = await _playbooks.ApplyPlaybookAsync(id, acId.Value, leadId, lead.OwnerUserId);
        if (count == 0) return NotFound(new { message = "Playbook no encontrado o sin pasos." });

        return Ok(new { applied = true, tasksCreated = count });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Deja solo un playbook predeterminado por cuenta.</summary>
    private async Task SetDefaultExclusive(int accountId, int keepId)
    {
        var others = await _db.ActivityPlaybooks
            .Where(p => p.AccountId == accountId && p.IsDefault && p.PlaybookId != keepId && !p.Deleted)
            .ToListAsync();
        foreach (var o in others) o.IsDefault = false;
        if (others.Count > 0) await _db.SaveChangesAsync();
    }

    private void ApplySteps(int playbookId, List<PlaybookStepRequest> steps)
    {
        for (int i = 0; i < steps.Count; i++)
        {
            _db.PlaybookTasks.Add(new PlaybookTask
            {
                PlaybookId    = playbookId,
                TaskName      = (steps[i].TaskName ?? "").Trim(),
                ActionType    = string.IsNullOrWhiteSpace(steps[i].ActionType) ? "Task" : steps[i].ActionType!,
                TargetStageId = steps[i].TargetStageId,
                Description   = steps[i].Description?.Trim(),
                Order         = i + 1,
                Priority      = string.IsNullOrWhiteSpace(steps[i].Priority) ? "Media" : steps[i].Priority!,
                OffsetDays    = Math.Max(0, steps[i].OffsetDays),
            });
        }
    }

    private static object ToDto(ActivityPlaybook p) => new
    {
        p.PlaybookId, p.Name, p.Description, p.IsActive, p.IsDefault,
        tasks = p.Tasks.OrderBy(t => t.Order).Select(t => new
        {
            t.TaskId, t.TaskName, t.ActionType, t.TargetStageId,
            t.Description, t.Order, t.Priority, t.OffsetDays,
        }),
    };
}

// ── DTOs ────────────────────────────────────────────────────────────────────────

public class SavePlaybookRequest
{
    public string  Name        { get; set; } = "";
    public string? Description  { get; set; }
    public bool    IsActive     { get; set; } = true;
    public bool    IsDefault    { get; set; } = false;
    public List<PlaybookStepRequest>? Tasks { get; set; }
}

public class PlaybookStepRequest
{
    public string? TaskName      { get; set; }
    public string? ActionType    { get; set; }
    public int?    TargetStageId { get; set; }
    public string? Description   { get; set; }
    public string? Priority      { get; set; }
    public int     OffsetDays    { get; set; }
}
