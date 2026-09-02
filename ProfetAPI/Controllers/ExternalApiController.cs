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
    private readonly ITimelineLogger _timeline;
    private readonly LeadAssignmentService _assignment;
    private readonly IngestionLogger _ingestionLog;

    public ExternalApiController(ApplicationDbContext context, ApiKeyService apiKeys, ITimelineLogger timeline, LeadAssignmentService assignment, IngestionLogger ingestionLog)
    {
        _context = context;
        _apiKeys = apiKeys;
        _timeline = timeline;
        _assignment = assignment;
        _ingestionLog = ingestionLog;
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
        // La integración no tiene forma de saber quién debe llevarse el lead —
        // se resuelve con el modo de asignación configurado en la cuenta (hoy:
        // carrusel/round-robin), igual que cualquier otro canal de entrada.
        lead.OwnerUserId = await _assignment.ResolveOwnerAsync(key.AccountId);

        try
        {
            _context.Leads.Add(lead);
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _ = _ingestionLog.LogAsync("ApiKey", $"Cuenta {key.AccountId} ({key.Name}) — {model.Name ?? model.Email ?? "sin nombre"}: {ex.Message}", success: false);
            return StatusCode(500, new { message = "No se pudo crear el prospecto. Ya quedó registrado el error para revisión." });
        }
        _ = _ingestionLog.LogAsync("ApiKey", $"Cuenta {key.AccountId} ({key.Name}) — Lead {lead.LeadId}: {lead.Name}");

        return CreatedAtAction(nameof(GetLead), new { id = lead.LeadId }, new
        {
            leadId = lead.LeadId,
            name = lead.Name,
            status = lead.Status,
            ownerUserId = lead.OwnerUserId,
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
    [SwaggerOperation(Summary = "Listar prospectos de la cuenta, o buscar por email/teléfono", Description = "Manda email o phone para revisar si un prospecto ya existe antes de crearlo (evita duplicados en reintentos de formulario).")]
    public async Task<IActionResult> GetLeads([FromQuery] string? email, [FromQuery] string? phone, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();

        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(page, 1);

        var query = _context.Leads.Where(l => l.AccountId == key.AccountId && (l.Deleted ?? false) == false);
        if (!string.IsNullOrWhiteSpace(email)) query = query.Where(l => l.Email == email.Trim());
        if (!string.IsNullOrWhiteSpace(phone)) query = query.Where(l => l.Phone == phone.Trim());
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

    // POST /api/external/leads/{id}/notes
    [HttpPost("leads/{id:long}/notes")]
    [SwaggerOperation(Summary = "Agregar una nota a la línea de tiempo del prospecto")]
    public async Task<IActionResult> AddLeadNote(long id, [FromBody] ExternalNoteDto model)
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();
        if (string.IsNullOrWhiteSpace(model.Text)) return BadRequest(new { message = "La nota no puede estar vacía." });

        var lead = await _context.Leads.FirstOrDefaultAsync(l => l.LeadId == id && l.AccountId == key.AccountId);
        if (lead == null || lead.Deleted == true) return NotFound(new { message = "Prospecto no encontrado." });

        await _timeline.LogAsync(key.AccountId, "Lead", id, "note", "Nota", detail: model.Text.Trim(), userId: null);
        return Ok(new { added = true });
    }

    // POST /api/external/leads/{id}/tags
    [HttpPost("leads/{id:long}/tags")]
    [SwaggerOperation(Summary = "Etiquetar un prospecto")]
    public async Task<IActionResult> AddLeadTag(long id, [FromBody] ExternalTagDto model)
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();

        var lead = await _context.Leads.FirstOrDefaultAsync(l => l.LeadId == id && l.AccountId == key.AccountId);
        if (lead == null || lead.Deleted == true) return NotFound(new { message = "Prospecto no encontrado." });

        var customerId = await _context.Accounts.AsNoTracking()
            .Where(a => a.AccountId == key.AccountId).Select(a => a.CustomerId).FirstOrDefaultAsync();
        var tag = await _context.Tags.FirstOrDefaultAsync(t => t.TagId == model.TagId && t.CustomerId == customerId);
        if (tag == null) return NotFound(new { message = "Etiqueta no encontrada." });

        var already = await _context.Taggings.AnyAsync(t => t.TagId == model.TagId && t.LeadId == (int)id);
        if (!already)
        {
            _context.Taggings.Add(new Tagging { TagId = model.TagId, LeadId = (int)id });
            await _context.SaveChangesAsync();
        }
        return Ok(new { assigned = true });
    }

    // DELETE /api/external/leads/{id}/tags/{tagId}
    [HttpDelete("leads/{id:long}/tags/{tagId:int}")]
    [SwaggerOperation(Summary = "Quitar una etiqueta de un prospecto")]
    public async Task<IActionResult> RemoveLeadTag(long id, int tagId)
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();

        var lead = await _context.Leads.FirstOrDefaultAsync(l => l.LeadId == id && l.AccountId == key.AccountId);
        if (lead == null) return NotFound(new { message = "Prospecto no encontrado." });

        var tagging = await _context.Taggings.FirstOrDefaultAsync(t => t.TagId == tagId && t.LeadId == (int)id);
        if (tagging != null)
        {
            _context.Taggings.Remove(tagging);
            await _context.SaveChangesAsync();
        }
        return Ok(new { removed = true });
    }

    // ── Contactos ────────────────────────────────────────────────────────────

    private async Task<List<int>> GetAccountContactIdsAsync(int accountId)
    {
        var leadContactIds = await _context.Leads.AsNoTracking()
            .Where(l => l.AccountId == accountId && l.ContactId != null && (l.Deleted ?? false) == false)
            .Select(l => l.ContactId!.Value).Distinct().ToListAsync();
        var dealContactIds = await _context.Deals.AsNoTracking()
            .Where(d => d.AccountId == accountId && d.PrimaryContactId != null)
            .Select(d => d.PrimaryContactId!.Value).Distinct().ToListAsync();
        return leadContactIds.Union(dealContactIds).Distinct().ToList();
    }

    // GET /api/external/contacts
    [HttpGet("contacts")]
    [SwaggerOperation(Summary = "Listar contactos de la cuenta, o buscar por email")]
    public async Task<IActionResult> GetContacts([FromQuery] string? email, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();

        pageSize = Math.Clamp(pageSize, 1, 200);
        var contactIds = await GetAccountContactIdsAsync(key.AccountId);
        var query = _context.Contacts.AsNoTracking().Where(c => contactIds.Contains(c.ContactId));
        if (!string.IsNullOrWhiteSpace(email)) query = query.Where(c => c.Email == email.Trim());

        var total = await query.CountAsync();
        var contacts = await query
            .OrderByDescending(c => c.CreatedOn)
            .Skip((Math.Max(page, 1) - 1) * pageSize).Take(pageSize)
            .Select(c => new { c.ContactId, c.FirstName, c.LastName, c.Email, c.PhoneNumber, c.Position, c.LifecycleStatus, c.CompanyId, c.CreatedOn })
            .ToListAsync();
        return Ok(new { total, page, pageSize, contacts });
    }

    // GET /api/external/contacts/{id}
    [HttpGet("contacts/{id:int}")]
    [SwaggerOperation(Summary = "Obtener un contacto")]
    public async Task<IActionResult> GetContact(int id)
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();

        var contactIds = await GetAccountContactIdsAsync(key.AccountId);
        if (!contactIds.Contains(id)) return NotFound(new { message = "Contacto no encontrado." });

        var contact = await _context.Contacts.AsNoTracking()
            .Where(c => c.ContactId == id)
            .Select(c => new { c.ContactId, c.FirstName, c.LastName, c.Email, c.PhoneNumber, c.Position, c.LifecycleStatus, c.PostalCode, c.CompanyId, c.CreatedOn, c.ModifiedOn })
            .FirstOrDefaultAsync();
        if (contact == null) return NotFound(new { message = "Contacto no encontrado." });
        return Ok(contact);
    }

    // PUT /api/external/contacts/{id}
    [HttpPut("contacts/{id:int}")]
    [SwaggerOperation(Summary = "Actualizar un contacto (parcial)")]
    public async Task<IActionResult> UpdateContact(int id, [FromBody] ContactUpsertDto model)
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();

        var contactIds = await GetAccountContactIdsAsync(key.AccountId);
        if (!contactIds.Contains(id)) return NotFound(new { message = "Contacto no encontrado." });

        var contact = await _context.Contacts.FindAsync(id);
        if (contact == null) return NotFound(new { message = "Contacto no encontrado." });

        contact.FirstName       = model.FirstName       ?? contact.FirstName;
        contact.LastName        = model.LastName        ?? contact.LastName;
        contact.Email           = model.Email           ?? contact.Email;
        contact.PhoneNumber     = model.PhoneNumber     ?? contact.PhoneNumber;
        contact.Position        = model.Position        ?? contact.Position;
        contact.PostalCode      = model.PostalCode      ?? contact.PostalCode;
        contact.CompanyId       = model.CompanyId       ?? contact.CompanyId;
        contact.LifecycleStatus = model.LifecycleStatus ?? contact.LifecycleStatus;
        contact.ModifiedOn      = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(new { contact.ContactId, updated = true });
    }

    // ── Empresas ─────────────────────────────────────────────────────────────

    private async Task<List<int>> GetAccountCompanyIdsAsync(int accountId) =>
        await _context.Deals.AsNoTracking()
            .Where(d => d.AccountId == accountId && d.CompanyId != null)
            .Select(d => d.CompanyId!.Value).Distinct().ToListAsync();

    // GET /api/external/companies
    [HttpGet("companies")]
    [SwaggerOperation(Summary = "Listar empresas de la cuenta")]
    public async Task<IActionResult> GetCompanies([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();

        pageSize = Math.Clamp(pageSize, 1, 200);
        var companyIds = await GetAccountCompanyIdsAsync(key.AccountId);
        var query = _context.Companies.AsNoTracking().Where(c => companyIds.Contains(c.CompanyId));
        var total = await query.CountAsync();
        var companies = await query
            .OrderByDescending(c => c.CreatedOn)
            .Skip((Math.Max(page, 1) - 1) * pageSize).Take(pageSize)
            .Select(c => new { c.CompanyId, c.Name, c.Website, c.City, c.PhoneNumber, c.LifecycleStatus, c.CreatedOn })
            .ToListAsync();
        return Ok(new { total, page, pageSize, companies });
    }

    // POST /api/external/companies
    [HttpPost("companies")]
    [SwaggerOperation(Summary = "Crear empresa")]
    public async Task<IActionResult> CreateCompany([FromBody] CompanyUpsertDto model)
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();
        if (string.IsNullOrWhiteSpace(model.Name)) return BadRequest(new { message = "El nombre es obligatorio." });

        var company = new Company
        {
            Name = model.Name, Website = model.Website, PhoneNumber = model.PhoneNumber,
            Address = model.Address, City = model.City, State = model.State, PostalCode = model.PostalCode,
            LifecycleStatus = model.LifecycleStatus ?? "Prospecto",
            CreatedOn = DateTime.UtcNow, ModifiedOn = DateTime.UtcNow,
        };
        _context.Companies.Add(company);
        await _context.SaveChangesAsync();
        return Ok(new { company.CompanyId, created = true });
    }

    // PUT /api/external/companies/{id}
    [HttpPut("companies/{id:int}")]
    [SwaggerOperation(Summary = "Actualizar empresa (parcial)")]
    public async Task<IActionResult> UpdateCompany(int id, [FromBody] CompanyUpsertDto model)
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();

        var companyIds = await GetAccountCompanyIdsAsync(key.AccountId);
        if (!companyIds.Contains(id)) return NotFound(new { message = "Empresa no encontrada." });

        var company = await _context.Companies.FindAsync(id);
        if (company == null) return NotFound(new { message = "Empresa no encontrada." });

        if (!string.IsNullOrWhiteSpace(model.Name)) company.Name = model.Name;
        company.Website         = model.Website         ?? company.Website;
        company.PhoneNumber     = model.PhoneNumber     ?? company.PhoneNumber;
        company.Address         = model.Address         ?? company.Address;
        company.City            = model.City            ?? company.City;
        company.State           = model.State            ?? company.State;
        company.PostalCode      = model.PostalCode       ?? company.PostalCode;
        company.LifecycleStatus = model.LifecycleStatus  ?? company.LifecycleStatus;
        company.ModifiedOn      = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(new { company.CompanyId, updated = true });
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

    // GET /api/external/catalogs/lost-reasons
    [HttpGet("catalogs/lost-reasons")]
    [SwaggerOperation(Summary = "Motivos de pérdida válidos de la cuenta")]
    public async Task<IActionResult> GetLostReasons()
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();

        var reasons = await _context.LeadLostReasons.AsNoTracking()
            .Where(r => r.AccountId == key.AccountId)
            .Select(r => new { r.LostReasonId, r.Description })
            .ToListAsync();
        return Ok(reasons);
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

public class ExternalNoteDto
{
    public string? Text { get; set; }
}

public class ExternalTagDto
{
    public int TagId { get; set; }
}
