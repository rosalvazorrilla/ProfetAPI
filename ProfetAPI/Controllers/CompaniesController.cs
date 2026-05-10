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
[SwaggerTag("CRM — Empresas")]
public class CompaniesController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    public CompaniesController(ApplicationDbContext context) => _context = context;

    private string? CurrentUserId => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    private string? CurrentUserRole => User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
    private bool IsAdminGlobal => CurrentUserRole == "AdminGlobal";

    // GET /api/companies?accountId=&search=&page=1&pageSize=50
    [HttpGet]
    [SwaggerOperation(Summary = "Listar empresas del CRM")]
    [SwaggerResponse(200, "Lista de empresas")]
    public async Task<IActionResult> GetCompanies(
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

        // Companies linked to deals from this account
        var companyIds = await _context.Deals
            .AsNoTracking()
            .Where(d => d.AccountId == resolvedAccountId && d.CompanyId != null)
            .Select(d => d.CompanyId!.Value)
            .Distinct()
            .ToListAsync();

        var query = _context.Companies
            .AsNoTracking()
            .Where(c => companyIds.Contains(c.CompanyId));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(c => c.Name.Contains(s) ||
                (c.Website != null && c.Website.Contains(s)) ||
                (c.City    != null && c.City.Contains(s)));
        }

        var total = await query.CountAsync();

        var companies = await query
            .OrderByDescending(c => c.CreatedOn)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new
            {
                c.CompanyId, c.Name, c.Website, c.City, c.State,
                c.PhoneNumber, c.LifecycleStatus, c.CreatedOn,
                contactCount = c.Contacts.Count(),
                dealCount    = c.Deals.Count(d => d.AccountId == resolvedAccountId),
            })
            .ToListAsync();

        return Ok(new { total, page, pageSize, data = companies });
    }

    // GET /api/companies/{id}
    [HttpGet("{id:int}")]
    [SwaggerOperation(Summary = "Detalle de una empresa")]
    [SwaggerResponse(200, "Empresa encontrada")]
    [SwaggerResponse(404, "No encontrada")]
    public async Task<IActionResult> GetCompany(int id)
    {
        var company = await _context.Companies
            .AsNoTracking()
            .Where(c => c.CompanyId == id)
            .Select(c => new
            {
                c.CompanyId, c.Name, c.Website, c.Address,
                c.City, c.State, c.PostalCode, c.PhoneNumber,
                c.LifecycleStatus, c.CreatedOn, c.ModifiedOn,
            })
            .FirstOrDefaultAsync();

        if (company == null) return NotFound(new { message = "Empresa no encontrada." });

        var contacts = await _context.Contacts
            .AsNoTracking()
            .Where(c => c.CompanyId == id)
            .Select(c => new { c.ContactId, c.FirstName, c.LastName, c.Email, c.PhoneNumber, c.Position })
            .ToListAsync();

        var deals = await _context.Deals
            .AsNoTracking()
            .Where(d => d.CompanyId == id)
            .Select(d => new { d.DealId, d.DealName, d.Status, d.QuotedAmount,
                stageName = d.Stage != null ? d.Stage.Name : null })
            .ToListAsync();

        return Ok(new { company, contacts, deals });
    }

    // POST /api/companies
    [HttpPost]
    [SwaggerOperation(Summary = "Crear empresa")]
    [SwaggerResponse(201, "Empresa creada")]
    public async Task<IActionResult> CreateCompany([FromBody] CompanyUpsertDto model)
    {
        var company = new Company
        {
            Name            = model.Name,
            Website         = model.Website,
            PhoneNumber     = model.PhoneNumber,
            Address         = model.Address,
            City            = model.City,
            State           = model.State,
            PostalCode      = model.PostalCode,
            LifecycleStatus = model.LifecycleStatus ?? "Prospecto",
            CreatedOn       = DateTime.UtcNow,
            ModifiedOn      = DateTime.UtcNow,
        };
        _context.Companies.Add(company);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetCompany), new { id = company.CompanyId }, new { company.CompanyId, company.Name });
    }

    // PUT /api/companies/{id}
    [HttpPut("{id:int}")]
    [SwaggerOperation(Summary = "Actualizar empresa")]
    [SwaggerResponse(200, "Actualizada")]
    [SwaggerResponse(404, "No encontrada")]
    public async Task<IActionResult> UpdateCompany(int id, [FromBody] CompanyUpsertDto model)
    {
        var company = await _context.Companies.FindAsync(id);
        if (company == null) return NotFound(new { message = "Empresa no encontrada." });

        if (!string.IsNullOrWhiteSpace(model.Name)) company.Name = model.Name;
        company.Website         = model.Website         ?? company.Website;
        company.PhoneNumber     = model.PhoneNumber     ?? company.PhoneNumber;
        company.Address         = model.Address         ?? company.Address;
        company.City            = model.City            ?? company.City;
        company.State           = model.State           ?? company.State;
        company.PostalCode      = model.PostalCode      ?? company.PostalCode;
        company.LifecycleStatus = model.LifecycleStatus ?? company.LifecycleStatus;
        company.ModifiedOn      = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(new { company.CompanyId, updated = true });
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────────────
public class CompanyUpsertDto
{
    public string Name             { get; set; } = null!;
    public string? Website         { get; set; }
    public string? PhoneNumber     { get; set; }
    public string? Address         { get; set; }
    public string? City            { get; set; }
    public string? State           { get; set; }
    public string? PostalCode      { get; set; }
    public string? LifecycleStatus { get; set; }
}
