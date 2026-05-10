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
        int resolvedAccountId;
        if (accountId.HasValue)
        {
            if (!IsAdminGlobal)
            {
                var belongs = await _context.AccountInternalUsers
                    .AnyAsync(a => a.AccountId == accountId && a.UserId == CurrentUserId);
                if (!belongs) return Forbid();
            }
            resolvedAccountId = accountId.Value;
        }
        else
        {
            if (IsAdminGlobal) return BadRequest(new { message = "AdminGlobal debe especificar accountId." });
            var assignment = await _context.AccountInternalUsers
                .Where(a => a.UserId == CurrentUserId)
                .FirstOrDefaultAsync();
            if (assignment == null) return NotFound(new { message = "Sin cuenta asignada." });
            resolvedAccountId = assignment.AccountId;
        }

        // Contacts linked to leads OR deals from this account
        var leadContactIds = await _context.Leads
            .AsNoTracking()
            .Where(l => l.AccountId == resolvedAccountId && l.ContactId != null && l.Deleted != true)
            .Select(l => l.ContactId!.Value)
            .Distinct()
            .ToListAsync();

        var dealContactIds = await _context.Deals
            .AsNoTracking()
            .Where(d => d.AccountId == resolvedAccountId && d.PrimaryContactId != null)
            .Select(d => d.PrimaryContactId!.Value)
            .Distinct()
            .ToListAsync();

        var allContactIds = leadContactIds.Union(dealContactIds).Distinct().ToList();

        var query = _context.Contacts
            .AsNoTracking()
            .Where(c => allContactIds.Contains(c.ContactId));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(c =>
                (c.FirstName != null && c.FirstName.Contains(s)) ||
                (c.LastName  != null && c.LastName.Contains(s))  ||
                (c.Email     != null && c.Email.Contains(s))     ||
                (c.PhoneNumber != null && c.PhoneNumber.Contains(s)));
        }

        var total = await query.CountAsync();

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

    // GET /api/contacts/{id}
    [HttpGet("{id:int}")]
    [SwaggerOperation(Summary = "Detalle de un contacto")]
    [SwaggerResponse(200, "Contacto encontrado")]
    [SwaggerResponse(404, "No encontrado")]
    public async Task<IActionResult> GetContact(int id)
    {
        var contact = await _context.Contacts
            .AsNoTracking()
            .Where(c => c.ContactId == id)
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

        // Related leads
        var leads = await _context.Leads
            .AsNoTracking()
            .Where(l => l.ContactId == id && l.Deleted != true)
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
    public string? LifecycleStatus { get; set; }
}
