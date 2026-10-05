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

    /// <summary>Cuentas a las que pertenece el usuario (null = AdminGlobal, sin restricción).</summary>
    private async Task<List<int>?> MyAccountIdsAsync()
    {
        if (IsAdminGlobal) return null;
        return await _context.AccountInternalUsers.Where(a => a.UserId == CurrentUserId)
            .Select(a => a.AccountId).ToListAsync();
    }

    /// <summary>¿La compañía es de alguna de estas cuentas? Directa (AccountId) o, para datos
    /// anteriores al aislamiento, porque un Lead/Deal de la cuenta la liga.</summary>
    private async Task<bool> CompanyVisibleAsync(int companyId, int? companyAccountId, List<int> accounts)
    {
        if (companyAccountId.HasValue) return accounts.Contains(companyAccountId.Value);
        return await _context.Leads.AnyAsync(l => l.CompanyId == companyId && l.AccountId != null && accounts.Contains(l.AccountId.Value))
            || await _context.Deals.AnyAsync(d => d.CompanyId == companyId && accounts.Contains(d.AccountId));
    }

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

        // Compañías "de" esta cuenta: creadas directamente para ella (AccountId), o
        // conocidas indirectamente porque algún Lead o Deal de la cuenta ya las liga.
        // Sin las tres vías, una compañía creada a mano nunca aparecía en ningún lado
        // (Company no tenía AccountId propio hasta ahora).
        var dealCompanyIds = await _context.Deals.AsNoTracking()
            .Where(d => d.AccountId == resolvedAccountId && d.CompanyId != null)
            .Select(d => d.CompanyId!.Value).ToListAsync();
        var leadCompanyIds = await _context.Leads.AsNoTracking()
            .Where(l => l.AccountId == resolvedAccountId && l.CompanyId != null)
            .Select(l => l.CompanyId!.Value).ToListAsync();
        var companyIds = dealCompanyIds.Union(leadCompanyIds).Distinct().ToList();

        var query = _context.Companies
            .AsNoTracking()
            .Where(c => c.AccountId == resolvedAccountId || companyIds.Contains(c.CompanyId));

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
        var myAccounts = await MyAccountIdsAsync();
        var company = await _context.Companies
            .AsNoTracking()
            .Where(c => c.CompanyId == id)
            .Select(c => new
            {
                c.AccountId, c.CompanyId, c.Name, c.Website, c.Address,
                c.City, c.State, c.PostalCode, c.PhoneNumber,
                c.LifecycleStatus, c.CreatedOn, c.ModifiedOn,
            })
            .FirstOrDefaultAsync();

        if (company == null || (myAccounts != null && !await CompanyVisibleAsync(id, company.AccountId, myAccounts)))
            return NotFound(new { message = "Empresa no encontrada." });

        var contacts = await _context.Contacts
            .AsNoTracking()
            .Where(c => c.CompanyId == id && (myAccounts == null || (c.AccountId != null && myAccounts.Contains(c.AccountId.Value))))
            .Select(c => new { c.ContactId, c.FirstName, c.LastName, c.Email, c.PhoneNumber, c.Position })
            .ToListAsync();

        var deals = await _context.Deals
            .AsNoTracking()
            .Where(d => d.CompanyId == id && (myAccounts == null || myAccounts.Contains(d.AccountId)))
            .Select(d => new { d.DealId, d.DealName, d.Status, d.QuotedAmount,
                stageName = d.Stage != null ? d.Stage.Name : null })
            .ToListAsync();

        var leads = await _context.Leads
            .AsNoTracking()
            .Where(l => l.CompanyId == id && (l.Deleted ?? false) == false
                && (myAccounts == null || (l.AccountId != null && myAccounts.Contains(l.AccountId.Value))))
            .OrderByDescending(l => l.CreatedOn).Take(50)
            .Select(l => new { l.LeadId, l.Name, l.Status, l.Score, l.CreatedOn })
            .ToListAsync();

        return Ok(new { company, contacts, deals, leads });
    }

    /// <summary>Carga la empresa si el usuario puede verla y devuelve los ids de sus leads/deals visibles.</summary>
    private async Task<(object? company, List<long> leadIds, List<long> dealIds, int? accountId)> Company360ScopeAsync(int id)
    {
        var myAccounts = await MyAccountIdsAsync();
        var company = await _context.Companies.AsNoTracking().Where(c => c.CompanyId == id)
            .Select(c => new { c.AccountId, c.CompanyId, c.Name, c.Website, c.City, c.State, c.LifecycleStatus })
            .FirstOrDefaultAsync();
        if (company == null || (myAccounts != null && !await CompanyVisibleAsync(id, company.AccountId, myAccounts)))
            return (null, new(), new(), null);

        var leadRows = await _context.Leads.AsNoTracking()
            .Where(l => l.CompanyId == id && (l.Deleted ?? false) == false
                && (myAccounts == null || (l.AccountId != null && myAccounts.Contains(l.AccountId.Value))))
            .Select(l => new { l.LeadId, l.AccountId }).ToListAsync();
        var dealIds = await _context.Deals.AsNoTracking()
            .Where(d => d.CompanyId == id && (myAccounts == null || myAccounts.Contains(d.AccountId)))
            .Select(d => (long)d.DealId).ToListAsync();
        var accountId = company.AccountId ?? leadRows.Select(l => l.AccountId).FirstOrDefault(a => a != null);
        return (company, leadRows.Select(l => l.LeadId).ToList(), dealIds, accountId);
    }

    // GET /api/companies/{id}/timeline — actividad combinada de todos sus prospectos y oportunidades
    [HttpGet("{id:int}/timeline")]
    [SwaggerOperation(Summary = "Línea de tiempo combinada de la empresa (Vista 360)")]
    public async Task<IActionResult> GetTimeline(int id)
    {
        var (company, leadIds, dealIds, _) = await Company360ScopeAsync(id);
        if (company == null) return NotFound(new { message = "Empresa no encontrada." });

        var events = await _context.TimelineEvents.AsNoTracking()
            .Where(e => !e.Deleted
                && ((e.EntityType == "Lead" && leadIds.Contains(e.EntityId))
                 || (e.EntityType == "Deal" && dealIds.Contains(e.EntityId))))
            .OrderByDescending(e => e.CreatedOn).Take(40)
            .Select(e => new { e.TimelineEventId, e.EntityType, e.EntityId, e.Type, e.Title, e.Detail, e.CreatedOn })
            .ToListAsync();
        return Ok(events);
    }

    // POST /api/companies/{id}/summary — resumen de la cuenta redactado por IA (solo bajo demanda)
    [HttpPost("{id:int}/summary")]
    [SwaggerOperation(Summary = "Resumen IA de la empresa a partir de su historial")]
    public async Task<IActionResult> Summarize(int id, [FromServices] ProfetAPI.Services.IAiClient ai, CancellationToken ct)
    {
        if (!ai.IsConfigured) return StatusCode(503, new { message = "La IA no está configurada." });
        var (company, leadIds, dealIds, _) = await Company360ScopeAsync(id);
        if (company == null) return NotFound(new { message = "Empresa no encontrada." });

        var leads = await _context.Leads.AsNoTracking().Where(l => leadIds.Contains(l.LeadId))
            .OrderByDescending(l => l.CreatedOn).Take(10)
            .Select(l => new { l.Name, l.Status, l.Score, l.CreatedOn }).ToListAsync(ct);
        var deals = await _context.Deals.AsNoTracking().Where(d => dealIds.Contains(d.DealId))
            .OrderByDescending(d => d.CreatedOn).Take(10)
            .Select(d => new { d.DealName, d.Status, d.QuotedAmount, stage = d.Stage != null ? d.Stage.Name : null }).ToListAsync(ct);
        var events = await _context.TimelineEvents.AsNoTracking()
            .Where(e => !e.Deleted
                && ((e.EntityType == "Lead" && leadIds.Contains(e.EntityId))
                 || (e.EntityType == "Deal" && dealIds.Contains(e.EntityId))))
            .OrderByDescending(e => e.CreatedOn).Take(25)
            .Select(e => new { e.Type, e.Title, e.Detail, e.CreatedOn }).ToListAsync(ct);

        if (leads.Count == 0 && deals.Count == 0 && events.Count == 0)
            return Ok(new { summary = "Esta empresa todavía no tiene prospectos, oportunidades ni actividad registrada." });

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Empresa: " + System.Text.Json.JsonSerializer.Serialize(company));
        sb.AppendLine("Prospectos: " + System.Text.Json.JsonSerializer.Serialize(leads));
        sb.AppendLine("Oportunidades: " + System.Text.Json.JsonSerializer.Serialize(deals));
        sb.AppendLine("Actividad reciente (más nueva primero): " + System.Text.Json.JsonSerializer.Serialize(events));

        const string system = """
Eres un asesor comercial. Con los datos de una empresa cliente/prospecto, escribe un resumen ejecutivo en español de
máximo 5 líneas: en qué punto está la relación, qué oportunidades hay abiertas y cuál es el siguiente paso recomendado.
Usa SOLO los datos dados; si algo no aparece, no lo inventes ni lo supongas. Sin encabezados ni viñetas largas.
""";
        try
        {
            var text = (await ai.CompleteTextAsync(system, sb.ToString(), ct)).Trim();
            return Ok(new { summary = text });
        }
        catch (Exception)
        {
            return StatusCode(502, new { message = "No se pudo generar el resumen. Intenta de nuevo." });
        }
    }

    // POST /api/companies
    [HttpPost]
    [SwaggerOperation(Summary = "Crear empresa")]
    [SwaggerResponse(201, "Empresa creada")]
    public async Task<IActionResult> CreateCompany([FromBody] CompanyUpsertDto model)
    {
        if (model.AccountId.HasValue && !IsAdminGlobal)
        {
            var belongs = await _context.AccountInternalUsers
                .AnyAsync(a => a.AccountId == model.AccountId && a.UserId == CurrentUserId);
            if (!belongs) return Forbid();
        }

        var company = new Company
        {
            AccountId       = model.AccountId,
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

        var myAccounts = await MyAccountIdsAsync();
        if (myAccounts != null && !await CompanyVisibleAsync(id, company.AccountId, myAccounts))
            return NotFound(new { message = "Empresa no encontrada." });

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
    /// <summary>Solo se usa al crear — de qué cuenta es esta compañía. Sin esto la
    /// compañía queda huérfana y no aparece en ningún listado por cuenta.</summary>
    public int? AccountId           { get; set; }
    public string Name             { get; set; } = null!;
    public string? Website         { get; set; }
    public string? PhoneNumber     { get; set; }
    public string? Address         { get; set; }
    public string? City            { get; set; }
    public string? State           { get; set; }
    public string? PostalCode      { get; set; }
    public string? LifecycleStatus { get; set; }
}
