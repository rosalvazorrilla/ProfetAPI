using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Models;
using Swashbuckle.AspNetCore.Annotations;
using System.Security.Claims;

namespace ProfetAPI.Controllers;

[Route("api/webhooks")]
[ApiController]
[Authorize]
[SwaggerTag("CRM — Webhooks entrantes y salientes por cuenta")]
public class WebhooksController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory   _http;

    public WebhooksController(ApplicationDbContext db, IHttpClientFactory http)
    {
        _db   = db;
        _http = http;
    }

    private string? UserId   => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private string? UserRole => User.FindFirstValue(ClaimTypes.Role);
    private bool    IsAdmin  => UserRole == "AdminGlobal";

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

    // ── GET /api/webhooks ──────────────────────────────────────────────────────

    [HttpGet]
    [SwaggerOperation(Summary = "Listar webhooks de la cuenta")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetAll([FromQuery] int? accountId, [FromQuery] string? direction)
    {
        var resolved = await ResolveAccountId(accountId);
        if (resolved == null) return BadRequest("No se pudo determinar la cuenta.");

        var q = _db.AccountWebhooks.Where(w => w.AccountId == resolved);
        if (!string.IsNullOrEmpty(direction))
            q = q.Where(w => w.Direction == direction);

        var items = await q
            .OrderByDescending(w => w.CreatedAt)
            .Select(w => new
            {
                w.WebhookId,
                w.AccountId,
                w.Name,
                w.Direction,
                w.Platform,
                w.ActionType,
                w.WebhookKey,
                w.TriggerEvent,
                w.TargetUrl,
                w.IsActive,
                w.MetaAppId,
                w.MetaPageId,
                w.MetaPageName,
                w.MetaFormId,
                w.MetaFormName,
                w.FieldMappingJson,
                w.MetaVerifyToken,
                w.DestFunnelId,
                w.DestLeadStatus,
                HasMetaSecret  = w.MetaAppSecret != null,
                HasPageToken   = w.MetaPageAccessToken != null,
                HasOutSecret   = w.OutgoingSecret != null,
                w.CreatedAt,
                w.LastTriggeredAt,
                w.TriggerCount,
                w.LastError,
            })
            .ToListAsync();

        return Ok(items);
    }

    // ── GET /api/webhooks/{id} ─────────────────────────────────────────────────

    [HttpGet("{id:int}")]
    [SwaggerOperation(Summary = "Detalle de un webhook")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetOne(int id, [FromQuery] int? accountId)
    {
        var resolved = await ResolveAccountId(accountId);
        if (resolved == null) return BadRequest();

        var w = await _db.AccountWebhooks
            .FirstOrDefaultAsync(x => x.WebhookId == id && x.AccountId == resolved);

        if (w == null) return NotFound();
        return Ok(ToDto(w));
    }

    // ── POST /api/webhooks ─────────────────────────────────────────────────────

    [HttpPost]
    [SwaggerOperation(Summary = "Crear webhook (entrante o saliente)")]
    [ProducesResponseType(201)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> Create([FromQuery] int? accountId, [FromBody] SaveWebhookRequest req)
    {
        var resolved = await ResolveAccountId(accountId);
        if (resolved == null) return BadRequest("No se pudo determinar la cuenta.");

        if (string.IsNullOrWhiteSpace(req.Name))      return BadRequest("Name es requerido.");
        if (string.IsNullOrWhiteSpace(req.Direction))  return BadRequest("Direction es requerido.");

        var wh = BuildWebhook(req, resolved.Value);
        _db.AccountWebhooks.Add(wh);
        await _db.SaveChangesAsync();

        bool metaSubscribed = false;
        if (wh.Platform == "MetaLeadAds" && !string.IsNullOrEmpty(wh.MetaPageId) && !string.IsNullOrEmpty(wh.MetaPageAccessToken))
            metaSubscribed = await SubscribeMetaPage(wh.MetaPageId, wh.MetaPageAccessToken);

        return CreatedAtAction(nameof(GetOne), new { id = wh.WebhookId },
            new { wh.WebhookId, wh.WebhookKey, wh.MetaVerifyToken, wh.Direction, metaSubscribed });
    }

    // ── PUT /api/webhooks/{id} ─────────────────────────────────────────────────

    [HttpPut("{id:int}")]
    [SwaggerOperation(Summary = "Actualizar webhook")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Update(int id, [FromQuery] int? accountId, [FromBody] SaveWebhookRequest req)
    {
        var resolved = await ResolveAccountId(accountId);
        if (resolved == null) return BadRequest();

        var wh = await _db.AccountWebhooks
            .FirstOrDefaultAsync(x => x.WebhookId == id && x.AccountId == resolved);
        if (wh == null) return NotFound();

        ApplyUpdate(wh, req);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── DELETE /api/webhooks/{id} ──────────────────────────────────────────────

    [HttpDelete("{id:int}")]
    [SwaggerOperation(Summary = "Eliminar webhook")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Delete(int id, [FromQuery] int? accountId)
    {
        var resolved = await ResolveAccountId(accountId);
        if (resolved == null) return BadRequest();

        var wh = await _db.AccountWebhooks
            .FirstOrDefaultAsync(x => x.WebhookId == id && x.AccountId == resolved);
        if (wh == null) return NotFound();

        _db.AccountWebhooks.Remove(wh);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── POST /api/webhooks/{id}/meta/clone ────────────────────────────────────

    [HttpPost("{id:int}/meta/clone")]
    [SwaggerOperation(Summary = "Crear nueva integración Meta reutilizando la conexión de página de una existente")]
    [ProducesResponseType(201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> CloneMeta(int id, [FromQuery] int? accountId, [FromBody] CloneMetaRequest req)
    {
        var resolved = await ResolveAccountId(accountId);
        if (resolved == null) return BadRequest();

        var source = await _db.AccountWebhooks
            .FirstOrDefaultAsync(x => x.WebhookId == id && x.AccountId == resolved && x.Platform == "MetaLeadAds");
        if (source == null) return NotFound();
        if (string.IsNullOrEmpty(source.MetaPageId) || string.IsNullOrEmpty(source.MetaPageAccessToken))
            return BadRequest("La integración fuente no tiene página de Meta configurada.");
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest("Name es requerido.");

        var wh = new AccountWebhook
        {
            AccountId           = resolved.Value,
            Name                = req.Name.Trim(),
            Direction           = "Incoming",
            Platform            = "MetaLeadAds",
            ActionType          = req.ActionType ?? "CreateLead",
            IsActive            = true,
            WebhookKey          = Guid.NewGuid().ToString("N"),
            MetaAppId           = source.MetaAppId,
            MetaAppSecret       = source.MetaAppSecret,
            MetaVerifyToken     = Guid.NewGuid().ToString("N"),
            MetaPageId          = source.MetaPageId,
            MetaPageName        = source.MetaPageName,
            MetaPageAccessToken = source.MetaPageAccessToken,
            MetaFormId          = req.FormId?.Trim(),
            MetaFormName        = req.FormName?.Trim(),
            FieldMappingJson    = req.FieldMappingJson?.Trim(),
            DestLeadStatus      = req.DestLeadStatus ?? "Nuevo",
            CreatedAt           = DateTime.UtcNow,
        };

        _db.AccountWebhooks.Add(wh);
        await _db.SaveChangesAsync();

        await SubscribeMetaPage(wh.MetaPageId, wh.MetaPageAccessToken);

        return CreatedAtAction(nameof(GetOne), new { id = wh.WebhookId },
            new { wh.WebhookId, wh.Direction, metaSubscribed = true });
    }

    // ── POST /api/webhooks/{id}/meta/subscribe ────────────────────────────────

    [HttpPost("{id:int}/meta/subscribe")]
    [SwaggerOperation(Summary = "Suscribir (o re-suscribir) la página de Meta al webhook de la app")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> MetaSubscribe(int id, [FromQuery] int? accountId)
    {
        var resolved = await ResolveAccountId(accountId);
        if (resolved == null) return BadRequest();

        var wh = await _db.AccountWebhooks.FirstOrDefaultAsync(x => x.WebhookId == id && x.AccountId == resolved);
        if (wh == null) return NotFound();
        if (wh.Platform != "MetaLeadAds") return BadRequest("Solo aplica para integraciones de Meta Lead Ads.");
        if (string.IsNullOrEmpty(wh.MetaPageId) || string.IsNullOrEmpty(wh.MetaPageAccessToken))
            return BadRequest("La integración no tiene página de Meta configurada.");

        var ok = await SubscribeMetaPage(wh.MetaPageId, wh.MetaPageAccessToken);
        return ok ? Ok(new { subscribed = true }) : BadRequest(new { subscribed = false, error = "Meta no aceptó la suscripción. Verifica que el token de página siga vigente." });
    }

    // ── GET /api/webhooks/{id}/events ─────────────────────────────────────────

    [HttpGet("{id:int}/events")]
    [SwaggerOperation(Summary = "Historial de eventos recibidos por un webhook")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetEvents(int id, [FromQuery] int? accountId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var resolved = await ResolveAccountId(accountId);
        if (resolved == null) return BadRequest();

        var exists = await _db.AccountWebhooks.AnyAsync(x => x.WebhookId == id && x.AccountId == resolved);
        if (!exists) return NotFound();

        pageSize = Math.Clamp(pageSize, 1, 100);
        var skip = (page - 1) * pageSize;

        var events = await _db.WebhookEventLogs
            .Where(e => e.WebhookId == id)
            .OrderByDescending(e => e.ReceivedAt)
            .Skip(skip)
            .Take(pageSize)
            .Select(e => new { e.EventLogId, e.ReceivedAt, e.Status, e.Summary, e.ExternalId, e.ErrorMessage })
            .ToListAsync();

        var total = await _db.WebhookEventLogs.CountAsync(e => e.WebhookId == id);

        return Ok(new { total, page, pageSize, events });
    }

    // ── GET /api/webhooks/{id}/meta/forms ─────────────────────────────────────

    [HttpGet("{id:int}/meta/forms")]
    [SwaggerOperation(Summary = "Obtener formularios disponibles para el webhook de Meta")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetMetaForms(int id, [FromQuery] int? accountId)
    {
        var resolved = await ResolveAccountId(accountId);
        if (resolved == null) return BadRequest();

        var wh = await _db.AccountWebhooks
            .FirstOrDefaultAsync(x => x.WebhookId == id && x.AccountId == resolved);
        if (wh == null) return NotFound();
        if (string.IsNullOrEmpty(wh.MetaPageAccessToken) || string.IsNullOrEmpty(wh.MetaPageId))
            return BadRequest("Esta integración no tiene página de Meta configurada.");

        var client = _http.CreateClient();
        var forms  = new List<object>();
        string? next = $"https://graph.facebook.com/v19.0/{wh.MetaPageId}/leadgen_forms" +
                       $"?fields=id,name,status&limit=100&access_token={wh.MetaPageAccessToken}";

        while (next != null)
        {
            var resp = await client.GetAsync(next);
            var json = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return BadRequest("No se pudieron obtener los formularios de Meta.");

            using var doc  = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("data", out var data))
                foreach (var f in data.EnumerateArray())
                    forms.Add(new {
                        FormId = f.GetProperty("id").GetString(),
                        Name   = f.GetProperty("name").GetString(),
                        Status = f.TryGetProperty("status", out var s) ? s.GetString() : "",
                    });

            next = null;
            if (root.TryGetProperty("paging", out var paging) &&
                paging.TryGetProperty("next", out var n))
                next = n.GetString();
        }

        return Ok(forms);
    }

    // ── GET /api/webhooks/{id}/meta/account-fields ───────────────────────────────

    [HttpGet("{id:int}/meta/account-fields")]
    [SwaggerOperation(Summary = "Campos activados y disponibles del account para el mapeo de formulario Meta")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetAccountFields(int id, [FromQuery] int? accountId)
    {
        var resolved = await ResolveAccountId(accountId);
        if (resolved == null) return BadRequest();

        if (id != 0)
        {
            var exists = await _db.AccountWebhooks.AnyAsync(x => x.WebhookId == id && x.AccountId == resolved);
            if (!exists) return NotFound();
        }

        // Campos que el account ya tiene activados
        var activated = await _db.AccountCustomFields
            .Where(a => a.AccountId == resolved)
            .Select(a => new { a.FieldId, a.CustomFieldDefinition.FieldCode, a.CustomFieldDefinition.FieldName, a.CustomFieldDefinition.FieldType })
            .OrderBy(a => a.FieldName)
            .ToListAsync();

        var activatedIds = activated.Select(a => a.FieldId).ToHashSet();

        // Campos del pool global que aún NO tiene activados (excluye sistema)
        var available = await _db.CustomFieldDefinitions
            .Where(f => !f.IsSystem && !activatedIds.Contains(f.FieldId))
            .Select(f => new { f.FieldId, f.FieldCode, f.FieldName, f.FieldType })
            .OrderBy(f => f.FieldName)
            .ToListAsync();

        return Ok(new { activated, available });
    }

    // ── POST /api/webhooks/{id}/meta/account-fields/activate ─────────────────────

    [HttpPost("{id:int}/meta/account-fields/activate")]
    [SwaggerOperation(Summary = "Activa un campo para el account (y opcionalmente lo crea en el pool global)")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> ActivateAccountField(int id, [FromQuery] int? accountId, [FromBody] ActivateFieldRequest req)
    {
        var resolved = await ResolveAccountId(accountId);
        if (resolved == null) return BadRequest();

        if (id != 0)
        {
            var exists = await _db.AccountWebhooks.AnyAsync(x => x.WebhookId == id && x.AccountId == resolved);
            if (!exists) return NotFound();
        }

        int fieldId;

        if (req.FieldId.HasValue)
        {
            // Activar campo existente del pool
            fieldId = req.FieldId.Value;
            var def = await _db.CustomFieldDefinitions.FindAsync(fieldId);
            if (def == null) return BadRequest("Campo no encontrado en el pool global.");
        }
        else
        {
            // Crear nuevo campo en el pool global y activarlo
            if (string.IsNullOrWhiteSpace(req.FieldName)) return BadRequest("FieldName es requerido para crear un nuevo campo.");
            var fieldCode = (req.FieldCode?.Trim().ToLower() ?? req.FieldName!.Trim().ToLower())
                .Replace(" ", "_");

            // Evitar duplicados de código
            var suffix = 0;
            var baseCode = fieldCode;
            while (await _db.CustomFieldDefinitions.AnyAsync(f => f.FieldCode == fieldCode))
                fieldCode = $"{baseCode}_{++suffix}";

            var newDef = new CustomFieldDefinition { FieldCode = fieldCode, FieldName = req.FieldName.Trim(), FieldType = "Text" };
            _db.CustomFieldDefinitions.Add(newDef);
            await _db.SaveChangesAsync();
            fieldId = newDef.FieldId;
        }

        // Activar si aún no está activado
        var alreadyActive = await _db.AccountCustomFields.AnyAsync(a => a.AccountId == resolved && a.FieldId == fieldId);
        if (!alreadyActive)
        {
            _db.AccountCustomFields.Add(new AccountCustomField { AccountId = resolved.Value, FieldId = fieldId, IsVisibleOnCard = false });
            await _db.SaveChangesAsync();
        }

        var result = await _db.CustomFieldDefinitions
            .Where(f => f.FieldId == fieldId)
            .Select(f => new { f.FieldId, f.FieldCode, f.FieldName, f.FieldType })
            .FirstAsync();

        return Ok(result);
    }

    // ── GET /api/webhooks/{id}/meta/form-questions?formId= ───────────────────────

    [HttpGet("{id:int}/meta/form-questions")]
    [SwaggerOperation(Summary = "Obtener preguntas de un formulario de Meta Lead Ads (para mapeo de campos)")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetMetaFormQuestions(int id, [FromQuery] int? accountId, [FromQuery] string? formId)
    {
        var resolved = await ResolveAccountId(accountId);
        if (resolved == null) return BadRequest();

        var wh = await _db.AccountWebhooks
            .FirstOrDefaultAsync(x => x.WebhookId == id && x.AccountId == resolved);
        if (wh == null) return NotFound();
        if (string.IsNullOrEmpty(wh.MetaPageAccessToken))
            return BadRequest("Esta integración no tiene token de página configurado.");

        var targetFormId = formId ?? wh.MetaFormId;
        if (string.IsNullOrEmpty(targetFormId))
            return BadRequest("Se requiere formId.");

        var client = _http.CreateClient();
        var url    = $"https://graph.facebook.com/v19.0/{targetFormId}?fields=name,questions&access_token={wh.MetaPageAccessToken}";
        var resp   = await client.GetAsync(url);
        var json   = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            return BadRequest($"Error de Meta Graph API: {json[..Math.Min(json.Length, 200)]}");

        using var doc  = System.Text.Json.JsonDocument.Parse(json);
        var root       = doc.RootElement;
        var questions  = new List<object>();

        if (root.TryGetProperty("questions", out var qs))
            foreach (var q in qs.EnumerateArray())
            {
                var key   = q.TryGetProperty("key",   out var k) ? k.GetString() : null;
                var label = q.TryGetProperty("label", out var l) ? l.GetString() : null;
                var type  = q.TryGetProperty("type",  out var t) ? t.GetString() : null;
                if (!string.IsNullOrEmpty(key))
                    questions.Add(new { key, label = label ?? key, type = type ?? "CUSTOM" });
            }

        return Ok(new { formId = targetFormId, questions });
    }

    // ── PATCH /api/webhooks/{id}/toggle ───────────────────────────────────────

    [HttpPatch("{id:int}/toggle")]
    [SwaggerOperation(Summary = "Activar o desactivar un webhook")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Toggle(int id, [FromQuery] int? accountId)
    {
        var resolved = await ResolveAccountId(accountId);
        if (resolved == null) return BadRequest();

        var wh = await _db.AccountWebhooks
            .FirstOrDefaultAsync(x => x.WebhookId == id && x.AccountId == resolved);
        if (wh == null) return NotFound();

        wh.IsActive = !wh.IsActive;
        await _db.SaveChangesAsync();
        return Ok(new { wh.WebhookId, wh.IsActive });
    }

    // ── PUT /api/webhooks/{id}/formatter ──────────────────────────────────────

    [HttpPut("{id:int}/formatter")]
    [SwaggerOperation(Summary = "Guardar reglas de transformación (formatter) del webhook")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> SaveFormatter(int id, [FromQuery] int? accountId, [FromBody] FormatterSaveRequest req)
    {
        var resolved = await ResolveAccountId(accountId);
        if (resolved == null) return BadRequest();

        var wh = await _db.AccountWebhooks
            .FirstOrDefaultAsync(x => x.WebhookId == id && x.AccountId == resolved);
        if (wh == null) return NotFound();

        wh.FormatterJson = req.FormatterJson?.Trim();
        await _db.SaveChangesAsync();
        return Ok(new { wh.WebhookId, saved = true });
    }

    // ── POST /api/webhooks/{id}/regenerate-key ─────────────────────────────────

    [HttpPost("{id:int}/regenerate-key")]
    [SwaggerOperation(Summary = "Regenerar URL key del webhook entrante")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> RegenerateKey(int id, [FromQuery] int? accountId)
    {
        var resolved = await ResolveAccountId(accountId);
        if (resolved == null) return BadRequest();

        var wh = await _db.AccountWebhooks
            .FirstOrDefaultAsync(x => x.WebhookId == id && x.AccountId == resolved);
        if (wh == null) return NotFound();

        wh.WebhookKey = Guid.NewGuid().ToString("N");
        await _db.SaveChangesAsync();
        return Ok(new { wh.WebhookKey });
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<bool> SubscribeMetaPage(string pageId, string pageAccessToken)
    {
        try
        {
            var client  = _http.CreateClient();
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["subscribed_fields"] = "leadgen",
                ["access_token"]      = pageAccessToken,
            });
            var resp = await client.PostAsync(
                $"https://graph.facebook.com/v19.0/{pageId}/subscribed_apps", content);
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("success", out var s) && s.GetBoolean();
        }
        catch { return false; }
    }

    private static AccountWebhook BuildWebhook(SaveWebhookRequest r, int accountId) => new()
    {
        AccountId            = accountId,
        Name                 = r.Name.Trim(),
        Direction            = r.Direction,
        IsActive             = r.IsActive,
        // Incoming
        Platform             = r.Platform?.Trim(),
        ActionType           = r.ActionType?.Trim() ?? "CreateLead",
        WebhookKey           = r.Direction == "Incoming" ? Guid.NewGuid().ToString("N") : null,
        MetaAppId            = r.MetaAppId?.Trim(),
        MetaAppSecret        = r.MetaAppSecret?.Trim(),
        MetaVerifyToken      = r.MetaVerifyToken?.Trim() ?? (r.Direction == "Incoming" ? Guid.NewGuid().ToString("N") : null),
        MetaPageAccessToken  = r.MetaPageAccessToken?.Trim(),
        MetaPageId           = r.MetaPageId?.Trim(),
        MetaPageName         = r.MetaPageName?.Trim(),
        MetaFormId           = r.MetaFormId?.Trim(),
        MetaFormName         = r.MetaFormName?.Trim(),
        FieldMappingJson     = r.FieldMappingJson?.Trim(),
        FormatterJson        = r.FormatterJson?.Trim(),
        MetaAdAccountId      = r.MetaAdAccountId?.Trim(),
        DestFunnelId         = r.DestFunnelId,
        DestLeadStatus       = r.DestLeadStatus ?? "Nuevo",
        // Outgoing
        TriggerEvent         = r.TriggerEvent?.Trim(),
        TargetUrl            = r.TargetUrl?.Trim(),
        OutgoingSecret       = r.OutgoingSecret?.Trim(),
        CreatedAt            = DateTime.UtcNow,
    };

    private static void ApplyUpdate(AccountWebhook wh, SaveWebhookRequest r)
    {
        if (!string.IsNullOrWhiteSpace(r.Name))  wh.Name     = r.Name.Trim();
        wh.IsActive = r.IsActive;

        if (r.Platform    != null) wh.Platform    = r.Platform.Trim();
        if (r.ActionType  != null) wh.ActionType  = r.ActionType.Trim();
        if (r.DestFunnelId.HasValue) wh.DestFunnelId  = r.DestFunnelId;
        if (r.DestLeadStatus != null) wh.DestLeadStatus = r.DestLeadStatus;

        if (r.MetaAppId            != null) wh.MetaAppId            = r.MetaAppId.Trim();
        if (r.MetaAppSecret        != null) wh.MetaAppSecret        = r.MetaAppSecret.Trim();
        if (r.MetaVerifyToken      != null) wh.MetaVerifyToken      = r.MetaVerifyToken.Trim();
        if (r.MetaPageAccessToken  != null) wh.MetaPageAccessToken  = r.MetaPageAccessToken.Trim();
        if (r.MetaPageId           != null) wh.MetaPageId           = r.MetaPageId.Trim();
        if (r.MetaPageName         != null) wh.MetaPageName         = r.MetaPageName.Trim();
        if (r.MetaFormId           != null) wh.MetaFormId           = r.MetaFormId.Trim();
        if (r.MetaFormName         != null) wh.MetaFormName         = r.MetaFormName.Trim();
        if (r.FieldMappingJson     != null) wh.FieldMappingJson     = r.FieldMappingJson.Trim();
        if (r.FormatterJson        != null) wh.FormatterJson        = r.FormatterJson.Trim();
        if (r.MetaAdAccountId      != null) wh.MetaAdAccountId      = r.MetaAdAccountId.Trim();

        if (r.TriggerEvent   != null) wh.TriggerEvent   = r.TriggerEvent.Trim();
        if (r.TargetUrl      != null) wh.TargetUrl      = r.TargetUrl.Trim();
        if (r.OutgoingSecret != null) wh.OutgoingSecret = r.OutgoingSecret.Trim();
    }

    private static object ToDto(AccountWebhook w) => new
    {
        w.WebhookId, w.AccountId, w.Name, w.Direction, w.Platform, w.ActionType,
        w.WebhookKey, w.MetaAppId, w.MetaPageId, w.MetaPageName, w.MetaFormId, w.MetaFormName,
        w.FieldMappingJson, w.FormatterJson, w.MetaAdAccountId, w.MetaVerifyToken,
        w.DestFunnelId, w.DestLeadStatus,
        w.TriggerEvent, w.TargetUrl,
        w.IsActive, w.CreatedAt, w.LastTriggeredAt, w.TriggerCount, w.LastError,
        HasMetaSecret = w.MetaAppSecret != null,
        HasPageToken  = w.MetaPageAccessToken != null,
        HasOutSecret  = w.OutgoingSecret != null,
    };
}

// ── DTOs ───────────────────────────────────────────────────────────────────────

public record CloneMetaRequest(
    string  Name,
    string? FormId          = null,
    string? FormName        = null,
    string? ActionType      = "CreateLead",
    string? DestLeadStatus  = "Nuevo",
    string? FieldMappingJson = null
);

public record SaveWebhookRequest(
    string  Name,
    string  Direction,
    bool    IsActive            = true,
    // Incoming
    string? Platform            = null,
    string? ActionType          = "CreateLead",
    string? MetaAppId           = null,
    string? MetaAppSecret       = null,
    string? MetaVerifyToken     = null,
    string? MetaPageAccessToken = null,
    string? MetaPageId          = null,
    string? MetaPageName        = null,
    string? MetaFormId          = null,
    string? MetaFormName        = null,
    string? FieldMappingJson    = null,
    string? FormatterJson       = null,
    string? MetaAdAccountId     = null,
    int?    DestFunnelId        = null,
    string? DestLeadStatus      = "Nuevo",
    // Outgoing
    string? TriggerEvent        = null,
    string? TargetUrl           = null,
    string? OutgoingSecret      = null
);

public class FormatterSaveRequest
{
    public string? FormatterJson { get; set; }
}

public class ActivateFieldRequest
{
    public int?    FieldId   { get; set; }   // Activar campo existente del pool
    public string? FieldCode { get; set; }   // Para campo nuevo (opcional, se genera desde FieldName si no viene)
    public string? FieldName { get; set; }   // Para campo nuevo
}
