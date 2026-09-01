using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Models;
using ProfetAPI.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace ProfetAPI.Controllers;

/// <summary>
/// API pública para integraciones externas — autenticada por API Key (header
/// X-Api-Key), NO por JWT de usuario. Pensada para que un sistema de un cliente
/// (su sitio web, Zapier, otro CRM, etc.) cree y actualice prospectos/contactos
/// sin necesitar el usuario/password de una persona real. Cada key está atada
/// a UNA sola Account — nunca puede tocar otra cuenta.
/// </summary>
[Route("api/external")]
[ApiController]
[AllowAnonymous]
[SwaggerTag("API Externa — Integraciones (autenticación por API Key)")]
public class ExternalApiController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ApiKeyService _apiKeys;

    public ExternalApiController(ApplicationDbContext context, ApiKeyService apiKeys)
    {
        _context = context;
        _apiKeys = apiKeys;
    }

    // ── Autenticación por API Key ───────────────────────────────────────────

    private async Task<AccountApiKey?> ResolveKeyAsync()
    {
        var raw = Request.Headers["X-Api-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var hash = _apiKeys.Hash(raw);
        var key = await _context.AccountApiKeys
            .FirstOrDefaultAsync(k => k.KeyHash == hash && k.IsActive && k.RevokedAt == null);
        if (key == null) return null;

        key.LastUsedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return key;
    }

    private IActionResult Unauthenticated() =>
        Unauthorized(new { message = "API Key inválida, revocada, o falta el header X-Api-Key." });

    // ── Prospectos ───────────────────────────────────────────────────────────

    // POST /api/external/leads
    [HttpPost("leads")]
    [SwaggerOperation(Summary = "Crear prospecto")]
    public async Task<IActionResult> CreateLead([FromBody] ExternalCreateLeadDto model)
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();

        var lead = new Lead
        {
            AccountId      = key.AccountId,
            Name           = model.Name,
            Email          = model.Email,
            Phone          = model.Phone,
            Company        = model.Company,
            Position       = model.Position,
            City           = model.City,
            ProspectSource = string.IsNullOrWhiteSpace(model.ProspectSource) ? key.Name : model.ProspectSource,
            InitialMessage = model.InitialMessage,
            Status         = "Nuevo",
            OriginType     = "Inbound",
            Active         = true,
            Deleted        = false,
            CreatedOn      = DateTime.UtcNow,
        };
        _context.Leads.Add(lead);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetLead), new { id = lead.LeadId }, new
        {
            leadId = lead.LeadId,
            name = lead.Name,
            status = lead.Status,
            createdOn = lead.CreatedOn,
        });
    }

    // PATCH /api/external/leads/{id}
    [HttpPatch("leads/{id:long}")]
    [SwaggerOperation(Summary = "Actualizar datos de un prospecto (parcial)")]
    public async Task<IActionResult> UpdateLead(long id, [FromBody] ExternalUpdateLeadDto model)
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();

        var lead = await _context.Leads.FirstOrDefaultAsync(l => l.LeadId == id && l.AccountId == key.AccountId);
        if (lead == null || lead.Deleted == true) return NotFound(new { message = "Prospecto no encontrado." });

        lead.Name           = model.Name           ?? lead.Name;
        lead.Email          = model.Email           ?? lead.Email;
        lead.Phone          = model.Phone           ?? lead.Phone;
        lead.Company        = model.Company         ?? lead.Company;
        lead.Position       = model.Position        ?? lead.Position;
        lead.City           = model.City            ?? lead.City;
        lead.InitialMessage = model.InitialMessage  ?? lead.InitialMessage;
        await _context.SaveChangesAsync();

        return Ok(new { leadId = lead.LeadId, updated = true });
    }

    // PATCH /api/external/leads/{id}/status
    [HttpPatch("leads/{id:long}/status")]
    [SwaggerOperation(Summary = "Cambiar el estatus de un prospecto")]
    public async Task<IActionResult> UpdateLeadStatus(long id, [FromBody] ExternalLeadStatusDto model)
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();

        var lead = await _context.Leads.FirstOrDefaultAsync(l => l.LeadId == id && l.AccountId == key.AccountId);
        if (lead == null || lead.Deleted == true) return NotFound(new { message = "Prospecto no encontrado." });

        lead.Status = model.Status;
        await _context.SaveChangesAsync();
        return Ok(new { leadId = lead.LeadId, status = lead.Status });
    }

    // GET /api/external/leads
    [HttpGet("leads")]
    [SwaggerOperation(Summary = "Listar prospectos de la cuenta")]
    public async Task<IActionResult> GetLeads([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();

        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(page, 1);

        var query = _context.Leads.Where(l => l.AccountId == key.AccountId && (l.Deleted ?? false) == false);
        var total = await query.CountAsync();
        var leads = await query
            .OrderByDescending(l => l.CreatedOn)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(l => new
            {
                l.LeadId, l.Name, l.Email, l.Phone, l.Company, l.Status,
                l.ProspectSource, l.CreatedOn,
            })
            .ToListAsync();

        return Ok(new { total, page, pageSize, leads });
    }

    // GET /api/external/leads/{id}
    [HttpGet("leads/{id:long}")]
    [SwaggerOperation(Summary = "Obtener un prospecto")]
    public async Task<IActionResult> GetLead(long id)
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();

        var lead = await _context.Leads
            .Where(l => l.LeadId == id && l.AccountId == key.AccountId && (l.Deleted ?? false) == false)
            .Select(l => new
            {
                l.LeadId, l.Name, l.Email, l.Phone, l.Company, l.Position, l.City,
                l.Status, l.ProspectSource, l.InitialMessage, l.CreatedOn,
            })
            .FirstOrDefaultAsync();
        if (lead == null) return NotFound(new { message = "Prospecto no encontrado." });
        return Ok(lead);
    }

    // ── Contactos ────────────────────────────────────────────────────────────

    // POST /api/external/contacts
    [HttpPost("contacts")]
    [SwaggerOperation(Summary = "Crear contacto")]
    public async Task<IActionResult> CreateContact([FromBody] ContactUpsertDto model)
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();

        var contact = new Contact
        {
            FirstName       = model.FirstName,
            LastName        = model.LastName,
            Email           = model.Email,
            PhoneNumber     = model.PhoneNumber,
            Position        = model.Position,
            PostalCode      = model.PostalCode,
            CompanyId       = model.CompanyId,
            LifecycleStatus = model.LifecycleStatus ?? "Lead",
            CreatedOn       = DateTime.UtcNow,
            ModifiedOn      = DateTime.UtcNow,
        };
        _context.Contacts.Add(contact);
        await _context.SaveChangesAsync();
        return Ok(new { contact.ContactId, created = true });
    }

    // ── Catálogos ────────────────────────────────────────────────────────────

    // GET /api/external/catalogs/sources
    [HttpGet("catalogs/sources")]
    [SwaggerOperation(Summary = "Fuentes de prospecto usadas en la cuenta")]
    public async Task<IActionResult> GetSources()
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();

        var sources = await _context.Leads
            .Where(l => l.AccountId == key.AccountId && l.ProspectSource != null && (l.Deleted ?? false) == false)
            .Select(l => l.ProspectSource!)
            .Distinct().OrderBy(s => s)
            .ToListAsync();
        return Ok(sources);
    }

    // GET /api/external/catalogs/tags
    [HttpGet("catalogs/tags")]
    [SwaggerOperation(Summary = "Etiquetas disponibles del cliente")]
    public async Task<IActionResult> GetTags()
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();

        var customerId = await _context.Accounts.AsNoTracking()
            .Where(a => a.AccountId == key.AccountId).Select(a => a.CustomerId).FirstOrDefaultAsync();

        var tags = await _context.Tags.AsNoTracking()
            .Where(t => t.CustomerId == customerId)
            .Select(t => new { t.TagId, t.Name, t.Color })
            .ToListAsync();
        return Ok(tags);
    }

    // GET /api/external/catalogs/variables
    [HttpGet("catalogs/variables")]
    [SwaggerOperation(Summary = "Variables (campos personalizados) activas en la cuenta")]
    public async Task<IActionResult> GetVariables()
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();

        var variables = await _context.AccountCustomFields
            .Where(a => a.AccountId == key.AccountId)
            .Select(a => new { a.FieldId, a.CustomFieldDefinition.FieldCode, a.CustomFieldDefinition.FieldName, a.CustomFieldDefinition.FieldType })
            .OrderBy(a => a.FieldName)
            .ToListAsync();
        return Ok(variables);
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

public class ExternalCreateLeadDto
{
    public string? Name        { get; set; }
    public string? Email       { get; set; }
    public string? Phone       { get; set; }
    public string? Company     { get; set; }
    public string? Position    { get; set; }
    public string? City        { get; set; }
    public string? ProspectSource { get; set; }
    public string? InitialMessage { get; set; }
}

public class ExternalUpdateLeadDto
{
    public string? Name        { get; set; }
    public string? Email       { get; set; }
    public string? Phone       { get; set; }
    public string? Company     { get; set; }
    public string? Position    { get; set; }
    public string? City        { get; set; }
    public string? InitialMessage { get; set; }
}

public class ExternalLeadStatusDto
{
    public string Status { get; set; } = "Nuevo";
}
