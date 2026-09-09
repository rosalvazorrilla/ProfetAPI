using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Models;
using ProfetAPI.Services;
using Swashbuckle.AspNetCore.Annotations;
using System.Security.Claims;

namespace ProfetAPI.Controllers;

// ── CRUD de playbooks (secuencias de tareas por cliente, asignables a una o varias cuentas) ──

[Route("api/playbooks")]
[ApiController]
[Authorize]
[SwaggerTag("CRM — Playbooks (secuencias de tareas por cliente)")]
public class PlaybooksController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly PlaybookService _playbooks;
    private readonly IFeatureGateService _featureGate;

    private const string SequenceFeatureCode = "SEQUENCE_AUTOMATION";

    private string? UserId   => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    private string? UserRole => User.FindFirst(ClaimTypes.Role)?.Value;
    private bool IsAdmin     => UserRole == "AdminGlobal";

    public PlaybooksController(ApplicationDbContext db, PlaybookService playbooks, IFeatureGateService featureGate)
    {
        _db          = db;
        _playbooks   = playbooks;
        _featureGate = featureGate;
    }

    /// <summary>AdminGlobal puede pasar customerId directo, o accountId (se resuelve su
    /// Account.CustomerId) — el resto siempre resuelve el customer de su propia cuenta.</summary>
    private async Task<int?> ResolveCustomerId(int? accountId, int? customerId)
    {
        if (IsAdmin)
        {
            if (customerId.HasValue) return customerId;
            if (accountId.HasValue)
                return await _db.Accounts.AsNoTracking().Where(a => a.AccountId == accountId)
                    .Select(a => (int?)a.CustomerId).FirstOrDefaultAsync();
            return null;
        }
        return await _db.AccountInternalUsers.AsNoTracking()
            .Where(u => u.UserId == UserId)
            .Select(u => (int?)u.Account.CustomerId)
            .FirstOrDefaultAsync();
    }

    /// <summary>Toda la sección de Secuencias vive detrás de este candado.</summary>
    private async Task<IActionResult?> RequireSequenceFeatureAsync(int customerId)
    {
        if (await _featureGate.HasFeatureAsync(customerId, SequenceFeatureCode)) return null;
        return StatusCode(403, new { message = "Esta función no está incluida en tu plan.", featureCode = SequenceFeatureCode });
    }

    // GET /api/playbooks
    [HttpGet]
    [SwaggerOperation(Summary = "Listar secuencias del cliente, con las cuentas a las que aplica cada una")]
    public async Task<IActionResult> List([FromQuery] int? accountId, [FromQuery] int? customerId)
    {
        var custId = await ResolveCustomerId(accountId, customerId);
        if (custId == null) return NotFound(new { message = "Sin cuenta asignada." });
        if (await RequireSequenceFeatureAsync(custId.Value) is { } gate1) return gate1;

        var playbooks = await _db.ActivityPlaybooks
            .Where(p => p.CustomerId == custId && !p.Deleted)
            .OrderBy(p => p.Name)
            .Select(p => new
            {
                p.PlaybookId, p.Name, p.Description, p.IsActive, p.GatingMode,
                accounts = p.AccountAssignments.Select(a => new { a.AccountId, accountName = a.Account.Name, a.IsDefault }),
                tasks = p.Tasks.OrderBy(t => t.Order).Select(t => new
                {
                    t.TaskId, t.TaskName, t.ActionType, t.TargetStageId, t.StageId,
                    t.Description, t.Order, t.Priority, t.OffsetDays,
                    t.AutomationMode, t.TemplateId,
                }),
            })
            .ToListAsync();

        return Ok(playbooks);
    }

    // GET /api/playbooks/channel-status
    [HttpGet("channel-status")]
    [SwaggerOperation(Summary = "Si Email/WhatsApp están conectados para activar envío automático en una cuenta puntual")]
    public async Task<IActionResult> ChannelStatus([FromQuery] int? accountId)
    {
        if (accountId == null) return NotFound(new { message = "Falta accountId." });

        var account = await _db.Accounts.AsNoTracking()
            .Where(a => a.AccountId == accountId)
            .Select(a => new { a.CustomerId, a.SmtpEnabled, a.SmtpIsVerified })
            .FirstOrDefaultAsync();
        if (account == null) return NotFound();

        var emailConnected = account.SmtpEnabled == true && account.SmtpIsVerified == true;

        var whatsappNumber = await _db.Customers.AsNoTracking()
            .Where(c => c.Id == account.CustomerId)
            .Select(c => c.WhatsappNumber)
            .FirstOrDefaultAsync();
        var whatsappConnected = !string.IsNullOrWhiteSpace(whatsappNumber);

        return Ok(new { emailConnected, whatsappConnected });
    }

    // GET /api/playbooks/stages
    [HttpGet("stages")]
    [SwaggerOperation(Summary = "Etapas del embudo de una cuenta puntual (para el paso 'Avanzar a etapa')")]
    public async Task<IActionResult> Stages([FromQuery] int? accountId)
    {
        if (accountId == null) return Ok(Array.Empty<object>());

        var funnel = await _db.Funnels.AsNoTracking()
            .Where(f => f.AccountId == accountId)
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
    [SwaggerOperation(Summary = "Obtener una secuencia con sus pasos y cuentas asignadas")]
    public async Task<IActionResult> Get(int id, [FromQuery] int? accountId, [FromQuery] int? customerId)
    {
        var custId = await ResolveCustomerId(accountId, customerId);
        if (custId == null) return NotFound();
        if (await RequireSequenceFeatureAsync(custId.Value) is { } gate2) return gate2;

        var playbook = await _db.ActivityPlaybooks
            .Where(p => p.PlaybookId == id && p.CustomerId == custId && !p.Deleted)
            .Include(p => p.Tasks)
            .Include(p => p.AccountAssignments).ThenInclude(a => a.Account)
            .FirstOrDefaultAsync();

        if (playbook == null) return NotFound();
        return Ok(ToDto(playbook));
    }

    // POST /api/playbooks
    [HttpPost]
    [SwaggerOperation(Summary = "Crear secuencia y asignarla a una o varias cuentas del cliente")]
    public async Task<IActionResult> Create([FromQuery] int? accountId, [FromQuery] int? customerId, [FromBody] SavePlaybookRequest req)
    {
        var custId = await ResolveCustomerId(accountId, customerId);
        if (custId == null) return NotFound(new { message = "Sin cuenta asignada." });
        if (await RequireSequenceFeatureAsync(custId.Value) is { } gate3) return gate3;
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { message = "El nombre es obligatorio." });

        var accountIds = (req.AccountIds ?? []).Distinct().ToList();
        var accountError = await ValidateAccountsAsync(custId.Value, accountIds, req.Tasks ?? []);
        if (accountError != null) return BadRequest(new { message = accountError });

        var stepError = await ValidateAutomationStepsAsync(custId.Value, req.Tasks ?? []);
        if (stepError != null) return BadRequest(new { message = stepError });

        var playbook = new ActivityPlaybook
        {
            CustomerId  = custId.Value,
            AccountId   = accountIds.FirstOrDefault(),
            Name        = req.Name.Trim(),
            Description = req.Description?.Trim(),
            IsActive    = req.IsActive,
            GatingMode  = req.GatingMode == "Block" ? "Block" : "Warn",
            Deleted     = false,
        };
        _db.ActivityPlaybooks.Add(playbook);
        await _db.SaveChangesAsync();

        ApplySteps(playbook.PlaybookId, req.Tasks ?? []);
        await SyncAccountAssignmentsAsync(playbook.PlaybookId, accountIds, req.DefaultForAccountIds ?? []);
        await _db.SaveChangesAsync();

        var saved = await _db.ActivityPlaybooks.Include(p => p.Tasks).Include(p => p.AccountAssignments).ThenInclude(a => a.Account)
            .FirstAsync(p => p.PlaybookId == playbook.PlaybookId);
        return CreatedAtAction(nameof(Get), new { id = playbook.PlaybookId }, ToDto(saved));
    }

    // PUT /api/playbooks/{id}
    [HttpPut("{id:int}")]
    [SwaggerOperation(Summary = "Actualizar secuencia, sus pasos y las cuentas a las que aplica")]
    public async Task<IActionResult> Update(int id, [FromQuery] int? accountId, [FromQuery] int? customerId, [FromBody] SavePlaybookRequest req)
    {
        var custId = await ResolveCustomerId(accountId, customerId);
        if (custId == null) return NotFound();
        if (await RequireSequenceFeatureAsync(custId.Value) is { } gate4) return gate4;
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { message = "El nombre es obligatorio." });

        var accountIds = (req.AccountIds ?? []).Distinct().ToList();
        var accountError = await ValidateAccountsAsync(custId.Value, accountIds, req.Tasks ?? []);
        if (accountError != null) return BadRequest(new { message = accountError });

        var stepError = await ValidateAutomationStepsAsync(custId.Value, req.Tasks ?? []);
        if (stepError != null) return BadRequest(new { message = stepError });

        var playbook = await _db.ActivityPlaybooks
            .Include(p => p.Tasks)
            .FirstOrDefaultAsync(p => p.PlaybookId == id && p.CustomerId == custId && !p.Deleted);
        if (playbook == null) return NotFound();

        playbook.Name        = req.Name.Trim();
        playbook.Description = req.Description?.Trim();
        playbook.IsActive    = req.IsActive;
        playbook.GatingMode  = req.GatingMode == "Block" ? "Block" : "Warn";

        _db.PlaybookTasks.RemoveRange(playbook.Tasks);
        ApplySteps(playbook.PlaybookId, req.Tasks ?? []);
        await SyncAccountAssignmentsAsync(playbook.PlaybookId, accountIds, req.DefaultForAccountIds ?? []);
        await _db.SaveChangesAsync();

        var saved = await _db.ActivityPlaybooks.Include(p => p.Tasks).Include(p => p.AccountAssignments).ThenInclude(a => a.Account)
            .FirstAsync(p => p.PlaybookId == id);
        return Ok(ToDto(saved));
    }

    // PATCH /api/playbooks/{id}/default?accountId=X
    [HttpPatch("{id:int}/default")]
    [SwaggerOperation(Summary = "Marcar esta secuencia como predeterminada para UNA cuenta puntual")]
    public async Task<IActionResult> SetDefault(int id, [FromQuery] int accountId)
    {
        var assignment = await _db.PlaybookAccountAssignments
            .FirstOrDefaultAsync(a => a.PlaybookId == id && a.AccountId == accountId);
        if (assignment == null) return NotFound(new { message = "Esta secuencia no está asignada a esa cuenta." });

        var playbook = await _db.ActivityPlaybooks.FirstOrDefaultAsync(p => p.PlaybookId == id && !p.Deleted);
        if (playbook == null) return NotFound();
        if (await RequireSequenceFeatureAsync(playbook.CustomerId) is { } gate5) return gate5;

        playbook.IsActive = true;
        assignment.IsDefault = true;
        await _db.SaveChangesAsync();
        await SetDefaultExclusive(accountId, id);

        return Ok(new { playbookId = id, accountId, isDefault = true });
    }

    // PATCH /api/playbooks/{id}/toggle
    [HttpPatch("{id:int}/toggle")]
    [SwaggerOperation(Summary = "Activar / desactivar una secuencia (en todas sus cuentas asignadas)")]
    public async Task<IActionResult> Toggle(int id, [FromQuery] int? accountId, [FromQuery] int? customerId)
    {
        var custId = await ResolveCustomerId(accountId, customerId);
        if (custId == null) return NotFound();
        if (await RequireSequenceFeatureAsync(custId.Value) is { } gate6) return gate6;

        var playbook = await _db.ActivityPlaybooks
            .Include(p => p.AccountAssignments)
            .FirstOrDefaultAsync(p => p.PlaybookId == id && p.CustomerId == custId && !p.Deleted);
        if (playbook == null) return NotFound();

        playbook.IsActive = !playbook.IsActive;
        // Una secuencia inactiva no puede seguir siendo predeterminada en ninguna cuenta
        if (!playbook.IsActive)
            foreach (var a in playbook.AccountAssignments) a.IsDefault = false;
        await _db.SaveChangesAsync();

        return Ok(new { playbook.PlaybookId, playbook.IsActive });
    }

    // DELETE /api/playbooks/{id}
    [HttpDelete("{id:int}")]
    [SwaggerOperation(Summary = "Eliminar secuencia (soft delete)")]
    public async Task<IActionResult> Delete(int id, [FromQuery] int? accountId, [FromQuery] int? customerId)
    {
        var custId = await ResolveCustomerId(accountId, customerId);
        if (custId == null) return NotFound();
        if (await RequireSequenceFeatureAsync(custId.Value) is { } gate7) return gate7;

        var playbook = await _db.ActivityPlaybooks
            .Include(p => p.AccountAssignments)
            .FirstOrDefaultAsync(p => p.PlaybookId == id && p.CustomerId == custId && !p.Deleted);
        if (playbook == null) return NotFound();

        playbook.Deleted = true;
        foreach (var a in playbook.AccountAssignments) a.IsDefault = false;
        await _db.SaveChangesAsync();
        return Ok(new { deleted = true });
    }

    // POST /api/playbooks/{id}/apply/{leadId}
    [HttpPost("{id:int}/apply/{leadId:long}")]
    [SwaggerOperation(Summary = "Aplicar manualmente una secuencia a un lead (genera las tareas)")]
    public async Task<IActionResult> Apply(int id, long leadId, [FromQuery] int? accountId)
    {
        if (accountId == null) return NotFound();
        var custId = await _db.Accounts.AsNoTracking().Where(a => a.AccountId == accountId).Select(a => (int?)a.CustomerId).FirstOrDefaultAsync();
        if (custId == null) return NotFound();
        if (await RequireSequenceFeatureAsync(custId.Value) is { } gate8) return gate8;

        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.LeadId == leadId && l.AccountId == accountId);
        if (lead == null) return NotFound(new { message = "Lead no encontrado en la cuenta." });

        var count = await _playbooks.ApplyPlaybookAsync(id, accountId.Value, leadId, lead.OwnerUserId);
        if (count == 0) return NotFound(new { message = "Secuencia no encontrada, sin pasos, o no asignada a esta cuenta." });

        return Ok(new { applied = true, tasksCreated = count });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Deja solo una secuencia predeterminada por cuenta (entre TODAS las
    /// secuencias asignadas a esa cuenta, no solo las de un mismo playbook).</summary>
    private async Task SetDefaultExclusive(int accountId, int keepPlaybookId)
    {
        var others = await _db.PlaybookAccountAssignments
            .Where(a => a.AccountId == accountId && a.IsDefault && a.PlaybookId != keepPlaybookId)
            .ToListAsync();
        foreach (var o in others) o.IsDefault = false;
        if (others.Count > 0) await _db.SaveChangesAsync();
    }

    /// <summary>Las cuentas deben pertenecer al cliente. Si algún paso es de fase Deal
    /// (StageId != null) o "AdvanceStage", la secuencia solo puede asignarse a UNA cuenta
    /// — esos pasos dependen del embudo específico de esa cuenta.</summary>
    private async Task<string?> ValidateAccountsAsync(int customerId, List<int> accountIds, List<PlaybookStepRequest> steps)
    {
        if (accountIds.Count == 0) return "Elige al menos una cuenta a la que aplique esta secuencia.";

        var validCount = await _db.Accounts.CountAsync(a => accountIds.Contains(a.AccountId) && a.CustomerId == customerId);
        if (validCount != accountIds.Count) return "Una de las cuentas elegidas no pertenece a este cliente.";

        var hasDealSteps = steps.Any(s => s.StageId != null || s.ActionType == "AdvanceStage");
        if (hasDealSteps && accountIds.Count > 1)
            return "Esta secuencia tiene pasos de etapa de Deal — esos dependen del embudo de una cuenta específica, así que solo se puede asignar a una cuenta.";

        return null;
    }

    private async Task SyncAccountAssignmentsAsync(int playbookId, List<int> accountIds, List<int> defaultForAccountIds)
    {
        var existing = await _db.PlaybookAccountAssignments.Where(a => a.PlaybookId == playbookId).ToListAsync();
        _db.PlaybookAccountAssignments.RemoveRange(existing);

        foreach (var accId in accountIds)
        {
            _db.PlaybookAccountAssignments.Add(new PlaybookAccountAssignment
            {
                PlaybookId = playbookId,
                AccountId  = accId,
                IsDefault  = defaultForAccountIds.Contains(accId),
            });
        }
        await _db.SaveChangesAsync();

        // Si alguna quedó marcada predeterminada, ninguna OTRA secuencia de esa cuenta
        // se queda predeterminada al mismo tiempo.
        foreach (var accId in accountIds.Where(defaultForAccountIds.Contains))
            await SetDefaultExclusive(accId, playbookId);
    }

    private void ApplySteps(int playbookId, List<PlaybookStepRequest> steps)
    {
        for (int i = 0; i < steps.Count; i++)
        {
            _db.PlaybookTasks.Add(new PlaybookTask
            {
                PlaybookId     = playbookId,
                TaskName       = (steps[i].TaskName ?? "").Trim(),
                ActionType     = string.IsNullOrWhiteSpace(steps[i].ActionType) ? "Task" : steps[i].ActionType!,
                TargetStageId  = steps[i].TargetStageId,
                StageId        = steps[i].StageId,
                Description    = steps[i].Description?.Trim(),
                Order          = i + 1,
                Priority       = string.IsNullOrWhiteSpace(steps[i].Priority) ? "Media" : steps[i].Priority!,
                OffsetDays     = Math.Max(0, steps[i].OffsetDays),
                AutomationMode = steps[i].AutomationMode == "Automatico" ? "Automatico" : "Manual",
                TemplateId     = steps[i].AutomationMode == "Automatico" ? steps[i].TemplateId : null,
            });
        }
    }

    /// <summary>
    /// Antes de guardar un paso en modo "Automatico": el tipo de acción debe admitir
    /// envío (Email/WhatsApp), debe traer una plantilla del mismo canal, y ese canal
    /// debe estar conectado — si no, no se deja activar para no prometer un envío que
    /// nunca va a salir.
    /// </summary>
    private async Task<string?> ValidateAutomationStepsAsync(int customerId, List<PlaybookStepRequest> steps)
    {
        var autoSteps = steps.Where(s => s.AutomationMode == "Automatico").ToList();
        if (autoSteps.Count == 0) return null;

        if (autoSteps.Any(s => s.ActionType != "Email" && s.ActionType != "WhatsApp"))
            return "El envío automático solo aplica a pasos de tipo Email o WhatsApp.";

        if (autoSteps.Any(s => s.TemplateId == null))
            return "Elige una plantilla para cada paso en modo automático.";

        var templateIds = autoSteps.Select(s => s.TemplateId!.Value).Distinct().ToList();
        var templates = await _db.MessageTemplates
            .Where(t => templateIds.Contains(t.TemplateId) && t.CustomerId == customerId)
            .ToDictionaryAsync(t => t.TemplateId);

        foreach (var s in autoSteps)
        {
            if (!templates.TryGetValue(s.TemplateId!.Value, out var tpl))
                return "Una de las plantillas elegidas no existe o no pertenece a este cliente.";
            if (tpl.Channel != s.ActionType)
                return $"La plantilla \"{tpl.Name}\" es de {tpl.Channel}, no coincide con el paso de {s.ActionType}.";
        }

        var whatsappNumber = await _db.Customers.AsNoTracking()
            .Where(c => c.Id == customerId).Select(c => c.WhatsappNumber).FirstOrDefaultAsync();
        var whatsappConnected = !string.IsNullOrWhiteSpace(whatsappNumber);

        if (autoSteps.Any(s => s.ActionType == "WhatsApp") && !whatsappConnected)
            return "Este cliente no tiene WhatsApp conectado — no se puede activar el envío automático de WhatsApp.";

        return null;
    }

    private static object ToDto(ActivityPlaybook p) => new
    {
        p.PlaybookId, p.Name, p.Description, p.IsActive, p.GatingMode,
        accounts = p.AccountAssignments.Select(a => new { a.AccountId, accountName = a.Account.Name, a.IsDefault }),
        tasks = p.Tasks.OrderBy(t => t.Order).Select(t => new
        {
            t.TaskId, t.TaskName, t.ActionType, t.TargetStageId, t.StageId,
            t.Description, t.Order, t.Priority, t.OffsetDays,
            t.AutomationMode, t.TemplateId,
        }),
    };
}

// ── DTOs ────────────────────────────────────────────────────────────────────────

public class SavePlaybookRequest
{
    public string  Name        { get; set; } = "";
    public string? Description  { get; set; }
    public bool    IsActive     { get; set; } = true;
    public string? GatingMode   { get; set; } = "Warn";   // "Block" | "Warn"
    public List<int>? AccountIds { get; set; }            // a qué cuentas del cliente aplica
    public List<int>? DefaultForAccountIds { get; set; }  // en cuáles de esas es la predeterminada
    public List<PlaybookStepRequest>? Tasks { get; set; }
}

public class PlaybookStepRequest
{
    public string? TaskName      { get; set; }
    public string? ActionType    { get; set; }
    public int?    TargetStageId { get; set; }
    public int?    StageId       { get; set; }   // null = fase Lead; con valor = etapa del Deal
    public string? Description   { get; set; }
    public string? Priority      { get; set; }
    public int     OffsetDays    { get; set; }
    public string? AutomationMode { get; set; }  // "Manual" (default) | "Automatico"
    public int?    TemplateId     { get; set; }
}
