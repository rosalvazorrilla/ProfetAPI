using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Models;
using ProfetAPI.Services;
using Swashbuckle.AspNetCore.Annotations;
using System.Security.Claims;
using System.Text.Json;

namespace ProfetAPI.Controllers;

// ── CRUD de reglas de automatización ─────────────────────────────────────────

[Route("api/automations")]
[ApiController]
[Authorize]
[SwaggerTag("CRM — Automatizaciones (mini-Zapier)")]
public class AutomationsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private string? UserId   => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    private string? UserRole => User.FindFirst(ClaimTypes.Role)?.Value;
    private bool IsAdmin     => UserRole == "AdminGlobal";

    public AutomationsController(ApplicationDbContext db) => _db = db;

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

    // GET /api/automations
    [HttpGet]
    [SwaggerOperation(Summary = "Listar automatizaciones de la cuenta")]
    public async Task<IActionResult> List([FromQuery] int? accountId)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound(new { message = "Sin cuenta asignada." });

        var rules = await _db.AutomationRules
            .Where(r => r.AccountId == acId && !r.Deleted)
            .Include(r => r.Steps.Where(s => s.IsActive).OrderBy(s => s.StepOrder))
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new
            {
                r.RuleId, r.Name, r.IsActive, r.TriggerType, r.TriggerPlatform,
                webhookUrl = r.WebhookKey != null
                    ? $"/api/receive/auto/{r.WebhookKey}"
                    : (string?)null,
                r.ConditionsJson, r.CreatedAt,
                steps = r.Steps.Select(s => new { s.StepId, s.StepOrder, s.StepType, s.ConfigJson, s.IsActive }),
                lastRun = _db.AutomationLogs
                    .Where(l => l.RuleId == r.RuleId)
                    .OrderByDescending(l => l.ExecutedAt)
                    .Select(l => (DateTime?)l.ExecutedAt)
                    .FirstOrDefault(),
            })
            .ToListAsync();

        return Ok(rules);
    }

    // GET /api/automations/{id}
    [HttpGet("{id:int}")]
    [SwaggerOperation(Summary = "Obtener una automatización con sus pasos")]
    public async Task<IActionResult> Get(int id, [FromQuery] int? accountId)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound();

        var rule = await _db.AutomationRules
            .Where(r => r.RuleId == id && r.AccountId == acId && !r.Deleted)
            .Include(r => r.Steps.OrderBy(s => s.StepOrder))
            .FirstOrDefaultAsync();

        if (rule == null) return NotFound();
        return Ok(ToDto(rule));
    }

    // POST /api/automations
    [HttpPost]
    [SwaggerOperation(Summary = "Crear nueva automatización")]
    public async Task<IActionResult> Create([FromQuery] int? accountId, [FromBody] SaveAutomationRequest req)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound(new { message = "Sin cuenta asignada." });

        var rule = new AutomationRule
        {
            AccountId       = acId.Value,
            Name            = req.Name.Trim(),
            IsActive        = req.IsActive,
            TriggerType     = req.TriggerType,
            TriggerPlatform = req.TriggerPlatform?.Trim(),
            ConditionsJson  = req.ConditionsJson,
            WebhookKey      = req.TriggerType == "WebhookIncoming"
                ? Guid.NewGuid().ToString("N")[..12]
                : null,
            CreatedAt = DateTime.UtcNow,
        };
        _db.AutomationRules.Add(rule);
        await _db.SaveChangesAsync();

        ApplySteps(rule.RuleId, req.Steps ?? []);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = rule.RuleId }, ToDto(rule));
    }

    // PUT /api/automations/{id}
    [HttpPut("{id:int}")]
    [SwaggerOperation(Summary = "Actualizar automatización y sus pasos")]
    public async Task<IActionResult> Update(int id, [FromQuery] int? accountId, [FromBody] SaveAutomationRequest req)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound();

        var rule = await _db.AutomationRules
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.RuleId == id && r.AccountId == acId && !r.Deleted);
        if (rule == null) return NotFound();

        rule.Name            = req.Name.Trim();
        rule.IsActive        = req.IsActive;
        rule.TriggerType     = req.TriggerType;
        rule.TriggerPlatform = req.TriggerPlatform?.Trim();
        rule.ConditionsJson  = req.ConditionsJson;

        // Regenerar key si cambia a WebhookIncoming y no tenía
        if (req.TriggerType == "WebhookIncoming" && rule.WebhookKey == null)
            rule.WebhookKey = Guid.NewGuid().ToString("N")[..12];
        else if (req.TriggerType != "WebhookIncoming")
            rule.WebhookKey = null;

        // Replace steps
        _db.AutomationSteps.RemoveRange(rule.Steps);
        ApplySteps(rule.RuleId, req.Steps ?? []);
        await _db.SaveChangesAsync();

        return Ok(ToDto(rule));
    }

    // PATCH /api/automations/{id}/toggle
    [HttpPatch("{id:int}/toggle")]
    [SwaggerOperation(Summary = "Activar / desactivar una automatización")]
    public async Task<IActionResult> Toggle(int id, [FromQuery] int? accountId)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound();

        var rule = await _db.AutomationRules
            .FirstOrDefaultAsync(r => r.RuleId == id && r.AccountId == acId && !r.Deleted);
        if (rule == null) return NotFound();

        rule.IsActive = !rule.IsActive;
        await _db.SaveChangesAsync();
        return Ok(new { rule.RuleId, rule.IsActive });
    }

    // DELETE /api/automations/{id}
    [HttpDelete("{id:int}")]
    [SwaggerOperation(Summary = "Eliminar automatización (soft delete)")]
    public async Task<IActionResult> Delete(int id, [FromQuery] int? accountId)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound();

        var rule = await _db.AutomationRules
            .FirstOrDefaultAsync(r => r.RuleId == id && r.AccountId == acId && !r.Deleted);
        if (rule == null) return NotFound();

        rule.Deleted = true;
        await _db.SaveChangesAsync();
        return Ok(new { deleted = true });
    }

    // GET /api/automations/{id}/logs
    [HttpGet("{id:int}/logs")]
    [SwaggerOperation(Summary = "Historial de ejecuciones de una automatización")]
    public async Task<IActionResult> Logs(int id, [FromQuery] int? accountId, [FromQuery] int page = 1)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound();

        var exists = await _db.AutomationRules.AnyAsync(r => r.RuleId == id && r.AccountId == acId && !r.Deleted);
        if (!exists) return NotFound();

        const int pageSize = 50;
        var logs = await _db.AutomationLogs
            .Where(l => l.RuleId == id)
            .OrderByDescending(l => l.ExecutedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(l => new { l.LogId, l.ExecutedAt, l.Success, l.ErrorMessage, l.StepsResultJson, l.PayloadPreview })
            .ToListAsync();

        return Ok(logs);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ApplySteps(int ruleId, List<StepRequest> steps)
    {
        for (int i = 0; i < steps.Count; i++)
        {
            _db.AutomationSteps.Add(new AutomationStep
            {
                RuleId     = ruleId,
                StepOrder  = i + 1,
                StepType   = steps[i].StepType,
                ConfigJson = steps[i].ConfigJson,
                IsActive   = true,
            });
        }
    }

    private static object ToDto(AutomationRule r) => new
    {
        r.RuleId, r.Name, r.IsActive, r.TriggerType, r.TriggerPlatform,
        webhookUrl  = r.WebhookKey != null ? $"/api/receive/auto/{r.WebhookKey}" : null,
        r.ConditionsJson, r.CreatedAt,
        steps = r.Steps.OrderBy(s => s.StepOrder).Select(s => new
        {
            s.StepId, s.StepOrder, s.StepType, s.ConfigJson, s.IsActive,
        }),
    };
}

// ── Receptor de webhooks externos ────────────────────────────────────────────

[Route("api/receive/auto")]
[ApiController]
[SwaggerTag("CRM — Receptor genérico de webhooks de automatización")]
public class AutomationWebhookReceiverController : ControllerBase
{
    private readonly ApplicationDbContext      _db;
    private readonly AutomationExecutorService _executor;

    public AutomationWebhookReceiverController(ApplicationDbContext db, AutomationExecutorService executor)
    {
        _db       = db;
        _executor = executor;
    }

    /// <summary>
    /// POST /api/receive/auto/{key}
    /// Recibe un payload JSON externo, lo convierte a diccionario y ejecuta la automatización correspondiente.
    /// </summary>
    [HttpPost("{key}")]
    [AllowAnonymous]
    [SwaggerOperation(Summary = "Recibir webhook externo y ejecutar automatización")]
    [SwaggerResponse(200, "Procesado")]
    [SwaggerResponse(404, "Clave no encontrada")]
    public async Task<IActionResult> Receive(string key)
    {
        var rule = await _db.AutomationRules
            .Include(r => r.Steps.Where(s => s.IsActive).OrderBy(s => s.StepOrder))
            .FirstOrDefaultAsync(r => r.WebhookKey == key && r.IsActive && !r.Deleted);

        if (rule == null) return NotFound(new { message = "Webhook no encontrado o inactivo." });

        // Parse body as flat string dictionary
        Dictionary<string, string> fields;
        try
        {
            using var reader = new System.IO.StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            fields = FlattenJson(body);
        }
        catch
        {
            fields = new Dictionary<string, string>();
        }

        // Fire and forget — respond 200 immediately, execute async
        _ = Task.Run(() => _executor.ExecuteRuleAsync(rule, fields));

        return Ok(new { received = true, ruleId = rule.RuleId });
    }

    /// <summary>Aplana un JSON anidado a diccionario plano: "address.city" → "city"</summary>
    private static Dictionary<string, string> FlattenJson(string json)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(json);
            Flatten(doc.RootElement, "", result);
        }
        catch { }
        return result;
    }

    private static void Flatten(JsonElement el, string prefix, Dictionary<string, string> result)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in el.EnumerateObject())
                    Flatten(prop.Value, prop.Name, result);
                break;
            case JsonValueKind.Array:
                var arr = el.EnumerateArray().ToList();
                if (arr.Count == 1)
                    Flatten(arr[0], prefix, result);
                break;
            default:
                if (!string.IsNullOrWhiteSpace(prefix))
                    result[prefix] = el.ToString();
                break;
        }
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

public class SaveAutomationRequest
{
    public string Name            { get; set; } = "";
    public bool   IsActive        { get; set; } = true;
    public string TriggerType     { get; set; } = "WebhookIncoming";
    public string? TriggerPlatform{ get; set; }
    public string? ConditionsJson { get; set; }
    public List<StepRequest>? Steps { get; set; }
}

public class StepRequest
{
    public string  StepType   { get; set; } = "";
    public string? ConfigJson { get; set; }
}
