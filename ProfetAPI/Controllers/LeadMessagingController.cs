using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace ProfetAPI.Controllers;

public class DraftMessageDto
{
    /// <summary>"Email" | "WhatsApp".</summary>
    public string Channel { get; set; } = "WhatsApp";
    /// <summary>Plantilla de partida (opcional): la IA la personaliza para este prospecto.</summary>
    public int? TemplateId { get; set; }
    /// <summary>Indicación libre del vendedor (opcional): "dale seguimiento a la cotización".</summary>
    public string? Instruction { get; set; }
}

public class SendLeadMessageDto
{
    public string Channel { get; set; } = "WhatsApp";
    public string? Subject { get; set; }
    public string Body { get; set; } = "";
    public int? TemplateId { get; set; }
}

/// <summary>
/// Envío 1 a 1 desde la ficha del prospecto: elegir plantilla o pedir un borrador a la IA,
/// editarlo y enviarlo. La IA solo redacta; nada sale sin que el vendedor lo confirme.
/// </summary>
[Route("api/leads/{leadId:long}/messaging")]
[ApiController]
[Authorize]
[SwaggerTag("CRM — Mensajes al prospecto")]
public class LeadMessagingController(
    ApplicationDbContext db, IAiClient ai, IFeatureGateService featureGate,
    ISequenceDispatchService dispatch, ITimelineLogger timeline) : ControllerBase
{
    private string? UserId => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    private bool IsAdminGlobal => User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value == "AdminGlobal";

    private async Task<(Models.Lead? lead, int customerId, IActionResult? error)> LoadAsync(long leadId)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.LeadId == leadId && (l.Deleted ?? false) == false);
        if (lead == null || lead.AccountId == null) return (null, 0, NotFound());

        if (!IsAdminGlobal)
        {
            var mine = await db.AccountInternalUsers.AnyAsync(a => a.UserId == UserId && a.AccountId == lead.AccountId);
            if (!mine) return (null, 0, Forbid());
        }

        var customerId = await db.Accounts.Where(a => a.AccountId == lead.AccountId).Select(a => a.CustomerId).FirstAsync();
        if (!await featureGate.HasFeatureAsync(customerId, "SEQUENCE_AUTOMATION"))
            return (null, 0, StatusCode(403, new { message = "Esta función no está incluida en tu plan.", featureCode = "SEQUENCE_AUTOMATION" }));
        return (lead, customerId, null);
    }

    // GET /api/leads/{id}/messaging/templates — plantillas activas del cliente + canales disponibles
    [HttpGet("templates")]
    [SwaggerOperation(Summary = "Plantillas disponibles para este prospecto y canales con los que se le puede escribir")]
    public async Task<IActionResult> Templates(long leadId)
    {
        var (lead, customerId, err) = await LoadAsync(leadId);
        if (err != null) return err;

        var templates = await db.MessageTemplates.AsNoTracking()
            .Where(t => t.CustomerId == customerId && t.IsActive)
            .OrderBy(t => t.Name)
            .Select(t => new { t.TemplateId, t.Name, t.Channel, t.Subject, t.Body })
            .ToListAsync();

        var account = await db.Accounts.AsNoTracking().FirstAsync(a => a.AccountId == lead!.AccountId);
        var customerWhatsapp = await db.Customers.Where(c => c.Id == customerId).Select(c => c.WhatsappNumber).FirstOrDefaultAsync();

        var emailReady = account.SmtpEnabled == true && account.SmtpIsVerified == true && !string.IsNullOrWhiteSpace(account.SmtpHost);
        var whatsappReady = !string.IsNullOrWhiteSpace(customerWhatsapp);

        return Ok(new
        {
            templates,
            email = new { ready = emailReady, hasAddress = !string.IsNullOrWhiteSpace(lead!.Email) },
            whatsapp = new { ready = whatsappReady, hasPhone = !string.IsNullOrWhiteSpace(lead.Phone) },
            aiConfigured = ai.IsConfigured,
        });
    }

    // POST /api/leads/{id}/messaging/draft — la IA propone el borrador (no envía nada)
    [HttpPost("draft")]
    [SwaggerOperation(Summary = "Borrador de mensaje redactado por IA para este prospecto")]
    public async Task<IActionResult> Draft(long leadId, [FromBody] DraftMessageDto dto, CancellationToken ct)
    {
        var (lead, customerId, err) = await LoadAsync(leadId);
        if (err != null) return err;
        if (dto.Channel != "Email" && dto.Channel != "WhatsApp") return BadRequest(new { message = "Canal inválido." });
        if (!ai.IsConfigured) return StatusCode(503, new { message = "La IA no está configurada." });

        string? baseText = null;
        if (dto.TemplateId != null)
        {
            var t = await db.MessageTemplates.AsNoTracking()
                .FirstOrDefaultAsync(x => x.TemplateId == dto.TemplateId && x.CustomerId == customerId && x.IsActive, ct);
            if (t == null) return BadRequest(new { message = "Plantilla no encontrada." });
            baseText = t.Body;
        }

        var notes = await db.TimelineEvents.AsNoTracking()
            .Where(e => e.EntityType == "Lead" && e.EntityId == leadId && !e.Deleted
                && (e.Type == "note" || e.Type == "message_in" || e.Type == "message_out" || e.Type == "call"))
            .OrderByDescending(e => e.CreatedOn).Take(8)
            .Select(e => new { e.Type, e.Title, e.Detail, e.CreatedOn }).ToListAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine($"Prospecto: {lead!.Name}");
        if (!string.IsNullOrWhiteSpace(lead.Company)) sb.AppendLine($"Empresa: {lead.Company}");
        if (!string.IsNullOrWhiteSpace(lead.Position)) sb.AppendLine($"Puesto: {lead.Position}");
        if (!string.IsNullOrWhiteSpace(lead.City)) sb.AppendLine($"Ciudad: {lead.City}");
        sb.AppendLine($"Etapa/estatus: {lead.Status}");
        if (!string.IsNullOrWhiteSpace(lead.InitialMessage)) sb.AppendLine($"Mensaje inicial del prospecto: {lead.InitialMessage}");
        if (notes.Count > 0)
        {
            sb.AppendLine("Actividad reciente (más nueva primero):");
            foreach (var n in notes) sb.AppendLine($"- [{n.Type}] {n.Title}{(string.IsNullOrWhiteSpace(n.Detail) ? "" : ": " + n.Detail)}");
        }
        if (baseText != null) sb.AppendLine($"\nPlantilla base a personalizar:\n{baseText}");
        if (!string.IsNullOrWhiteSpace(dto.Instruction)) sb.AppendLine($"\nIndicación del vendedor: {dto.Instruction}");

        var channelRules = dto.Channel == "Email"
            ? "Es un correo: incluye asunto breve y cuerpo de 3-6 líneas."
            : "Es WhatsApp: un solo mensaje corto (2-4 líneas), natural, sin asunto.";
        var system = $"""
Eres un asesor de ventas B2B redactando un mensaje de seguimiento a un prospecto. {channelRules}
Usa SOLO los datos que se te dan; no inventes precios, fechas, promesas ni datos de la empresa del prospecto.
Si hay una plantilla base, respeta su intención y personalízala con lo que sabes del prospecto.
Español, tono cercano y profesional, sin firma ni "Estimado". Termina con una invitación clara a responder.
""";
        const string schema = """
{"type":"object","additionalProperties":false,"required":["subject","body"],"properties":{"subject":{"type":"string"},"body":{"type":"string"}}}
""";
        try
        {
            var json = await ai.CompleteJsonAsync(system, sb.ToString(), schema, ct);
            using var doc = JsonDocument.Parse(json);
            var body = doc.RootElement.GetProperty("body").GetString()?.Trim() ?? "";
            var subject = dto.Channel == "Email" ? doc.RootElement.GetProperty("subject").GetString()?.Trim() : null;
            if (string.IsNullOrWhiteSpace(body)) return StatusCode(502, new { message = "La IA no devolvió un borrador." });
            return Ok(new { subject, body });
        }
        catch (Exception)
        {
            return StatusCode(502, new { message = "No se pudo generar el borrador. Intenta de nuevo." });
        }
    }

    // POST /api/leads/{id}/messaging/send — envía el texto ya revisado por el vendedor
    [HttpPost("send")]
    [SwaggerOperation(Summary = "Enviar un mensaje 1 a 1 al prospecto (Email o WhatsApp)")]
    public async Task<IActionResult> Send(long leadId, [FromBody] SendLeadMessageDto dto)
    {
        var (lead, customerId, err) = await LoadAsync(leadId);
        if (err != null) return err;
        if (dto.Channel != "Email" && dto.Channel != "WhatsApp") return BadRequest(new { message = "Canal inválido." });
        if (string.IsNullOrWhiteSpace(dto.Body)) return BadRequest(new { message = "El mensaje está vacío." });
        if (dto.Channel == "Email" && string.IsNullOrWhiteSpace(dto.Subject)) return BadRequest(new { message = "Falta el asunto." });

        string? templateName = null;
        if (dto.TemplateId != null)
        {
            templateName = await db.MessageTemplates.AsNoTracking()
                .Where(t => t.TemplateId == dto.TemplateId && t.CustomerId == customerId)
                .Select(t => t.Name).FirstOrDefaultAsync();
            if (templateName == null) return BadRequest(new { message = "Plantilla no encontrada." });
        }

        var (ok, error) = await dispatch.SendCustomToLeadAsync(lead!, dto.Channel, dto.Subject, dto.Body, dto.TemplateId, templateName);
        if (!ok) return BadRequest(new { message = error ?? "No se pudo enviar." });

        await timeline.LogAsync(lead!.AccountId!.Value, "Lead", lead.LeadId,
            dto.Channel == "Email" ? "email" : "message_out",
            dto.Channel == "Email" ? "Correo enviado desde la ficha" : "WhatsApp enviado desde la ficha",
            detail: dto.Body.Length > 500 ? dto.Body[..500] + "…" : dto.Body, userId: UserId);

        return Ok(new { sent = true });
    }
}
