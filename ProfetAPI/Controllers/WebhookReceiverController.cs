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
    private readonly ILogger<WebhookReceiverController> _log;

    public WebhookReceiverController(ApplicationDbContext db, IHttpClientFactory http,
        ILogger<WebhookReceiverController> log)
    {
        _db   = db;
        _http = http;
        _log  = log;
    }

    // ── Meta Lead Ads — Verificación ──────────────────────────────────────────

    [HttpGet("meta/{key}")]
    [SwaggerOperation(Summary = "Verificación del webhook de Meta (hub.challenge)")]
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

    // ── Meta Lead Ads — Evento ────────────────────────────────────────────────

    [HttpPost("meta/{key}")]
    [SwaggerOperation(Summary = "Recibir evento leadgen de Meta Lead Ads")]
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
                wh.LastError = $"Graph API {resp.StatusCode}: {json[..Math.Min(json.Length, 200)]}";
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

            var fullName = fields_map.GetValueOrDefault("full_name")
                        ?? (fields_map.GetValueOrDefault("first_name", "") + " " +
                            fields_map.GetValueOrDefault("last_name",  "")).Trim();

            switch (wh.ActionType)
            {
                case "CreateContact":
                    var nameParts = (fullName.Length > 0 ? fullName : "Contacto Meta").Split(' ', 2);
                    _db.Contacts.Add(new Contact
                    {
                        FirstName       = nameParts[0],
                        LastName        = nameParts.Length > 1 ? nameParts[1] : null,
                        Email           = fields_map.GetValueOrDefault("email"),
                        PhoneNumber     = fields_map.GetValueOrDefault("phone_number") ?? fields_map.GetValueOrDefault("phone"),
                        Position        = fields_map.GetValueOrDefault("job_title") ?? fields_map.GetValueOrDefault("position"),
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
                        Name           = string.IsNullOrWhiteSpace(fullName) ? "Lead Meta" : fullName,
                        Email          = fields_map.GetValueOrDefault("email"),
                        Phone          = fields_map.GetValueOrDefault("phone_number") ?? fields_map.GetValueOrDefault("phone"),
                        Company        = fields_map.GetValueOrDefault("company_name") ?? fields_map.GetValueOrDefault("company"),
                        Position       = fields_map.GetValueOrDefault("job_title")   ?? fields_map.GetValueOrDefault("position"),
                        City           = fields_map.GetValueOrDefault("city"),
                        ProspectSource = "Meta Lead Ads",
                        AdName         = root.TryGetProperty("ad_name",      out var an) ? an.GetString() : null,
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

            wh.LastError = null;
        }
        catch (Exception ex)
        {
            wh.LastError = ex.Message[..Math.Min(ex.Message.Length, 300)];
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
