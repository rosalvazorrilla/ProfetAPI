using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Models;
using Swashbuckle.AspNetCore.Annotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProfetAPI.Controllers;

/// <summary>
/// Endpoints PÚBLICOS que reciben eventos de webhooks externos.
/// La seguridad es por firma HMAC o token en URL — no requieren JWT.
/// </summary>
[Route("api/receive")]
[ApiController]
[AllowAnonymous]
[SwaggerTag("Webhooks — Receptor público de eventos entrantes")]
public class WebhookReceiverController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory   _http;
    private readonly IConfiguration       _config;
    private readonly ILogger<WebhookReceiverController> _log;

    public WebhookReceiverController(ApplicationDbContext db, IHttpClientFactory http,
        IConfiguration config, ILogger<WebhookReceiverController> log)
    {
        _db     = db;
        _http   = http;
        _config = config;
        _log    = log;
    }

    // ── Meta Lead Ads — Endpoint único (URL registrada en Meta for Developers) ─

    [HttpGet("meta")]
    [SwaggerOperation(Summary = "Verificación del webhook de Meta (endpoint único)")]
    public IActionResult MetaVerifySingle(
        [FromQuery(Name = "hub.mode")]         string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")]    string? challenge)
    {
        var configToken = _config["Meta:WebhookVerifyToken"] ?? "";
        if (mode != "subscribe") return BadRequest("hub.mode inválido.");
        if (verifyToken != configToken) return Unauthorized("hub.verify_token no coincide.");
        return Ok(challenge);
    }

    [HttpPost("meta")]
    [SwaggerOperation(Summary = "Recibir eventos leadgen de Meta Lead Ads (endpoint único — enruta por page_id)")]
    public async Task<IActionResult> MetaEventSingle()
    {
        var rawBody  = await ReadRawBody();
        var appSecret = _config["Meta:AppSecret"] ?? "";
        if (!string.IsNullOrEmpty(appSecret) && !VerifyHmac(appSecret, rawBody))
        {
            _log.LogWarning("Meta HMAC inválida — endpoint único");
            return Unauthorized("Firma inválida.");
        }

        using var doc = ParseJson(rawBody);
        if (doc == null) return BadRequest("JSON inválido.");

        var root = doc.RootElement;
        if (!root.TryGetProperty("object", out var obj) || obj.GetString() != "page") return Ok();
        if (!root.TryGetProperty("entry", out var entries)) return Ok();

        foreach (var entry in entries.EnumerateArray())
        {
            var pageId = entry.TryGetProperty("id", out var pid) ? pid.GetString() : null;
            if (!entry.TryGetProperty("changes", out var changes)) continue;

            foreach (var change in changes.EnumerateArray())
            {
                if (!change.TryGetProperty("field", out var f) || f.GetString() != "leadgen") continue;
                if (!change.TryGetProperty("value", out var val)) continue;

                var formId    = val.TryGetProperty("form_id",    out var fi) ? fi.GetString() : null;
                var leadgenId = val.TryGetProperty("leadgen_id", out var li) ? li.GetString() : null;
                var evPageId  = val.TryGetProperty("page_id",    out var pi) ? pi.GetString() : pageId;

                if (string.IsNullOrEmpty(leadgenId) || string.IsNullOrEmpty(evPageId)) continue;

                // Buscar integración por page_id (y form_id si está configurado)
                var wh = await _db.AccountWebhooks.FirstOrDefaultAsync(w =>
                    w.Platform == "MetaLeadAds" && w.Direction == "Incoming" && w.IsActive &&
                    w.MetaPageId == evPageId &&
                    (w.MetaFormId == null || w.MetaFormId == formId));

                if (wh == null)
                {
                    _log.LogWarning("Sin integración para page_id={PageId} form_id={FormId}", evPageId, formId);
                    continue;
                }

                await ProcessMetaLead(wh, leadgenId);
                await BumpMetrics(wh);
            }
        }

        return Ok();
    }

    // ── Meta Lead Ads — Verificación (legacy por key) ─────────────────────────

    [HttpGet("meta/{key}")]
    [SwaggerOperation(Summary = "Verificación del webhook de Meta por key (legacy)")]
    public async Task<IActionResult> MetaVerify(string key,
        [FromQuery(Name = "hub.mode")]         string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")]    string? challenge)
    {
        var wh = await FindWebhook(key, "MetaLeadAds");
        if (wh == null) return NotFound();
        if (mode != "subscribe") return BadRequest("hub.mode inválido.");
        if (verifyToken != wh.MetaVerifyToken) return Unauthorized("hub.verify_token no coincide.");
        return Ok(challenge);
    }

    // ── Meta Lead Ads — Evento (legacy por key) ───────────────────────────────

    [HttpPost("meta/{key}")]
    [SwaggerOperation(Summary = "Recibir evento leadgen de Meta Lead Ads (legacy por key)")]
    public async Task<IActionResult> MetaEvent(string key)
    {
        var wh = await FindWebhook(key, "MetaLeadAds");
        if (wh == null) return NotFound();

        var rawBody = await ReadRawBody();

        if (!string.IsNullOrEmpty(wh.MetaAppSecret) && !VerifyHmac(wh.MetaAppSecret, rawBody))
        {
            _log.LogWarning("Meta HMAC inválida — webhook {Key}", key);
            return Unauthorized("Firma inválida.");
        }

        using var doc = ParseJson(rawBody);
        if (doc == null) return BadRequest("JSON inválido.");

        var root = doc.RootElement;
        if (!root.TryGetProperty("object", out var obj) || obj.GetString() != "page") return Ok();
        if (!root.TryGetProperty("entry", out var entries)) return Ok();

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes)) continue;
            foreach (var change in changes.EnumerateArray())
            {
                if (change.TryGetProperty("field", out var f) && f.GetString() == "leadgen"
                    && change.TryGetProperty("value", out var val)
                    && val.TryGetProperty("leadgen_id", out var lid))
                {
                    var leadgenId = lid.GetString();
                    if (!string.IsNullOrEmpty(leadgenId))
                        await ProcessMetaLead(wh, leadgenId);
                }
            }
        }

        await BumpMetrics(wh);
        return Ok();
    }

    // ── Custom HTTP ───────────────────────────────────────────────────────────

    [HttpPost("custom/{key}")]
    [SwaggerOperation(Summary = "Webhook HTTP genérico")]
    public async Task<IActionResult> CustomEvent(string key)
    {
        var wh = await FindWebhook(key, "CustomHttp");
        if (wh == null) return NotFound();
        await BumpMetrics(wh);
        return Ok(new { received = true });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<AccountWebhook?> FindWebhook(string key, string platform) =>
        await _db.AccountWebhooks.FirstOrDefaultAsync(w =>
            w.WebhookKey == key && w.Platform == platform &&
            w.Direction == "Incoming" && w.IsActive);

    private async Task<string> ReadRawBody()
    {
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        Request.Body.Seek(0, SeekOrigin.Begin);
        return body;
    }

    private bool VerifyHmac(string secret, string body)
    {
        var sig      = Request.Headers["X-Hub-Signature-256"].FirstOrDefault() ?? "";
        var hash     = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body));
        var expected = "sha256=" + Convert.ToHexString(hash).ToLower();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(sig),
            Encoding.UTF8.GetBytes(expected));
    }

    private static JsonDocument? ParseJson(string body)
    {
        try { return JsonDocument.Parse(body); } catch { return null; }
    }

    private async Task ProcessMetaLead(AccountWebhook wh, string leadgenId)
    {
        if (string.IsNullOrEmpty(wh.MetaPageAccessToken))
        {
            wh.LastError = "Sin PageAccessToken.";
            _db.WebhookEventLogs.Add(new WebhookEventLog { WebhookId = wh.WebhookId, Status = "Error", ExternalId = leadgenId, ErrorMessage = "Sin PageAccessToken." });
            return;
        }

        try
        {
            var client = _http.CreateClient();
            var fields = "field_data,created_time,ad_id,ad_name,adset_name,campaign_name,form_id";
            var url    = $"https://graph.facebook.com/v19.0/{leadgenId}?fields={fields}&access_token={wh.MetaPageAccessToken}";
            var resp   = await client.GetAsync(url);
            var json   = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                var err = $"Graph API {resp.StatusCode}: {json[..Math.Min(json.Length, 200)]}";
                wh.LastError = err;
                _db.WebhookEventLogs.Add(new WebhookEventLog { WebhookId = wh.WebhookId, Status = "Error", ExternalId = leadgenId, ErrorMessage = err });
                return;
            }

            using var doc  = JsonDocument.Parse(json);
            var root       = doc.RootElement;
            var fields_map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (root.TryGetProperty("field_data", out var fd))
                foreach (var f in fd.EnumerateArray())
                {
                    var name = f.TryGetProperty("name",   out var n) ? n.GetString() ?? "" : "";
                    var val  = f.TryGetProperty("values", out var v) && v.GetArrayLength() > 0
                               ? v[0].GetString() ?? "" : "";
                    if (!string.IsNullOrEmpty(name)) fields_map[name] = val;
                }

            // Mapeo: JSON guardado es { metaKey → crmField } (ej: {"full_name":"name","email":"email"})
            // Construimos un diccionario inverso: crmField → valor del campo Meta
            var crmValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(wh.FieldMappingJson))
            {
                try
                {
                    var fmap = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(wh.FieldMappingJson);
                    if (fmap != null)
                        foreach (var (metaKey, crmField) in fmap)
                            if (!string.IsNullOrEmpty(crmField) && !crmField.StartsWith("custom:") &&
                                fields_map.TryGetValue(metaKey, out var val) && !string.IsNullOrEmpty(val))
                                crmValues.TryAdd(crmField, val);
                }
                catch { }
            }

            // Obtener valor de un campo CRM: usa mapping guardado primero, luego fallback a nombres estándar
            bool hasMapping = crmValues.Count > 0;
            string GetCrm(string crmField, params string[] metaFallbacks)
            {
                if (crmValues.TryGetValue(crmField, out var v) && !string.IsNullOrEmpty(v)) return v;
                if (hasMapping) return ""; // hay mapping pero este campo no está mapeado → vacío intencional
                foreach (var k in metaFallbacks)
                    if (fields_map.TryGetValue(k, out var fv) && !string.IsNullOrEmpty(fv)) return fv;
                return "";
            }

            var firstName = GetCrm("first_name", "first_name");
            var lastName  = GetCrm("last_name",  "last_name");
            var fullName  = GetCrm("name", "full_name")
                         .IfEmpty((firstName + " " + lastName).Trim())
                         .IfEmpty("Lead Meta");

            var email    = GetCrm("email",    "email");
            var phone    = GetCrm("phone",    "phone_number", "phone");
            var company  = GetCrm("company",  "company_name", "company");
            var city     = GetCrm("city",     "city");
            var position = GetCrm("position", "job_title", "position");
            var message  = GetCrm("message",  "message", "comments", "comment");

            var summary = fullName;
            if (!string.IsNullOrEmpty(email)) summary += $" · {email}";

            switch (wh.ActionType)
            {
                case "CreateContact":
                    var nameParts = fullName.Split(' ', 2);
                    _db.Contacts.Add(new Contact
                    {
                        FirstName       = nameParts[0],
                        LastName        = nameParts.Length > 1 ? nameParts[1] : null,
                        Email           = email.NullIfEmpty(),
                        PhoneNumber     = phone.NullIfEmpty(),
                        Position        = position.NullIfEmpty(),
                        LifecycleStatus = "Nuevo",
                        CreatedOn       = DateTime.UtcNow,
                    });
                    break;

                case "LogOnly":
                    _log.LogInformation("Meta lead recibido (solo log) — Cuenta {AccountId}, Id {LeadId}", wh.AccountId, leadgenId);
                    break;

                default: // CreateLead
                    _db.Leads.Add(new Lead
                    {
                        AccountId      = wh.AccountId,
                        Name           = fullName,
                        Email          = email.NullIfEmpty(),
                        Phone          = phone.NullIfEmpty(),
                        Company        = company.NullIfEmpty(),
                        Position       = position.NullIfEmpty(),
                        City           = city.NullIfEmpty(),
                        InitialMessage = message.NullIfEmpty(),
                        ProspectSource = "Meta Lead Ads",
                        AdName         = root.TryGetProperty("ad_name",       out var an) ? an.GetString() : null,
                        CampaignName   = root.TryGetProperty("campaign_name", out var cn) ? cn.GetString() : null,
                        StageId        = wh.DestFunnelId,
                        Status         = wh.DestLeadStatus ?? "Nuevo",
                        OriginType     = "Inbound",
                        Active         = true,
                        Deleted        = false,
                        CreatedOn      = DateTime.UtcNow,
                    });
                    break;
            }

            _db.WebhookEventLogs.Add(new WebhookEventLog { WebhookId = wh.WebhookId, Status = "Success", ExternalId = leadgenId, Summary = summary[..Math.Min(summary.Length, 300)] });
            wh.LastError = null;
        }
        catch (Exception ex)
        {
            var err = ex.Message[..Math.Min(ex.Message.Length, 300)];
            wh.LastError = err;
            _db.WebhookEventLogs.Add(new WebhookEventLog { WebhookId = wh.WebhookId, Status = "Error", ExternalId = leadgenId, ErrorMessage = err });
            _log.LogError(ex, "Error procesando Meta lead {Id}", leadgenId);
        }
    }

    private async Task BumpMetrics(AccountWebhook wh)
    {
        wh.LastTriggeredAt = DateTime.UtcNow;
        wh.TriggerCount++;
        await _db.SaveChangesAsync();
    }
}

internal static class StringExtensions
{
    internal static string IfEmpty(this string s, string fallback) =>
        string.IsNullOrWhiteSpace(s) ? fallback : s;
    internal static string? NullIfEmpty(this string s) =>
        string.IsNullOrWhiteSpace(s) ? null : s;
}
