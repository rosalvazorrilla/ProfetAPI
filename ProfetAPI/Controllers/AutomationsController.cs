using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly SecretProtector _secrets;
    private string? UserId   => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    private string? UserRole => User.FindFirst(ClaimTypes.Role)?.Value;
    private bool IsAdmin     => UserRole == "AdminGlobal";

    public AutomationsController(ApplicationDbContext db, SecretProtector secrets)
    {
        _db      = db;
        _secrets = secrets;
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
                r.VerifyToken, r.MetaPageId, r.MetaFormId, r.MetaPageName, r.MetaFormName,
                hasMetaToken = r.MetaPageToken != null,
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
            VerifyToken     = req.TriggerType == "WebhookIncoming"
                ? Guid.NewGuid().ToString("N")[..16]
                : null,
            MetaPageToken   = string.IsNullOrWhiteSpace(req.MetaPageToken) ? null : _secrets.Protect(req.MetaPageToken.Trim()),
            MetaPageId      = string.IsNullOrWhiteSpace(req.MetaPageId) ? null : req.MetaPageId.Trim(),
            MetaFormId      = string.IsNullOrWhiteSpace(req.MetaFormId) ? null : req.MetaFormId.Trim(),
            MetaPageName    = string.IsNullOrWhiteSpace(req.MetaPageName) ? null : req.MetaPageName.Trim(),
            MetaFormName    = string.IsNullOrWhiteSpace(req.MetaFormName) ? null : req.MetaFormName.Trim(),
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

        // Regenerar key/token si cambia a WebhookIncoming y no tenía
        if (req.TriggerType == "WebhookIncoming")
        {
            rule.WebhookKey  ??= Guid.NewGuid().ToString("N")[..12];
            rule.VerifyToken ??= Guid.NewGuid().ToString("N")[..16];
            // Solo sobrescribir el token de Meta si el front envía uno nuevo (no reenvía el existente)
            if (!string.IsNullOrWhiteSpace(req.MetaPageToken))
                rule.MetaPageToken = _secrets.Protect(req.MetaPageToken.Trim());
            rule.MetaPageId   = string.IsNullOrWhiteSpace(req.MetaPageId) ? null : req.MetaPageId.Trim();
            rule.MetaFormId   = string.IsNullOrWhiteSpace(req.MetaFormId) ? null : req.MetaFormId.Trim();
            rule.MetaPageName = string.IsNullOrWhiteSpace(req.MetaPageName) ? null : req.MetaPageName.Trim();
            rule.MetaFormName = string.IsNullOrWhiteSpace(req.MetaFormName) ? null : req.MetaFormName.Trim();
        }
        else
        {
            rule.WebhookKey    = null;
            rule.VerifyToken   = null;
            rule.MetaPageToken = null;
        }

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
        r.VerifyToken,
        hasMetaToken = r.MetaPageToken != null,
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
    private readonly ApplicationDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory   _httpFactory;
    private readonly SecretProtector      _secrets;

    public AutomationWebhookReceiverController(
        ApplicationDbContext db, IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpFactory, SecretProtector secrets)
    {
        _db           = db;
        _scopeFactory = scopeFactory;
        _httpFactory  = httpFactory;
        _secrets      = secrets;
    }

    /// <summary>
    /// GET /api/receive/auto/{key}
    /// Handshake de verificación estilo Meta Lead Ads:
    /// ?hub.mode=subscribe&amp;hub.verify_token=...&amp;hub.challenge=... → devuelve el challenge si el token coincide.
    /// Así la misma URL sirve como "Callback URL" al conectar Meta (u otra plataforma que verifique).
    /// </summary>
    [HttpGet("{key}")]
    [AllowAnonymous]
    [SwaggerOperation(Summary = "Verificación de webhook (handshake tipo Meta)")]
    public async Task<IActionResult> Verify(string key,
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? token,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        var rule = await _db.AutomationRules.AsNoTracking()
            .FirstOrDefaultAsync(r => r.WebhookKey == key && !r.Deleted);
        if (rule == null) return NotFound();

        // Meta envía GET con hub.* para validar la URL antes de suscribir el webhook
        if (mode == "subscribe" && !string.IsNullOrEmpty(token)
            && !string.IsNullOrEmpty(rule.VerifyToken) && token == rule.VerifyToken)
            return Content(challenge ?? "", "text/plain");

        return Forbid();
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
        var rule = await _db.AutomationRules.AsNoTracking()
            .FirstOrDefaultAsync(r => r.WebhookKey == key && r.IsActive && !r.Deleted);
        if (rule == null) return NotFound(new { message = "Webhook no encontrado o inactivo." });

        // Leer el cuerpo crudo
        string body;
        try
        {
            using var reader = new System.IO.StreamReader(Request.Body);
            body = await reader.ReadToEndAsync();
        }
        catch { body = ""; }

        var ruleId       = rule.RuleId;
        var metaTokenEnc = rule.MetaPageToken;   // cifrado (opcional, override manual)
        var metaPageId   = rule.MetaPageId;
        var ruleAccount  = rule.AccountId;
        var leadgenIds   = ExtractMetaLeadgenIds(body, rule.MetaFormId);

        // Fire and forget con SCOPE PROPIO (evita usar el DbContext del request ya liberado).
        // Responder 200 rápido es requisito de Meta (<5s).
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var db   = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var exec = scope.ServiceProvider.GetRequiredService<AutomationExecutorService>();

            var freshRule = await db.AutomationRules
                .Include(r => r.Steps.Where(s => s.IsActive).OrderBy(s => s.StepOrder))
                .FirstOrDefaultAsync(r => r.RuleId == ruleId);
            if (freshRule == null) return;

            // Resolver el token de Meta: 1) el pegado manualmente (cifrado), o 2) el de la conexión
            //    de Meta existente de la cuenta (AccountWebhooks) según la página elegida.
            var metaToken = _secrets.Unprotect(metaTokenEnc);
            if (string.IsNullOrWhiteSpace(metaToken) && !string.IsNullOrWhiteSpace(metaPageId))
                metaToken = await db.AccountWebhooks.AsNoTracking()
                    .Where(w => w.AccountId == ruleAccount && w.MetaPageId == metaPageId && w.MetaPageAccessToken != null)
                    .Select(w => w.MetaPageAccessToken).FirstOrDefaultAsync();

            if (leadgenIds.Count > 0 && !string.IsNullOrWhiteSpace(metaToken))
            {
                // Meta Lead Ads: cada leadgen_id se resuelve contra la Graph API para traer los campos reales
                foreach (var lid in leadgenIds)
                {
                    var fields = await FetchMetaLeadAsync(lid, metaToken!);
                    if (fields.Count > 0) await exec.ExecuteRuleAsync(freshRule, fields);
                }
            }
            else
            {
                await exec.ExecuteRuleAsync(freshRule, FlattenJson(body));
            }
        });

        return Ok(new { received = true, ruleId });
    }

    /// <summary>Extrae los leadgen_id de un payload de Meta Lead Ads. Si formFilter != null, solo los de ese formulario.</summary>
    private static List<string> ExtractMetaLeadgenIds(string json, string? formFilter = null)
    {
        var ids = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return ids;
            if (!root.TryGetProperty("object", out var obj) || obj.GetString() != "page") return ids;
            if (!root.TryGetProperty("entry", out var entry) || entry.ValueKind != JsonValueKind.Array) return ids;

            foreach (var e in entry.EnumerateArray())
            {
                if (!e.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array) continue;
                foreach (var c in changes.EnumerateArray())
                {
                    if (c.TryGetProperty("value", out var val) &&
                        val.TryGetProperty("leadgen_id", out var lid))
                    {
                        // Filtrar por formulario si la automatización eligió uno
                        if (!string.IsNullOrWhiteSpace(formFilter))
                        {
                            var fid = val.TryGetProperty("form_id", out var f)
                                ? (f.ValueKind == JsonValueKind.String ? f.GetString() : f.ToString()) : null;
                            if (fid != formFilter) continue;
                        }
                        var s = lid.ValueKind == JsonValueKind.String ? lid.GetString() : lid.ToString();
                        if (!string.IsNullOrWhiteSpace(s)) ids.Add(s!);
                    }
                }
            }
        }
        catch { }
        return ids;
    }

    /// <summary>Consulta la Graph API de Meta con el Page Access Token y aplana field_data en un diccionario.</summary>
    private async Task<Dictionary<string, string>> FetchMetaLeadAsync(string leadgenId, string pageToken)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var client = _httpFactory.CreateClient();
            var url = $"https://graph.facebook.com/v21.0/{leadgenId}"
                    + $"?fields=field_data,form_id,ad_id,campaign_id,created_time"
                    + $"&access_token={Uri.EscapeDataString(pageToken)}";
            var resp = await client.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return result;

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("field_data", out var fd) && fd.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in fd.EnumerateArray())
                {
                    var name = f.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    string? value = null;
                    if (f.TryGetProperty("values", out var vs) && vs.ValueKind == JsonValueKind.Array && vs.GetArrayLength() > 0)
                        value = vs[0].GetString();
                    if (!string.IsNullOrWhiteSpace(name)) result[name] = value ?? "";
                }
            }
            foreach (var k in new[] { "form_id", "ad_id", "campaign_id", "created_time" })
                if (root.TryGetProperty(k, out var v)) result[k] = v.ToString();
            result["leadgen_id"] = leadgenId;
            result["prospectSource"] = "Meta Lead Ads";
        }
        catch { }
        return result;
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
    public string? MetaPageToken  { get; set; }
    public string? MetaPageId     { get; set; }
    public string? MetaFormId     { get; set; }
    public string? MetaPageName   { get; set; }
    public string? MetaFormName   { get; set; }
    public string? ConditionsJson { get; set; }
    public List<StepRequest>? Steps { get; set; }
}

public class StepRequest
{
    public string  StepType   { get; set; } = "";
    public string? ConfigJson { get; set; }
}
