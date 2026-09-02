using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProfetAPI.Data;
using ProfetAPI.Models;
using ProfetAPI.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace ProfetAPI.Controllers;

/// <summary>
/// Compatibilidad con el sistema viejo (LeadsMVC / Profet_db): replica
/// POST /api/leadsapi tal cual lo consumen HOY decenas de landing pages ya
/// conectadas, para que sigan funcionando sin cambiar nada mientras se migran
/// una por una a la API Key nueva (/api/external/leads). NO requiere
/// autenticación — igual que el endpoint viejo, que tampoco la pedía.
///
/// Mapeo clave: en la migración Profet_db → Profet_new, Leads.AccountId =
/// Leads.CampaignId 1:1 sin excepciones (confirmado sobre datos reales) —
/// así que el CampaignId que ya mandan las landing pages ES el AccountId
/// nuevo, no hace falta ninguna tabla de equivalencias.
/// </summary>
[Route("api/leadsapi")]
[ApiController]
[AllowAnonymous]
[SwaggerTag("Compatibilidad — Landing pages viejas (usar api/external/leads en integraciones nuevas)")]
public class LegacyLeadsApiController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly AutomationExecutorService _automations;
    private readonly PlaybookService _playbooks;
    private readonly INotificationService _notify;
    private readonly IScoringAiService _scoringAi;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LeadAssignmentService _assignment;
    private readonly IngestionLogger _ingestionLog;

    public LegacyLeadsApiController(
        ApplicationDbContext context,
        AutomationExecutorService automations,
        PlaybookService playbooks,
        INotificationService notify,
        IScoringAiService scoringAi,
        IServiceScopeFactory scopeFactory,
        LeadAssignmentService assignment,
        IngestionLogger ingestionLog)
    {
        _context = context;
        _automations = automations;
        _playbooks = playbooks;
        _notify = notify;
        _scoringAi = scoringAi;
        _scopeFactory = scopeFactory;
        _assignment = assignment;
        _ingestionLog = ingestionLog;
    }

    // POST /api/leadsapi — mismo path y método que el sistema viejo
    [HttpPost]
    [SwaggerOperation(
        Summary = "[Compatibilidad] Crear prospecto — mismo contrato que el LeadsApiController viejo",
        Description = "Acepta el JSON tal cual lo mandan las landing pages existentes (CampaignId, Name, Email, Phone, MessageSent, ProspectSource, AdName, etc). Nueva integraciones deben usar POST /api/external/leads con API Key en su lugar."
    )]
    public async Task<IActionResult> Post([FromBody] LegacyLeadDto model)
    {
        if (model.CampaignId <= 0)
            return BadRequest(new { message = "CampaignId es obligatorio." });

        var account = await _context.Accounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.AccountId == (int)model.CampaignId);
        if (account == null)
            return BadRequest(new { message = $"No existe ninguna cuenta con CampaignId {model.CampaignId}." });

        // Mismo criterio de "usable" que el sistema viejo: necesita email o teléfono
        if (string.IsNullOrWhiteSpace(model.Email) && string.IsNullOrWhiteSpace(model.Phone))
            return BadRequest(new { message = "El prospecto necesita al menos Email o Phone." });

        var lead = new Lead
        {
            AccountId      = account.AccountId,
            Name           = string.IsNullOrWhiteSpace(model.Name) ? "Prospecto" : model.Name,
            Email          = model.Email,
            Phone          = model.Phone,
            Company        = model.Company,
            Position       = model.Position,
            City           = model.City,
            ProspectSource = string.IsNullOrWhiteSpace(model.ProspectSource) ? "Landing Page" : model.ProspectSource,
            AdName         = model.AdName,
            InitialMessage = model.MessageSent ?? model.Comments,
            Status         = "Nuevo",
            OriginType     = "Inbound",
            Active         = true,
            Deleted        = false,
            CreatedOn      = DateTime.UtcNow,
        };

        // Asignación directa si mandan un UserId válido de esta cuenta; si no,
        // se resuelve con el modo de asignación configurado en la cuenta (carrusel).
        if (!string.IsNullOrWhiteSpace(model.UserId))
        {
            var validOwner = await _context.AccountInternalUsers
                .AnyAsync(a => a.AccountId == account.AccountId && a.UserId == model.UserId);
            if (validOwner) lead.OwnerUserId = model.UserId;
        }
        if (string.IsNullOrEmpty(lead.OwnerUserId))
            lead.OwnerUserId = await _assignment.ResolveOwnerAsync(account.AccountId);

        try
        {
            _context.Leads.Add(lead);
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _ = _ingestionLog.LogAsync("LegacyCompat", $"CampaignId {model.CampaignId} — {model.Name ?? model.Email ?? "sin nombre"}: {ex.Message}", success: false);
            return StatusCode(500, new { message = "No se pudo crear el prospecto." });
        }
        _ = _ingestionLog.LogAsync("LegacyCompat", $"CampaignId {model.CampaignId} — Lead {lead.LeadId}: {lead.Name}");

        // De aquí para abajo: exactamente el mismo pipeline que POST /api/leads (la vía
        // "oficial" del sistema nuevo) — playbook de tareas, notificación al vendedor
        // asignado, auto-calificación IA preliminar, y automatizaciones de la cuenta.
        // El lead YA quedó guardado arriba — si algo de esto falla no debe tumbar la
        // respuesta a la landing, solo quedar registrado.
        try
        {
            if (!string.IsNullOrEmpty(lead.OwnerUserId))
                await _notify.NotifyAsync(lead.OwnerUserId, $"Nuevo prospecto asignado: {lead.Name}",
                    url: $"/prospectos?id={lead.LeadId}", entityType: "Lead", entityId: lead.LeadId);

            await _playbooks.ApplyDefaultAsync(account.AccountId, lead.LeadId, lead.OwnerUserId);

            if (_scoringAi.IsConfigured)
            {
                var newLeadId = lead.LeadId;
                _ = Task.Run(async () =>
                {
                    using var scope = _scopeFactory.CreateScope();
                    var ai = scope.ServiceProvider.GetRequiredService<IScoringAiService>();
                    try { await ai.ScoreAndPersistAsync(newLeadId); } catch { /* no romper la ingesta */ }
                });
            }

            _ = Task.Run(() => _automations.FireAsync(account.AccountId, "LeadCreated", new Dictionary<string, string>
            {
                ["_leadId"]        = lead.LeadId.ToString(),
                ["name"]           = lead.Name          ?? "",
                ["email"]          = lead.Email         ?? "",
                ["phone"]          = lead.Phone         ?? "",
                ["company"]        = lead.Company       ?? "",
                ["prospectSource"] = lead.ProspectSource?? "",
                ["status"]         = lead.Status,
            }));
        }
        catch (Exception ex)
        {
            _ = _ingestionLog.LogAsync("LegacyCompat", $"Lead {lead.LeadId} creado pero falló el pipeline post-creación: {ex.Message}", success: false);
        }

        // El sistema viejo devolvía el objeto Lead completo con 200 OK y un header
        // "Lead-ID" — se replica el header por si alguna landing lo lee, y un body
        // simple (las landing pages típicamente no procesan la respuesta).
        Response.Headers["Lead-ID"] = lead.LeadId.ToString();
        return Ok(new { Id = lead.LeadId, CampaignId = model.CampaignId, lead.Name, lead.Email, lead.Phone, lead.Status });
    }
}

// ── DTO — superset de campos que puede mandar una landing page vieja.
// Solo se usan los relevantes para el sistema nuevo; el resto se ignora
// silenciosamente (así ninguna landing existente truena por mandar de más).
public class LegacyLeadDto
{
    public long CampaignId       { get; set; }
    public string? Name          { get; set; }
    public string? Email         { get; set; }
    public string? Phone         { get; set; }
    public string? Company       { get; set; }
    public string? Position      { get; set; }
    public string? City          { get; set; }
    public string? ProspectSource { get; set; }
    public string? AdName        { get; set; }
    public string? MessageSent   { get; set; }
    public string? Comments      { get; set; }
    public string? UserId        { get; set; }
    public string? ContactFormFacebook { get; set; }
}
