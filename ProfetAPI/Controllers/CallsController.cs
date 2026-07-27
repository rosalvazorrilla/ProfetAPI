using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Dtos.Calls;
using ProfetAPI.Models;
using ProfetAPI.Services;
using Swashbuckle.AspNetCore.Annotations;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace ProfetAPI.Controllers;

/// <summary>
/// Integración con CallPicker: click-to-call desde un prospecto y recepción del webhook
/// de llamadas (entrantes/salientes) que CallPicker dispara al terminar cada llamada.
/// </summary>
[Route("api/calls")]
[ApiController]
[SwaggerTag("Llamadas (CallPicker)")]
public class CallsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ITimelineLogger _timeline;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;

    private const string ClickToCallUrl = "https://my.callpicker.com/call_details/chrome_extension_return_call";

    public CallsController(ApplicationDbContext db, ITimelineLogger timeline, IHttpClientFactory httpFactory, IConfiguration config)
    {
        _db = db; _timeline = timeline; _httpFactory = httpFactory; _config = config;
    }

    private string? UserId  => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    private bool    IsAdmin => User.FindFirst(ClaimTypes.Role)?.Value == "AdminGlobal";

    private static string LastTenDigits(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return digits.Length > 10 ? digits[^10..] : digits;
    }

    private async Task<int?> ResolveAccountId(int? accountId)
    {
        if (accountId.HasValue)
        {
            if (IsAdmin) return accountId;
            var ok = await _db.AccountInternalUsers.AnyAsync(u => u.AccountId == accountId && u.UserId == UserId);
            return ok ? accountId : null;
        }
        if (IsAdmin) return null;
        return await _db.AccountInternalUsers.Where(u => u.UserId == UserId)
            .Select(u => (int?)u.AccountId).FirstOrDefaultAsync();
    }

    // GET /api/calls/config
    [HttpGet("config")]
    [Authorize]
    [SwaggerOperation(Summary = "Indica si el usuario actual tiene CallPicker configurado")]
    public async Task<IActionResult> GetConfig()
    {
        var profile = await _db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == UserId);
        var configured = profile != null && !string.IsNullOrWhiteSpace(profile.CallPickerExtension) && !string.IsNullOrWhiteSpace(profile.CallPickerKey);
        return Ok(new CallConfigDto { Configured = configured, Extension = profile?.CallPickerExtension });
    }

    // GET /api/calls/lead/{leadId}
    [HttpGet("lead/{leadId:long}")]
    [Authorize]
    [SwaggerOperation(Summary = "Historial de llamadas de un prospecto")]
    public async Task<IActionResult> GetCallsForLead(long leadId, [FromQuery] int? accountId)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound();

        var lead = await _db.Leads.AsNoTracking().FirstOrDefaultAsync(l => l.LeadId == leadId && l.AccountId == acId);
        if (lead == null) return NotFound(new { message = "Prospecto no encontrado." });

        var calls = await _db.Activities.AsNoTracking()
            .Where(a => a.ActivityType == "Call" && a.EntityType == "Lead" && a.EntityId == leadId)
            .OrderByDescending(a => a.CreatedOn)
            .Select(a => new CallItemDto
            {
                ActivityId = a.ActivityId,
                Direction  = (a.Notes ?? "").Contains("Entrante") ? "in" : "out",
                Status     = a.Notes,
                At         = a.ActivityDate ?? a.CreatedOn,
                Phone      = lead.Phone,
                OwnerName  = _db.UserProfiles.Where(p => p.UserId == a.OwnerUserId)
                    .Select(p => (p.FirstName + " " + p.LastName)).FirstOrDefault(),
                Duration     = a.CallDetail != null ? a.CallDetail.Duration : null,
                RecordingUrl = a.CallDetail != null ? a.CallDetail.RecordingUrl : null,
            })
            .ToListAsync();

        return Ok(calls);
    }

    // POST /api/calls/click
    [HttpPost("click")]
    [Authorize]
    [SwaggerOperation(Summary = "Click-to-call: inicia una llamada saliente hacia el prospecto")]
    public async Task<IActionResult> ClickToCall([FromBody] ClickToCallRequestDto dto)
    {
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.LeadId == dto.LeadId);
        if (lead == null) return NotFound(new { message = "Prospecto no encontrado." });

        var phone = string.IsNullOrWhiteSpace(dto.Phone) ? lead.Phone : dto.Phone;
        if (string.IsNullOrWhiteSpace(phone)) return BadRequest(new { message = "El prospecto no tiene teléfono." });

        var profile = await _db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == UserId);
        if (profile == null || string.IsNullOrWhiteSpace(profile.CallPickerExtension) || string.IsNullOrWhiteSpace(profile.CallPickerKey))
            return BadRequest(new { message = "Tu usuario no tiene configurada una extensión de CallPicker." });

        var phonec = LastTenDigits(phone);
        var data = $"id=0&phone={phonec}&call_type=out&extension={profile.CallPickerExtension}&api={profile.CallPickerKey}";

        try
        {
            var client = _httpFactory.CreateClient();
            var resp = await client.PostAsync(ClickToCallUrl,
                new StringContent(data, Encoding.UTF8, "application/x-www-form-urlencoded"));
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode) return StatusCode(502, new { message = "CallPicker no respondió correctamente." });

            using var json = JsonDocument.Parse(body);
            var result = json.RootElement.TryGetProperty("Response", out var r) ? r.ToString() : body;
            return Ok(new { started = true, response = result });
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { message = "No se pudo iniciar la llamada.", error = ex.Message });
        }
    }

    // POST /api/calls/webhook?token=...
    [HttpPost("webhook")]
    [AllowAnonymous]
    [SwaggerOperation(Summary = "Webhook de CallPicker — registra cada llamada finalizada (entrante o saliente)")]
    public async Task<IActionResult> Webhook([FromQuery] string? token)
    {
        var expected = _config["CallPicker:WebhookToken"];
        if (string.IsNullOrEmpty(expected) || token != expected) return Unauthorized();

        string raw;
        using (var reader = new StreamReader(Request.Body))
            raw = await reader.ReadToEndAsync();

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            string Get(string name) => root.TryGetProperty(name, out var v) ? (v.ValueKind == JsonValueKind.Null ? "" : v.ToString()) : "";

            var callType   = Get("call_type");
            var isInbound  = callType.Contains("in", StringComparison.OrdinalIgnoreCase);
            var rawPhone   = isInbound ? Get("caller_id") : Get("dialed_number");
            var phonec     = LastTenDigits(rawPhone);
            var extension  = isInbound ? Get("answered_by") : Get("dialed_by");
            var duration   = Get("duration");
            var callStatus = Get("call_status");
            var callSid    = Get("call_id");
            var dateStr    = Get("date");
            DateTime.TryParse(dateStr, out var date);
            if (date == default) date = DateTime.UtcNow;

            var profile = await _db.UserProfiles.AsNoTracking()
                .FirstOrDefaultAsync(p => p.CallPickerExtensionName == extension || p.CallPickerExtension == extension);

            int? accountId = null;
            string? ownerUserId = profile?.UserId;
            if (ownerUserId != null)
                accountId = await _db.AccountInternalUsers.Where(u => u.UserId == ownerUserId)
                    .Select(u => (int?)u.AccountId).FirstOrDefaultAsync();

            long? leadId = null;
            if (accountId != null && !string.IsNullOrEmpty(phonec))
            {
                leadId = await _db.Leads.AsNoTracking()
                    .Where(l => l.AccountId == accountId && l.Phone != null && l.Phone.Contains(phonec))
                    .OrderByDescending(l => l.CreatedOn)
                    .Select(l => (long?)l.LeadId)
                    .FirstOrDefaultAsync();
            }

            var activity = new Activity
            {
                ActivityType = "Call",
                EntityType   = leadId != null ? "Lead" : null,
                EntityId     = leadId,
                AccountId    = accountId,
                OwnerUserId  = ownerUserId,
                Subject      = isInbound ? "Llamada entrante" : "Llamada saliente",
                Notes        = $"{(isInbound ? "Entrante" : "Saliente")} · {callStatus} · {phonec}",
                ActivityDate = date,
                IsCompleted  = true,
                CreatedOn    = DateTime.UtcNow,
            };
            _db.Activities.Add(activity);
            await _db.SaveChangesAsync();

            _db.CallDetails.Add(new CallDetail
            {
                ActivityId = activity.ActivityId,
                Duration   = duration,
                CallSid    = callSid,
            });
            await _db.SaveChangesAsync();

            if (leadId != null && accountId != null)
                await _timeline.LogAsync(accountId.Value, "Lead", leadId.Value, "call",
                    isInbound ? "Llamada entrante" : "Llamada saliente",
                    detail: $"{callStatus} · {duration}s · {phonec}", userId: ownerUserId);

            return Ok(new { received = true });
        }
        catch (Exception ex)
        {
            return Ok(new { received = false, error = ex.Message });
        }
    }
}
