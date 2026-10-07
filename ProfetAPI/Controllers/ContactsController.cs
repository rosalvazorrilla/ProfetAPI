using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Models;
using Swashbuckle.AspNetCore.Annotations;

namespace ProfetAPI.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
[SwaggerTag("CRM — Contactos")]
public class ContactsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    public ContactsController(ApplicationDbContext context) => _context = context;

    private string? CurrentUserId => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    private string? CurrentUserRole => User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
    private bool IsAdminGlobal => CurrentUserRole == "AdminGlobal";

    // GET /api/contacts?accountId=&search=&page=1&pageSize=50
    [HttpGet]
    [SwaggerOperation(Summary = "Listar contactos del CRM")]
    [SwaggerResponse(200, "Lista de contactos")]
    public async Task<IActionResult> GetContacts(
        [FromQuery] int? accountId,
        [FromQuery] string? search,
        [FromQuery] int page     = 1,
        [FromQuery] int pageSize = 50)
    {
        var (query, error) = await VisibleContactsAsync(accountId, search);
        if (error != null) return error;

        var total = await query!.CountAsync();

        var contacts = await query
            .OrderByDescending(c => c.CreatedOn)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new
            {
                c.ContactId,
                c.FirstName,
                c.LastName,
                c.Email,
                c.PhoneNumber,
                c.Position,
                c.LifecycleStatus,
                c.CreatedOn,
                c.CompanyId,
                companyName = c.Company != null ? c.Company.Name : null,
            })
            .ToListAsync();

        return Ok(new { total, page, pageSize, data = contacts });
    }


    /// <summary>Contactos visibles de una cuenta (propios + ligados a sus prospectos u oportunidades), sin los eliminados.</summary>
    private async Task<(IQueryable<Contact>? query, IActionResult? error)> VisibleContactsAsync(int? accountId, string? search)
    {
        int resolvedAccountId;
        if (accountId.HasValue)
        {
            if (!IsAdminGlobal)
            {
                var belongs = await _context.AccountInternalUsers
                    .AnyAsync(a => a.AccountId == accountId && a.UserId == CurrentUserId);
                if (!belongs) return (null, Forbid());
            }
            resolvedAccountId = accountId.Value;
        }
        else
        {
            if (IsAdminGlobal) return (null, BadRequest(new { message = "AdminGlobal debe especificar accountId." }));
            var assignment = await _context.AccountInternalUsers
                .Where(a => a.UserId == CurrentUserId)
                .FirstOrDefaultAsync();
            if (assignment == null) return (null, NotFound(new { message = "Sin cuenta asignada." }));
            resolvedAccountId = assignment.AccountId;
        }

        var leadContactIds = await _context.Leads.AsNoTracking()
            .Where(l => l.AccountId == resolvedAccountId && l.ContactId != null && (l.Deleted ?? false) == false)
            .Select(l => l.ContactId!.Value).Distinct().ToListAsync();
        var dealContactIds = await _context.Deals.AsNoTracking()
            .Where(d => d.AccountId == resolvedAccountId && d.PrimaryContactId != null)
            .Select(d => d.PrimaryContactId!.Value).Distinct().ToListAsync();
        var ownContactIds = await _context.Contacts.AsNoTracking()
            .Where(c => c.AccountId == resolvedAccountId).Select(c => c.ContactId).ToListAsync();
        var allContactIds = leadContactIds.Union(dealContactIds).Union(ownContactIds).Distinct().ToList();

        var query = _context.Contacts.AsNoTracking().Where(c => !c.Deleted && allContactIds.Contains(c.ContactId));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var t = search.Trim();
            query = query.Where(c =>
                (c.FirstName != null && c.FirstName.Contains(t)) ||
                (c.LastName  != null && c.LastName.Contains(t))  ||
                (c.Email     != null && c.Email.Contains(t))     ||
                (c.PhoneNumber != null && c.PhoneNumber.Contains(t)));
        }
        return (query, null);
    }

    // GET /api/contacts/export?accountId=&search=  — descarga los contactos visibles en Excel
    [HttpGet("export")]
    [SwaggerOperation(Summary = "Exportar contactos a Excel")]
    public async Task<IActionResult> ExportContacts([FromQuery] int? accountId, [FromQuery] string? search)
    {
        var (query, error) = await VisibleContactsAsync(accountId, search);
        if (error != null) return error;

        var rows = await query!.OrderByDescending(c => c.CreatedOn).Take(20000)
            .Select(c => new { c.ContactId, c.FirstName, c.LastName, c.Email, c.PhoneNumber, c.Position,
                Company = c.Company != null ? c.Company.Name : null, c.LifecycleStatus, c.CreatedOn })
            .ToListAsync();

        var bytes = ProfetAPI.Services.XlsxExport.Build("Contactos",
            new[] { "ID", "Nombre", "Apellido", "Correo", "Teléfono", "Puesto", "Empresa", "Estatus", "Fecha de alta" },
            rows.Select(r => new object?[] { r.ContactId, r.FirstName, r.LastName, r.Email, r.PhoneNumber, r.Position, r.Company, r.LifecycleStatus, r.CreatedOn }));
        return File(bytes, ProfetAPI.Services.XlsxExport.ContentType, $"contactos_{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    // DELETE /api/contacts/{id}  — borrado lógico
    [HttpDelete("{id:int}")]
    [SwaggerOperation(Summary = "Eliminar contacto (borrado lógico)")]
    [SwaggerResponse(204, "Eliminado")]
    [SwaggerResponse(404, "No encontrado")]
    public async Task<IActionResult> DeleteContact(int id)
    {
        var contact = await _context.Contacts.FirstOrDefaultAsync(c => c.ContactId == id && !c.Deleted);
        if (contact == null) return NotFound(new { message = "Contacto no encontrado." });

        if (!IsAdminGlobal)
        {
            var mine = await _context.AccountInternalUsers.Where(a => a.UserId == CurrentUserId).Select(a => a.AccountId).ToListAsync();
            if (contact.AccountId == null || !mine.Contains(contact.AccountId.Value)) return Forbid();
        }

        contact.Deleted = true;
        contact.ModifiedOn = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // GET /api/contacts/{id}
    [HttpGet("{id:int}")]
    [SwaggerOperation(Summary = "Detalle de un contacto")]
    [SwaggerResponse(200, "Contacto encontrado")]
    [SwaggerResponse(404, "No encontrado")]
    public async Task<IActionResult> GetContact(int id)
    {
        var contact = await _context.Contacts
            .AsNoTracking()
            .Where(c => c.ContactId == id && !c.Deleted)
            .Select(c => new
            {
                c.ContactId, c.FirstName, c.LastName, c.Email,
                c.PhoneNumber, c.Position, c.LifecycleStatus,
                c.PostalCode, c.IsWhatsappContact, c.CreatedOn, c.ModifiedOn,
                c.CompanyId,
                company = c.Company != null ? new { c.Company.CompanyId, c.Company.Name } : null,
            })
            .FirstOrDefaultAsync();

        if (contact == null) return NotFound(new { message = "Contacto no encontrado." });

        if (!(User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value == "AdminGlobal"))
        {
            var uid = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var mine = await _context.AccountInternalUsers.Where(a => a.UserId == uid).Select(a => a.AccountId).ToListAsync();
            var ok = await _context.Contacts.AnyAsync(c => c.ContactId == id && c.AccountId != null && mine.Contains(c.AccountId.Value))
                  || await _context.Leads.AnyAsync(l => l.ContactId == id && l.AccountId != null && mine.Contains(l.AccountId.Value))
                  || await _context.Deals.AnyAsync(d => d.PrimaryContactId == id && mine.Contains(d.AccountId));
            if (!ok) return NotFound(new { message = "Contacto no encontrado." });
        }

        // Related leads
        var leads = await _context.Leads
            .AsNoTracking()
            .Where(l => l.ContactId == id && (l.Deleted ?? false) == false)
            .Select(l => new { l.LeadId, l.Name, l.Status, l.CreatedOn })
            .ToListAsync();

        // Related deals
        var deals = await _context.Deals
            .AsNoTracking()
            .Where(d => d.PrimaryContactId == id)
            .Select(d => new { d.DealId, d.DealName, d.Status, d.StageId, d.CreatedOn,
                stageName = d.Stage != null ? d.Stage.Name : null })
            .ToListAsync();

        return Ok(new
        {
            contact.ContactId, contact.FirstName, contact.LastName,
            contact.Email, contact.PhoneNumber, contact.Position,
            contact.LifecycleStatus, contact.PostalCode, contact.IsWhatsappContact,
            contact.CreatedOn, contact.ModifiedOn, contact.CompanyId, contact.company,
            leads, deals,
        });
    }

    // POST /api/contacts
    [HttpPost]
    [SwaggerOperation(Summary = "Crear contacto")]
    [SwaggerResponse(201, "Contacto creado")]
    public async Task<IActionResult> CreateContact([FromBody] ContactUpsertDto model)
    {
        var contact = new Contact
        {
            FirstName       = model.FirstName,
            LastName        = model.LastName,
            Email           = model.Email,
            PhoneNumber     = model.PhoneNumber,
            Position        = model.Position,
            PostalCode      = model.PostalCode,
            CompanyId       = model.CompanyId,
            AccountId       = model.AccountId ?? (model.CompanyId == null ? null : await _context.Companies.Where(x => x.CompanyId == model.CompanyId).Select(x => x.AccountId).FirstOrDefaultAsync()),
            LifecycleStatus = model.LifecycleStatus ?? "Lead",
            CreatedOn       = DateTime.UtcNow,
            ModifiedOn      = DateTime.UtcNow,
        };
        _context.Contacts.Add(contact);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetContact), new { id = contact.ContactId }, new { contact.ContactId });
    }

    // PUT /api/contacts/{id}
    [HttpPut("{id:int}")]
    [SwaggerOperation(Summary = "Actualizar contacto")]
    [SwaggerResponse(200, "Actualizado")]
    [SwaggerResponse(404, "No encontrado")]
    public async Task<IActionResult> UpdateContact(int id, [FromBody] ContactUpsertDto model)
    {
        var contact = await _context.Contacts.FirstOrDefaultAsync(c => c.ContactId == id && !c.Deleted);
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
}

// ── DTOs ──────────────────────────────────────────────────────────────────────
public class ContactUpsertDto
{
    public string? FirstName       { get; set; }
    public string? LastName        { get; set; }
    public string? Email           { get; set; }
    public string? PhoneNumber     { get; set; }
    public string? Position        { get; set; }
    public string? PostalCode      { get; set; }
    public int? CompanyId          { get; set; }
    public int? AccountId          { get; set; }
    public string? LifecycleStatus { get; set; }
}
