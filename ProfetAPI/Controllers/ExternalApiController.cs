using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Models;
using ProfetAPI.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace ProfetAPI.Controllers;

/// <summary>
/// API para integraciones externas — autenticada por API Key (header X-Api-Key), NO por JWT de usuario.
/// Pensada para que un sistema del cliente (su sitio web, Zapier, otro CRM, etc.) cree, actualice y consulte
/// prospectos, contactos, empresas y tratos sin usar el usuario/contraseña de una persona. Cada key está atada
/// a UNA sola cuenta (campaña): nunca puede ver ni tocar datos de otra.
/// </summary>
[Route("api/external")]
[ApiController]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("public-api")]
[AllowAnonymous]
[Microsoft.AspNetCore.Cors.EnableCors("public")]
[Produces("application/json")]
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

        // API externa es de Evolution; un cliente migrado a otro plan no puede usar sus llaves.
        var customerId = await _context.Accounts.Where(a => a.AccountId == key.AccountId).Select(a => a.CustomerId).FirstOrDefaultAsync();
        if (!await HttpContext.RequestServices.GetRequiredService<IFeatureGateService>().IsAllowedAsync(customerId, "EXTERNAL_API"))
        {
            HttpContext.Items["planBlocked"] = true;
            return null;
        }

        key.LastUsedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return key;
    }

    private IActionResult Unauthenticated() =>
        HttpContext.Items.ContainsKey("planBlocked")
            ? StatusCode(403, new ApiMessage { Message = "La API externa no está incluida en el plan de esta cuenta." })
            : Unauthorized(new ApiMessage { Message = "API Key inválida, revocada, o falta el header X-Api-Key." });

    // ── Cuenta ───────────────────────────────────────────────────────────────

    /// <summary>Verificar la API Key y ver a qué cuenta pertenece.</summary>
    [HttpGet("me")]
    [SwaggerOperation(Summary = "Verificar la API Key y ver la cuenta a la que pertenece",
        Description = "Úsalo como primera llamada para comprobar que la llave funciona. Devuelve el id y nombre de la cuenta (campaña) sobre la que trabajan todos los demás endpoints.")]
    [ProducesResponseType(typeof(ExternalAccountInfo), 200)]
    [ProducesResponseType(typeof(ApiMessage), 401)]
    [ProducesResponseType(typeof(ApiMessage), 403)]
    public async Task<IActionResult> Me()
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();

        var acc = await _context.Accounts.AsNoTracking().Where(a => a.AccountId == key.AccountId)
            .Select(a => new ExternalAccountInfo { AccountId = a.AccountId, Name = a.Name, KeyName = key.Name })
            .FirstOrDefaultAsync();
        return Ok(acc);
    }

    // ── Prospectos ───────────────────────────────────────────────────────────

    /// <summary>Crear un prospecto.</summary>
    [HttpPost("leads")]
    [SwaggerOperation(Summary = "Crear prospecto",
        Description = "Crea un prospecto en la cuenta de la API Key. Debes mandar al menos **name**, **email** o **phone**. " +
                      "El prospecto se asigna automáticamente al vendedor que toque según el modo de asignación de la cuenta y entra con estatus `Nuevo`. " +
                      "Si no mandas `prospectSource`, se usa el nombre de la API Key. " +
                      "Para evitar duplicados en reintentos, consulta antes con `GET /api/external/leads?email=...`.")]
    [ProducesResponseType(typeof(ExternalLeadCreated), 201)]
    [ProducesResponseType(typeof(ApiMessage), 400)]
    [ProducesResponseType(typeof(ApiMessage), 401)]
    [ProducesResponseType(typeof(ApiMessage), 429)]
    public async Task<IActionResult> CreateLead([FromBody] ExternalCreateLeadDto model)
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();
        if (string.IsNullOrWhiteSpace(model.Name) && string.IsNullOrWhiteSpace(model.Email) && string.IsNullOrWhiteSpace(model.Phone))
            return BadRequest(new ApiMessage { Message = "Manda al menos name, email o phone." });

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
            return StatusCode(500, new ApiMessage { Message = "No se pudo crear el prospecto. Ya quedó registrado el error para revisión." });
        }
        _ = _ingestionLog.LogAsync("ApiKey", $"Cuenta {key.AccountId} ({key.Name}) — Lead {lead.LeadId}: {lead.Name}");

        return CreatedAtAction(nameof(GetLead), new { id = lead.LeadId }, new ExternalLeadCreated
        {
            LeadId = lead.LeadId, Name = lead.Name, Status = lead.Status,
            OwnerUserId = lead.OwnerUserId, CreatedOn = lead.CreatedOn,
        });
    }

    /// <summary>Actualizar datos de un prospecto.</summary>
    [HttpPatch("leads/{id:long}")]
    [SwaggerOperation(Summary = "Actualizar datos de un prospecto (parcial)",
        Description = "Solo se modifican los campos que mandes; los omitidos se quedan como estaban.")]
    [ProducesResponseType(typeof(ExternalLeadUpdated), 200)]
    [ProducesResponseType(typeof(ApiMessage), 401)]
    [ProducesResponseType(typeof(ApiMessage), 404)]
    public async Task<IActionResult> UpdateLead(long id, [FromBody] ExternalUpdateLeadDto model)
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();

        var lead = await _context.Leads.FirstOrDefaultAsync(l => l.LeadId == id && l.AccountId == key.AccountId);
        if (lead == null || lead.Deleted == true) return NotFound(new ApiMessage { Message = "Prospecto no encontrado." });

        lead.Name           = model.Name           ?? lead.Name;
        lead.Email          = model.Email           ?? lead.Email;
        lead.Phone          = model.Phone           ?? lead.Phone;
        lead.Company        = model.Company         ?? lead.Company;
        lead.Position       = model.Position        ?? lead.Position;
        lead.City           = model.City            ?? lead.City;
        lead.InitialMessage = model.InitialMessage  ?? lead.InitialMessage;
        await _context.SaveChangesAsync();

        return Ok(new ExternalLeadUpdated { LeadId = lead.LeadId, Updated = true });
    }

    /// <summary>Cambiar el estatus de un prospecto.</summary>
    [HttpPatch("leads/{id:long}/status")]
    [SwaggerOperation(Summary = "Cambiar el estatus de un prospecto",
        Description = "Texto libre del estatus del prospecto (por ejemplo `Nuevo`, `Contactado`, `Calificado`).")]
    [ProducesResponseType(typeof(ExternalLeadStatusResult), 200)]
    [ProducesResponseType(typeof(ApiMessage), 401)]
    [ProducesResponseType(typeof(ApiMessage), 404)]
    public async Task<IActionResult> UpdateLeadStatus(long id, [FromBody] ExternalLeadStatusDto model)
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();
        if (string.IsNullOrWhiteSpace(model.Status)) return BadRequest(new ApiMessage { Message = "El estatus es obligatorio." });

        var lead = await _context.Leads.FirstOrDefaultAsync(l => l.LeadId == id && l.AccountId == key.AccountId);
        if (lead == null || lead.Deleted == true) return NotFound(new ApiMessage { Message = "Prospecto no encontrado." });

        lead.Status = model.Status.Trim();
        await _context.SaveChangesAsync();
        return Ok(new ExternalLeadStatusResult { LeadId = lead.LeadId, Status = lead.Status });
    }

    /// <summary>Listar prospectos con filtros.</summary>
    [HttpGet("leads")]
    [SwaggerOperation(Summary = "Listar prospectos de la cuenta (con filtros)",
        Description = "Paginado y ordenado del más reciente al más antiguo. Filtros opcionales (se combinan): `email`, `phone`, `status`, `source`, " +
                      "`stageId` (etapa del funnel; ver `GET /api/external/funnel`), `createdFrom` / `createdTo` (fechas UTC, ej. `2026-11-01`). " +
                      "Úsalo con `email` o `phone` para revisar si un prospecto ya existe antes de crearlo.")]
    [ProducesResponseType(typeof(ExternalLeadList), 200)]
    [ProducesResponseType(typeof(ApiMessage), 401)]
    public async Task<IActionResult> GetLeads(
        [FromQuery] string? email, [FromQuery] string? phone, [FromQuery] string? status, [FromQuery] string? source,
        [FromQuery] int? stageId, [FromQuery] DateTime? createdFrom, [FromQuery] DateTime? createdTo,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();

        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(page, 1);

        var query = _context.Leads.AsNoTracking().Where(l => l.AccountId == key.AccountId && (l.Deleted ?? false) == false);
        if (!string.IsNullOrWhiteSpace(email))  query = query.Where(l => l.Email == email.Trim());
        if (!string.IsNullOrWhiteSpace(phone))  query = query.Where(l => l.Phone == phone.Trim());
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(l => l.Status == status.Trim());
        if (!string.IsNullOrWhiteSpace(source)) query = query.Where(l => l.ProspectSource == source.Trim());
        if (stageId.HasValue)    query = query.Where(l => l.StageId == stageId.Value);
        if (createdFrom.HasValue) query = query.Where(l => l.CreatedOn >= createdFrom.Value);
        if (createdTo.HasValue)   query = query.Where(l => l.CreatedOn <= createdTo.Value);

        var total = await query.CountAsync();
        var leads = await query
            .OrderByDescending(l => l.CreatedOn)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(l => new ExternalLeadItem
            {
                LeadId = l.LeadId, Name = l.Name, Email = l.Email, Phone = l.Phone, Company = l.Company,
                Status = l.Status, ProspectSource = l.ProspectSource, StageId = l.StageId,
                Score = l.Score, TierId = l.TierId, OwnerUserId = l.OwnerUserId, CreatedOn = l.CreatedOn,
            })
            .ToListAsync();

        return Ok(new ExternalLeadList { Total = total, Page = page, PageSize = pageSize, Leads = leads });
    }

    /// <summary>Obtener un prospecto.</summary>
    [HttpGet("leads/{id:long}")]
    [SwaggerOperation(Summary = "Obtener un prospecto", Description = "Detalle completo de un prospecto de la cuenta.")]
    [ProducesResponseType(typeof(ExternalLeadDetail), 200)]
    [ProducesResponseType(typeof(ApiMessage), 401)]
    [ProducesResponseType(typeof(ApiMessage), 404)]
    public async Task<IActionResult> GetLead(long id)
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();

        var lead = await _context.Leads.AsNoTracking()
            .Where(l => l.LeadId == id && l.AccountId == key.AccountId && (l.Deleted ?? false) == false)
            .Select(l => new ExternalLeadDetail
            {
                LeadId = l.LeadId, Name = l.Name, Email = l.Email, Phone = l.Phone, Company = l.Company,
                Position = l.Position, City = l.City, Status = l.Status, ProspectSource = l.ProspectSource,
                InitialMessage = l.InitialMessage, StageId = l.StageId, Score = l.Score, TierId = l.TierId,
                OwnerUserId = l.OwnerUserId, ContactId = l.ContactId, CreatedOn = l.CreatedOn,
            })
            .FirstOrDefaultAsync();
        if (lead == null) return NotFound(new ApiMessage { Message = "Prospecto no encontrado." });
        return Ok(lead);
    }

    /// <summary>Agregar una nota al prospecto.</summary>
    [HttpPost("leads/{id:long}/notes")]
    [SwaggerOperation(Summary = "Agregar una nota a la línea de tiempo del prospecto")]
    [ProducesResponseType(typeof(ExternalAdded), 200)]
    [ProducesResponseType(typeof(ApiMessage), 400)]
    [ProducesResponseType(typeof(ApiMessage), 401)]
    [ProducesResponseType(typeof(ApiMessage), 404)]
    public async Task<IActionResult> AddLeadNote(long id, [FromBody] ExternalNoteDto model)
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();
        if (string.IsNullOrWhiteSpace(model.Text)) return BadRequest(new ApiMessage { Message = "La nota no puede estar vacía." });

        var lead = await _context.Leads.FirstOrDefaultAsync(l => l.LeadId == id && l.AccountId == key.AccountId);
        if (lead == null || lead.Deleted == true) return NotFound(new ApiMessage { Message = "Prospecto no encontrado." });

        await _timeline.LogAsync(key.AccountId, "Lead", id, "note", "Nota", detail: model.Text.Trim(), userId: null);
        return Ok(new ExternalAdded { Added = true });
    }

    /// <summary>Etiquetar un prospecto.</summary>
    [HttpPost("leads/{id:long}/tags")]
    [SwaggerOperation(Summary = "Etiquetar un prospecto", Description = "Los ids de etiqueta salen de `GET /api/external/catalogs/tags`.")]
    [ProducesResponseType(typeof(ExternalAssigned), 200)]
    [ProducesResponseType(typeof(ApiMessage), 401)]
    [ProducesResponseType(typeof(ApiMessage), 404)]
    public async Task<IActionResult> AddLeadTag(long id, [FromBody] ExternalTagDto model)
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();

        var lead = await _context.Leads.FirstOrDefaultAsync(l => l.LeadId == id && l.AccountId == key.AccountId);
        if (lead == null || lead.Deleted == true) return NotFound(new ApiMessage { Message = "Prospecto no encontrado." });

        var customerId = await _context.Accounts.AsNoTracking()
            .Where(a => a.AccountId == key.AccountId).Select(a => a.CustomerId).FirstOrDefaultAsync();
        var tag = await _context.Tags.FirstOrDefaultAsync(t => t.TagId == model.TagId && t.CustomerId == customerId);
        if (tag == null) return NotFound(new ApiMessage { Message = "Etiqueta no encontrada." });

        var already = await _context.Taggings.AnyAsync(t => t.TagId == model.TagId && t.LeadId == (int)id);
        if (!already)
        {
            _context.Taggings.Add(new Tagging { TagId = model.TagId, LeadId = (int)id });
            await _context.SaveChangesAsync();
        }
        return Ok(new ExternalAssigned { Assigned = true });
    }

    /// <summary>Quitar una etiqueta de un prospecto.</summary>
    [HttpDelete("leads/{id:long}/tags/{tagId:int}")]
    [SwaggerOperation(Summary = "Quitar una etiqueta de un prospecto")]
    [ProducesResponseType(typeof(ExternalRemoved), 200)]
    [ProducesResponseType(typeof(ApiMessage), 401)]
    [ProducesResponseType(typeof(ApiMessage), 404)]
    public async Task<IActionResult> RemoveLeadTag(long id, int tagId)
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();

        var lead = await _context.Leads.FirstOrDefaultAsync(l => l.LeadId == id && l.AccountId == key.AccountId);
        if (lead == null) return NotFound(new ApiMessage { Message = "Prospecto no encontrado." });

        var tagging = await _context.Taggings.FirstOrDefaultAsync(t => t.TagId == tagId && t.LeadId == (int)id);
        if (tagging != null)
        {
            _context.Taggings.Remove(tagging);
            await _context.SaveChangesAsync();
        }
        return Ok(new ExternalRemoved { Removed = true });
    }

    // ── Contactos ────────────────────────────────────────────────────────────

    // Un contacto es de la cuenta si se creó en ella (AccountId) o si está ligado a un prospecto/trato de la cuenta.
    private IQueryable<Contact> AccountContacts(int accountId)
    {
        var viaLeads = _context.Leads.Where(l => l.AccountId == accountId && l.ContactId != null && (l.Deleted ?? false) == false).Select(l => l.ContactId!.Value);
        var viaDeals = _context.Deals.Where(d => d.AccountId == accountId && d.PrimaryContactId != null).Select(d => d.PrimaryContactId!.Value);
        return _context.Contacts.Where(c => !c.Deleted && (c.AccountId == accountId || viaLeads.Contains(c.ContactId) || viaDeals.Contains(c.ContactId)));
    }

    /// <summary>Listar contactos.</summary>
    [HttpGet("contacts")]
    [SwaggerOperation(Summary = "Listar contactos de la cuenta, o buscar por email",
        Description = "Paginado del más reciente al más antiguo. Filtro opcional `email` (coincidencia exacta) y `companyId`.")]
    [ProducesResponseType(typeof(ExternalContactList), 200)]
    [ProducesResponseType(typeof(ApiMessage), 401)]
    public async Task<IActionResult> GetContacts([FromQuery] string? email, [FromQuery] int? companyId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();

        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(page, 1);
        var query = AccountContacts(key.AccountId).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(email)) query = query.Where(c => c.Email == email.Trim());
        if (companyId.HasValue) query = query.Where(c => c.CompanyId == companyId.Value);

        var total = await query.CountAsync();
        var contacts = await query
            .OrderByDescending(c => c.CreatedOn)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new ExternalContactItem
            {
                ContactId = c.ContactId, FirstName = c.FirstName, LastName = c.LastName, Email = c.Email,
                PhoneNumber = c.PhoneNumber, Position = c.Position, LifecycleStatus = c.LifecycleStatus,
                CompanyId = c.CompanyId, CreatedOn = c.CreatedOn,
            })
            .ToListAsync();
        return Ok(new ExternalContactList { Total = total, Page = page, PageSize = pageSize, Contacts = contacts });
    }

    /// <summary>Obtener un contacto.</summary>
    [HttpGet("contacts/{id:int}")]
    [SwaggerOperation(Summary = "Obtener un contacto")]
    [ProducesResponseType(typeof(ExternalContactItem), 200)]
    [ProducesResponseType(typeof(ApiMessage), 401)]
    [ProducesResponseType(typeof(ApiMessage), 404)]
    public async Task<IActionResult> GetContact(int id)
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();

        var contact = await AccountContacts(key.AccountId).AsNoTracking()
            .Where(c => c.ContactId == id)
            .Select(c => new ExternalContactItem
            {
                ContactId = c.ContactId, FirstName = c.FirstName, LastName = c.LastName, Email = c.Email,
                PhoneNumber = c.PhoneNumber, Position = c.Position, LifecycleStatus = c.LifecycleStatus,
                PostalCode = c.PostalCode, CompanyId = c.CompanyId, CreatedOn = c.CreatedOn, ModifiedOn = c.ModifiedOn,
            })
            .FirstOrDefaultAsync();
        if (contact == null) return NotFound(new ApiMessage { Message = "Contacto no encontrado." });
        return Ok(contact);
    }

    /// <summary>Crear un contacto.</summary>
    [HttpPost("contacts")]
    [SwaggerOperation(Summary = "Crear contacto",
        Description = "Crea un contacto en la cuenta. Si mandas `companyId`, la empresa debe existir en la cuenta (ver `GET /api/external/companies`). " +
                      "Debes mandar al menos nombre, email o teléfono.")]
    [ProducesResponseType(typeof(ExternalContactCreated), 200)]
    [ProducesResponseType(typeof(ApiMessage), 400)]
    [ProducesResponseType(typeof(ApiMessage), 401)]
    public async Task<IActionResult> CreateContact([FromBody] ExternalContactDto model)
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();
        if (string.IsNullOrWhiteSpace(model.FirstName) && string.IsNullOrWhiteSpace(model.LastName) &&
            string.IsNullOrWhiteSpace(model.Email) && string.IsNullOrWhiteSpace(model.PhoneNumber))
            return BadRequest(new ApiMessage { Message = "Manda al menos nombre, email o teléfono." });
        if (model.CompanyId.HasValue && !await AccountCompanies(key.AccountId).AnyAsync(c => c.CompanyId == model.CompanyId.Value))
            return BadRequest(new ApiMessage { Message = "La empresa (companyId) no existe en esta cuenta." });

        var contact = new Contact
        {
            FirstName       = model.FirstName,
            LastName        = model.LastName,
            Email           = model.Email,
            PhoneNumber     = model.PhoneNumber,
            Position        = model.Position,
            PostalCode      = model.PostalCode,
            CompanyId       = model.CompanyId,
            AccountId       = key.AccountId,
            LifecycleStatus = model.LifecycleStatus ?? "Lead",
            CreatedOn       = DateTime.UtcNow,
            ModifiedOn      = DateTime.UtcNow,
        };
        _context.Contacts.Add(contact);
        await _context.SaveChangesAsync();
        return Ok(new ExternalContactCreated { ContactId = contact.ContactId, Created = true });
    }

    /// <summary>Actualizar un contacto.</summary>
    [HttpPut("contacts/{id:int}")]
    [SwaggerOperation(Summary = "Actualizar un contacto (parcial)", Description = "Solo se modifican los campos que mandes.")]
    [ProducesResponseType(typeof(ExternalContactUpdated), 200)]
    [ProducesResponseType(typeof(ApiMessage), 400)]
    [ProducesResponseType(typeof(ApiMessage), 401)]
    [ProducesResponseType(typeof(ApiMessage), 404)]
    public async Task<IActionResult> UpdateContact(int id, [FromBody] ExternalContactDto model)
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();

        var contact = await AccountContacts(key.AccountId).FirstOrDefaultAsync(c => c.ContactId == id);
        if (contact == null) return NotFound(new ApiMessage { Message = "Contacto no encontrado." });
        if (model.CompanyId.HasValue && !await AccountCompanies(key.AccountId).AnyAsync(c => c.CompanyId == model.CompanyId.Value))
            return BadRequest(new ApiMessage { Message = "La empresa (companyId) no existe en esta cuenta." });

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
        return Ok(new ExternalContactUpdated { ContactId = contact.ContactId, Updated = true });
    }

    // ── Empresas ─────────────────────────────────────────────────────────────

    // Una empresa es de la cuenta si se creó en ella (AccountId) o si tiene tratos en la cuenta.
    private IQueryable<Company> AccountCompanies(int accountId)
    {
        var viaDeals = _context.Deals.Where(d => d.AccountId == accountId && d.CompanyId != null).Select(d => d.CompanyId!.Value);
        return _context.Companies.Where(c => c.AccountId == accountId || viaDeals.Contains(c.CompanyId));
    }

    /// <summary>Listar empresas.</summary>
    [HttpGet("companies")]
    [SwaggerOperation(Summary = "Listar empresas de la cuenta", Description = "Paginado del más reciente al más antiguo. Filtro opcional `name` (contiene).")]
    [ProducesResponseType(typeof(ExternalCompanyList), 200)]
    [ProducesResponseType(typeof(ApiMessage), 401)]
    public async Task<IActionResult> GetCompanies([FromQuery] string? name, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();

        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(page, 1);
        var query = AccountCompanies(key.AccountId).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(name)) query = query.Where(c => c.Name.Contains(name.Trim()));

        var total = await query.CountAsync();
        var companies = await query
            .OrderByDescending(c => c.CreatedOn)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new ExternalCompanyItem
            {
                CompanyId = c.CompanyId, Name = c.Name, Website = c.Website, City = c.City,
                PhoneNumber = c.PhoneNumber, LifecycleStatus = c.LifecycleStatus, CreatedOn = c.CreatedOn,
            })
            .ToListAsync();
        return Ok(new ExternalCompanyList { Total = total, Page = page, PageSize = pageSize, Companies = companies });
    }

    /// <summary>Obtener una empresa.</summary>
    [HttpGet("companies/{id:int}")]
    [SwaggerOperation(Summary = "Obtener una empresa")]
    [ProducesResponseType(typeof(ExternalCompanyItem), 200)]
    [ProducesResponseType(typeof(ApiMessage), 401)]
    [ProducesResponseType(typeof(ApiMessage), 404)]
    public async Task<IActionResult> GetCompany(int id)
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();

        var company = await AccountCompanies(key.AccountId).AsNoTracking()
            .Where(c => c.CompanyId == id)
            .Select(c => new ExternalCompanyItem
            {
                CompanyId = c.CompanyId, Name = c.Name, Website = c.Website, Address = c.Address, City = c.City,
                State = c.State, PostalCode = c.PostalCode, PhoneNumber = c.PhoneNumber,
                LifecycleStatus = c.LifecycleStatus, CreatedOn = c.CreatedOn, ModifiedOn = c.ModifiedOn,
            })
            .FirstOrDefaultAsync();
        if (company == null) return NotFound(new ApiMessage { Message = "Empresa no encontrada." });
        return Ok(company);
    }

    /// <summary>Crear una empresa.</summary>
    [HttpPost("companies")]
    [SwaggerOperation(Summary = "Crear empresa", Description = "El nombre es obligatorio. La empresa queda ligada a la cuenta de la API Key.")]
    [ProducesResponseType(typeof(ExternalCompanyCreated), 200)]
    [ProducesResponseType(typeof(ApiMessage), 400)]
    [ProducesResponseType(typeof(ApiMessage), 401)]
    public async Task<IActionResult> CreateCompany([FromBody] ExternalCompanyDto model)
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();
        if (string.IsNullOrWhiteSpace(model.Name)) return BadRequest(new ApiMessage { Message = "El nombre es obligatorio." });

        var company = new Company
        {
            AccountId = key.AccountId,
            Name = model.Name.Trim(), Website = model.Website, PhoneNumber = model.PhoneNumber,
            Address = model.Address, City = model.City, State = model.State, PostalCode = model.PostalCode,
            LifecycleStatus = model.LifecycleStatus ?? "Prospecto",
            CreatedOn = DateTime.UtcNow, ModifiedOn = DateTime.UtcNow,
        };
        _context.Companies.Add(company);
        await _context.SaveChangesAsync();
        return Ok(new ExternalCompanyCreated { CompanyId = company.CompanyId, Created = true });
    }

    /// <summary>Actualizar una empresa.</summary>
    [HttpPut("companies/{id:int}")]
    [SwaggerOperation(Summary = "Actualizar empresa (parcial)", Description = "Solo se modifican los campos que mandes.")]
    [ProducesResponseType(typeof(ExternalCompanyUpdated), 200)]
    [ProducesResponseType(typeof(ApiMessage), 401)]
    [ProducesResponseType(typeof(ApiMessage), 404)]
    public async Task<IActionResult> UpdateCompany(int id, [FromBody] ExternalCompanyDto model)
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();

        var company = await AccountCompanies(key.AccountId).FirstOrDefaultAsync(c => c.CompanyId == id);
        if (company == null) return NotFound(new ApiMessage { Message = "Empresa no encontrada." });

        if (!string.IsNullOrWhiteSpace(model.Name)) company.Name = model.Name.Trim();
        company.Website         = model.Website         ?? company.Website;
        company.PhoneNumber     = model.PhoneNumber     ?? company.PhoneNumber;
        company.Address         = model.Address         ?? company.Address;
        company.City            = model.City            ?? company.City;
        company.State           = model.State           ?? company.State;
        company.PostalCode      = model.PostalCode      ?? company.PostalCode;
        company.LifecycleStatus = model.LifecycleStatus ?? company.LifecycleStatus;
        company.ModifiedOn      = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(new ExternalCompanyUpdated { CompanyId = company.CompanyId, Updated = true });
    }

    // ── Funnel y tratos ──────────────────────────────────────────────────────

    /// <summary>Funnel (embudo) de la cuenta con sus etapas.</summary>
    [HttpGet("funnel")]
    [SwaggerOperation(Summary = "Funnel de la cuenta con sus etapas",
        Description = "Devuelve los embudos de la cuenta y sus etapas en orden. Los `stageId` sirven para filtrar `GET /api/external/leads?stageId=` y `GET /api/external/deals?stageId=`.")]
    [ProducesResponseType(typeof(List<ExternalFunnel>), 200)]
    [ProducesResponseType(typeof(ApiMessage), 401)]
    public async Task<IActionResult> GetFunnel()
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();

        var funnels = await _context.Funnels.AsNoTracking()
            .Where(f => f.AccountId == key.AccountId)
            .Select(f => new ExternalFunnel
            {
                FunnelId = f.FunnelId, Name = f.Name,
                Stages = f.Stages.OrderBy(s => s.Order)
                    .Select(s => new ExternalStage { StageId = s.StageId, Name = s.Name, Order = s.Order, Color = s.Color }).ToList(),
            })
            .ToListAsync();
        return Ok(funnels);
    }

    /// <summary>Listar tratos (oportunidades).</summary>
    [HttpGet("deals")]
    [SwaggerOperation(Summary = "Listar tratos (oportunidades) de la cuenta",
        Description = "Solo lectura. Paginado del más reciente al más antiguo. Filtros opcionales: `status` (`Abierto`, `Ganado`, `Perdido`), `stageId`, `companyId`, `createdFrom` / `createdTo`.")]
    [ProducesResponseType(typeof(ExternalDealList), 200)]
    [ProducesResponseType(typeof(ApiMessage), 401)]
    public async Task<IActionResult> GetDeals(
        [FromQuery] string? status, [FromQuery] int? stageId, [FromQuery] int? companyId,
        [FromQuery] DateTime? createdFrom, [FromQuery] DateTime? createdTo,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();

        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(page, 1);
        var query = _context.Deals.AsNoTracking().Where(d => d.AccountId == key.AccountId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(d => d.Status == status.Trim());
        if (stageId.HasValue)     query = query.Where(d => d.StageId == stageId.Value);
        if (companyId.HasValue)   query = query.Where(d => d.CompanyId == companyId.Value);
        if (createdFrom.HasValue) query = query.Where(d => d.CreatedOn >= createdFrom.Value);
        if (createdTo.HasValue)   query = query.Where(d => d.CreatedOn <= createdTo.Value);

        var total = await query.CountAsync();
        var deals = await query
            .OrderByDescending(d => d.CreatedOn)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(d => new ExternalDealItem
            {
                DealId = d.DealId, DealName = d.DealName, Status = d.Status, StageId = d.StageId,
                QuotedAmount = d.QuotedAmount, FinalAmount = d.FinalAmount, CompanyId = d.CompanyId,
                PrimaryContactId = d.PrimaryContactId, CloseDate = d.CloseDate, ProspectSource = d.ProspectSource,
                CreatedOn = d.CreatedOn,
            })
            .ToListAsync();
        return Ok(new ExternalDealList { Total = total, Page = page, PageSize = pageSize, Deals = deals });
    }

    /// <summary>Obtener un trato.</summary>
    [HttpGet("deals/{id:int}")]
    [SwaggerOperation(Summary = "Obtener un trato (oportunidad)")]
    [ProducesResponseType(typeof(ExternalDealItem), 200)]
    [ProducesResponseType(typeof(ApiMessage), 401)]
    [ProducesResponseType(typeof(ApiMessage), 404)]
    public async Task<IActionResult> GetDeal(int id)
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();

        var deal = await _context.Deals.AsNoTracking()
            .Where(d => d.DealId == id && d.AccountId == key.AccountId)
            .Select(d => new ExternalDealItem
            {
                DealId = d.DealId, DealName = d.DealName, Status = d.Status, StageId = d.StageId,
                QuotedAmount = d.QuotedAmount, FinalAmount = d.FinalAmount, CompanyId = d.CompanyId,
                PrimaryContactId = d.PrimaryContactId, CloseDate = d.CloseDate, ProspectSource = d.ProspectSource,
                CreatedOn = d.CreatedOn,
            })
            .FirstOrDefaultAsync();
        if (deal == null) return NotFound(new ApiMessage { Message = "Trato no encontrado." });
        return Ok(deal);
    }

    // ── Catálogos ────────────────────────────────────────────────────────────

    /// <summary>Fuentes de prospecto usadas en la cuenta.</summary>
    [HttpGet("catalogs/sources")]
    [SwaggerOperation(Summary = "Fuentes de prospecto usadas en la cuenta", Description = "Lista de textos que puedes mandar en `prospectSource` al crear un prospecto.")]
    [ProducesResponseType(typeof(List<string>), 200)]
    [ProducesResponseType(typeof(ApiMessage), 401)]
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

    /// <summary>Etiquetas disponibles.</summary>
    [HttpGet("catalogs/tags")]
    [SwaggerOperation(Summary = "Etiquetas disponibles del cliente", Description = "Los `tagId` sirven para `POST /api/external/leads/{id}/tags`.")]
    [ProducesResponseType(typeof(List<ExternalTagItem>), 200)]
    [ProducesResponseType(typeof(ApiMessage), 401)]
    public async Task<IActionResult> GetTags()
    {
        var key = await ResolveKeyAsync();
        if (key == null) return Unauthenticated();

        var customerId = await _context.Accounts.AsNoTracking()
            .Where(a => a.AccountId == key.AccountId).Select(a => a.CustomerId).FirstOrDefaultAsync();

        var tags = await _context.Tags.AsNoTracking()
            .Where(t => t.CustomerId == customerId)
            .Select(t => new ExternalTagItem { TagId = t.TagId, Name = t.Name, Color = t.Color })
            .ToListAsync();
        return Ok(tags);
    }

    /// <summary>Variables (campos personalizados) activas.</summary>
    [HttpGet("catalogs/variables")]
    [SwaggerOperation(Summary = "Variables (campos personalizados) activas en la cuenta")]
    [ProducesResponseType(typeof(IEnumerable<object>), 200)]
    [ProducesResponseType(typeof(ApiMessage), 401)]
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

    /// <summary>Motivos de pérdida válidos.</summary>
    [HttpGet("catalogs/lost-reasons")]
    [SwaggerOperation(Summary = "Motivos de pérdida válidos de la cuenta")]
    [ProducesResponseType(typeof(IEnumerable<object>), 200)]
    [ProducesResponseType(typeof(ApiMessage), 401)]
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

// ── DTOs de entrada ───────────────────────────────────────────────────────────

/// <summary>Datos para crear un prospecto. Manda al menos name, email o phone.</summary>
public class ExternalCreateLeadDto
{
    /// <summary>Nombre completo del prospecto.</summary>
    /// <example>María López</example>
    public string? Name        { get; set; }
    /// <summary>Correo electrónico.</summary>
    /// <example>maria@empresa.com</example>
    public string? Email       { get; set; }
    /// <summary>Teléfono (con lada; ideal formato internacional).</summary>
    /// <example>+5215512345678</example>
    public string? Phone       { get; set; }
    /// <summary>Empresa donde trabaja.</summary>
    /// <example>Constructora Norte</example>
    public string? Company     { get; set; }
    /// <summary>Puesto.</summary>
    /// <example>Directora de compras</example>
    public string? Position    { get; set; }
    /// <summary>Ciudad.</summary>
    /// <example>Monterrey</example>
    public string? City        { get; set; }
    /// <summary>Fuente u origen del prospecto. Si se omite se usa el nombre de la API Key.</summary>
    /// <example>Landing Black Friday</example>
    public string? ProspectSource { get; set; }
    /// <summary>Mensaje o comentario inicial que dejó el prospecto.</summary>
    /// <example>Quiero una cotización para 20 licencias</example>
    public string? InitialMessage { get; set; }
}

/// <summary>Campos a modificar de un prospecto; los omitidos no cambian.</summary>
public class ExternalUpdateLeadDto
{
    /// <example>María López</example>
    public string? Name        { get; set; }
    /// <example>maria@empresa.com</example>
    public string? Email       { get; set; }
    /// <example>+5215512345678</example>
    public string? Phone       { get; set; }
    /// <example>Constructora Norte</example>
    public string? Company     { get; set; }
    /// <example>Directora de compras</example>
    public string? Position    { get; set; }
    /// <example>Monterrey</example>
    public string? City        { get; set; }
    /// <example>Quiero una cotización para 20 licencias</example>
    public string? InitialMessage { get; set; }
}

public class ExternalLeadStatusDto
{
    /// <summary>Nuevo estatus del prospecto.</summary>
    /// <example>Contactado</example>
    public string Status { get; set; } = "Nuevo";
}

public class ExternalNoteDto
{
    /// <summary>Texto de la nota.</summary>
    /// <example>Pidió que lo llamemos el lunes por la mañana.</example>
    public string? Text { get; set; }
}

public class ExternalTagDto
{
    /// <summary>Id de la etiqueta (de GET /api/external/catalogs/tags).</summary>
    /// <example>12</example>
    public int TagId { get; set; }
}

/// <summary>Datos de un contacto (crear o actualizar).</summary>
public class ExternalContactDto
{
    /// <example>Carlos</example>
    public string? FirstName       { get; set; }
    /// <example>Ramírez</example>
    public string? LastName        { get; set; }
    /// <example>carlos@empresa.com</example>
    public string? Email           { get; set; }
    /// <example>+5218112345678</example>
    public string? PhoneNumber     { get; set; }
    /// <example>Gerente de operaciones</example>
    public string? Position        { get; set; }
    /// <example>64000</example>
    public string? PostalCode      { get; set; }
    /// <summary>Empresa a la que pertenece (id de GET /api/external/companies).</summary>
    /// <example>45</example>
    public int? CompanyId          { get; set; }
    /// <summary>Etapa de vida del contacto.</summary>
    /// <example>Lead</example>
    public string? LifecycleStatus { get; set; }
}

/// <summary>Datos de una empresa (crear o actualizar). El nombre es obligatorio al crear.</summary>
public class ExternalCompanyDto
{
    /// <example>Constructora Norte S.A. de C.V.</example>
    public string Name             { get; set; } = null!;
    /// <example>https://constructoranorte.mx</example>
    public string? Website         { get; set; }
    /// <example>+5218187654321</example>
    public string? PhoneNumber     { get; set; }
    /// <example>Av. Constitución 100</example>
    public string? Address         { get; set; }
    /// <example>Monterrey</example>
    public string? City            { get; set; }
    /// <example>Nuevo León</example>
    public string? State           { get; set; }
    /// <example>64000</example>
    public string? PostalCode      { get; set; }
    /// <example>Prospecto</example>
    public string? LifecycleStatus { get; set; }
}

// ── DTOs de salida (documentan el contrato de respuesta en Swagger) ────────────

public class ApiMessage { public string Message { get; set; } = ""; }
public class ExternalAccountInfo { public int AccountId { get; set; } public string? Name { get; set; } public string? KeyName { get; set; } }
public class ExternalLeadCreated { public long LeadId { get; set; } public string? Name { get; set; } public string? Status { get; set; } public string? OwnerUserId { get; set; } public DateTime CreatedOn { get; set; } }
public class ExternalLeadUpdated { public long LeadId { get; set; } public bool Updated { get; set; } }
public class ExternalContactUpdated { public int ContactId { get; set; } public bool Updated { get; set; } }
public class ExternalCompanyUpdated { public int CompanyId { get; set; } public bool Updated { get; set; } }
public class ExternalContactCreated { public int ContactId { get; set; } public bool Created { get; set; } }
public class ExternalCompanyCreated { public int CompanyId { get; set; } public bool Created { get; set; } }
public class ExternalAdded { public bool Added { get; set; } }
public class ExternalAssigned { public bool Assigned { get; set; } }
public class ExternalRemoved { public bool Removed { get; set; } }
public class ExternalLeadStatusResult { public long LeadId { get; set; } public string? Status { get; set; } }

public class ExternalLeadItem
{
    public long LeadId { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Company { get; set; }
    public string? Status { get; set; }
    public string? ProspectSource { get; set; }
    /// <summary>Etapa del funnel (ver GET /api/external/funnel).</summary>
    public int? StageId { get; set; }
    /// <summary>Calificación (score) del prospecto, si ya fue calificado.</summary>
    public decimal? Score { get; set; }
    public int? TierId { get; set; }
    /// <summary>Id del vendedor asignado.</summary>
    public string? OwnerUserId { get; set; }
    /// <summary>Fecha de creación en UTC.</summary>
    public DateTime CreatedOn { get; set; }
}

public class ExternalLeadDetail : ExternalLeadItem
{
    public string? Position { get; set; }
    public string? City { get; set; }
    public string? InitialMessage { get; set; }
    /// <summary>Contacto ligado al prospecto, si existe.</summary>
    public int? ContactId { get; set; }
}

public class ExternalLeadList { public int Total { get; set; } public int Page { get; set; } public int PageSize { get; set; } public List<ExternalLeadItem> Leads { get; set; } = new(); }

public class ExternalContactItem
{
    public int ContactId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Position { get; set; }
    public string? LifecycleStatus { get; set; }
    public string? PostalCode { get; set; }
    public int? CompanyId { get; set; }
    public DateTime CreatedOn { get; set; }
    public DateTime? ModifiedOn { get; set; }
}
public class ExternalContactList { public int Total { get; set; } public int Page { get; set; } public int PageSize { get; set; } public List<ExternalContactItem> Contacts { get; set; } = new(); }

public class ExternalCompanyItem
{
    public int CompanyId { get; set; }
    public string Name { get; set; } = "";
    public string? Website { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? PhoneNumber { get; set; }
    public string? LifecycleStatus { get; set; }
    public DateTime CreatedOn { get; set; }
    public DateTime? ModifiedOn { get; set; }
}
public class ExternalCompanyList { public int Total { get; set; } public int Page { get; set; } public int PageSize { get; set; } public List<ExternalCompanyItem> Companies { get; set; } = new(); }

public class ExternalStage { public int StageId { get; set; } public string Name { get; set; } = ""; public int Order { get; set; } public string? Color { get; set; } }
public class ExternalFunnel { public int FunnelId { get; set; } public string Name { get; set; } = ""; public List<ExternalStage> Stages { get; set; } = new(); }

public class ExternalDealItem
{
    public int DealId { get; set; }
    public string DealName { get; set; } = "";
    /// <summary>Abierto, Ganado o Perdido.</summary>
    public string Status { get; set; } = "";
    public int? StageId { get; set; }
    public decimal? QuotedAmount { get; set; }
    public decimal? FinalAmount { get; set; }
    public int? CompanyId { get; set; }
    public int? PrimaryContactId { get; set; }
    public DateTime? CloseDate { get; set; }
    public string? ProspectSource { get; set; }
    public DateTime CreatedOn { get; set; }
}
public class ExternalDealList { public int Total { get; set; } public int Page { get; set; } public int PageSize { get; set; } public List<ExternalDealItem> Deals { get; set; } = new(); }

public class ExternalTagItem { public int TagId { get; set; } public string? Name { get; set; } public string? Color { get; set; } }
