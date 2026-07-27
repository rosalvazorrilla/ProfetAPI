using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using Swashbuckle.AspNetCore.Annotations;
using System.Security.Claims;

namespace ProfetAPI.Controllers;

[Route("api/search")]
[ApiController]
[Authorize]
[SwaggerTag("Búsqueda global (leads, oportunidades, contactos, empresas)")]
public class SearchController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    public SearchController(ApplicationDbContext db) => _db = db;

    private string? UserId  => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    private bool    IsAdmin => User.FindFirst(ClaimTypes.Role)?.Value == "AdminGlobal";

    /// <summary>
    /// Cuentas donde el usuario puede buscar. AdminGlobal: todas (búsqueda de verdad global),
    /// salvo que pida una específica. Usuario normal/PM: TODAS las que tenga asignadas en
    /// AccountInternalUsers (antes solo tomaba la primera, ignorando el resto).
    /// Null = sin restricción (buscar en todas las cuentas del sistema).
    /// </summary>
    private async Task<List<int>?> ResolveAccountIds(int? accountId)
    {
        if (accountId.HasValue)
        {
            if (IsAdmin) return new List<int> { accountId.Value };
            var ok = await _db.AccountInternalUsers.AnyAsync(u => u.AccountId == accountId && u.UserId == UserId);
            return ok ? new List<int> { accountId.Value } : new List<int>();
        }
        if (IsAdmin) return null; // sin filtro: todas las cuentas del sistema
        return await _db.AccountInternalUsers.Where(u => u.UserId == UserId)
            .Select(u => u.AccountId).ToListAsync();
    }

    // GET /api/search?q=&accountId=
    [HttpGet]
    [SwaggerOperation(Summary = "Búsqueda unificada — todas las cuentas accesibles por el usuario")]
    public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] int? accountId)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            return Ok(new { leads = Array.Empty<object>(), deals = Array.Empty<object>(), contacts = Array.Empty<object>(), companies = Array.Empty<object>() });

        var accIds = await ResolveAccountIds(accountId);
        if (accIds != null && accIds.Count == 0) return Ok(new { leads = Array.Empty<object>(), deals = Array.Empty<object>(), contacts = Array.Empty<object>(), companies = Array.Empty<object>() });

        var like = $"%{q.Trim()}%";
        const int perGroup = 5;

        // Leads (directo por AccountId) — si ya se convirtió a Deal, no lo repetimos aquí,
        // la oportunidad ya aparece en el grupo de Deals.
        var leadsRaw = await _db.Leads.AsNoTracking()
            .Where(l => (accIds == null || (l.AccountId != null && accIds.Contains(l.AccountId.Value))) && (l.Deleted ?? false) == false
                 && l.Status != "Convertido" &&
                (EF.Functions.Like(l.Name!, like) || EF.Functions.Like(l.Email!, like)
                 || EF.Functions.Like(l.Phone!, like) || EF.Functions.Like(l.Company!, like)))
            .OrderByDescending(l => l.CreatedOn).Take(perGroup)
            .Select(l => new { l.LeadId, l.Name, l.Company, l.Email, l.AccountId })
            .ToListAsync();

        // Deals (directo por AccountId)
        var dealsRaw = await _db.Deals.AsNoTracking()
            .Where(d => (accIds == null || accIds.Contains(d.AccountId)) && EF.Functions.Like(d.DealName, like))
            .OrderByDescending(d => d.CreatedOn).Take(perGroup)
            .Select(d => new { d.DealId, d.DealName, d.Status, d.AccountId })
            .ToListAsync();

        // Contactos y empresas visibles en las cuentas resueltas (vía leads/deals)
        var contactIdsQ = _db.Leads.Where(l => (accIds == null || (l.AccountId != null && accIds.Contains(l.AccountId.Value))) && l.ContactId != null)
                .Select(l => l.ContactId!.Value)
            .Union(_db.Deals.Where(d => (accIds == null || accIds.Contains(d.AccountId)) && d.PrimaryContactId != null)
                .Select(d => d.PrimaryContactId!.Value));
        var contactIds = await contactIdsQ.ToListAsync();

        var contactsRaw = await _db.Contacts.AsNoTracking()
            .Where(c => contactIds.Contains(c.ContactId) &&
                (EF.Functions.Like(c.FirstName!, like) || EF.Functions.Like(c.LastName!, like)
                 || EF.Functions.Like(c.Email!, like) || EF.Functions.Like(c.PhoneNumber!, like)))
            .Take(perGroup)
            .Select(c => new { c.ContactId, c.FirstName, c.LastName, c.Email, c.PhoneNumber })
            .ToListAsync();

        var companyIdsQ = _db.Deals.Where(d => (accIds == null || accIds.Contains(d.AccountId)) && d.CompanyId != null)
                .Select(d => d.CompanyId!.Value)
            .Union(_db.Contacts.Where(c => contactIds.Contains(c.ContactId) && c.CompanyId != null)
                .Select(c => c.CompanyId!.Value));
        var companyIds = await companyIdsQ.ToListAsync();

        var companiesRaw = await _db.Companies.AsNoTracking()
            .Where(co => companyIds.Contains(co.CompanyId) && EF.Functions.Like(co.Name, like))
            .Take(perGroup)
            .Select(co => new { co.CompanyId, co.Name, co.City })
            .ToListAsync();

        // Cuenta + cliente de cada resultado — solo tiene sentido para AdminGlobal, que
        // puede ver varios clientes a la vez. Un usuario normal ya sabe a qué cuenta
        // pertenece (es la suya), así que mostrarlo ahí sería ruido.
        var involvedAccountIds = leadsRaw.Select(l => l.AccountId).Where(a => a.HasValue).Select(a => a!.Value)
            .Union(dealsRaw.Select(d => d.AccountId)).Distinct().ToList();
        Dictionary<int, string> accountLabels = new();
        if (IsAdmin && involvedAccountIds.Count > 0)
        {
            var accountInfo = await _db.Accounts.AsNoTracking()
                .Where(a => involvedAccountIds.Contains(a.AccountId))
                .Select(a => new { a.AccountId, a.Name, a.CustomerId })
                .ToListAsync();
            var customerNames = await _db.Customers.AsNoTracking()
                .Where(c => accountInfo.Select(a => a.CustomerId).Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name);
            accountLabels = accountInfo.ToDictionary(a => a.AccountId,
                a => customerNames.TryGetValue(a.CustomerId, out var cust) ? $"{a.Name} ({cust})" : a.Name);
        }

        var leads = leadsRaw.Select(l => new { id = l.LeadId, type = "lead", title = l.Name ?? "Prospecto",
            subtitle = CombineSubtitle(l.Company ?? l.Email, l.AccountId.HasValue ? accountLabels.GetValueOrDefault(l.AccountId.Value) : null),
            url = $"/prospectos?id={l.LeadId}" });

        var deals = dealsRaw.Select(d => new { id = d.DealId, type = "deal", title = d.DealName,
            subtitle = CombineSubtitle(d.Status, accountLabels.GetValueOrDefault(d.AccountId)),
            url = $"/oportunidades?id={d.DealId}" });

        var contacts = contactsRaw.Select(c => new { id = c.ContactId, type = "contact",
            title = ((c.FirstName ?? "") + " " + (c.LastName ?? "")).Trim(),
            subtitle = c.Email ?? c.PhoneNumber, url = $"/contactos?id={c.ContactId}" });

        var companies = companiesRaw.Select(co => new { id = co.CompanyId, type = "company", title = co.Name,
            subtitle = co.City, url = $"/companias?id={co.CompanyId}" });

        return Ok(new { leads, deals, contacts, companies });
    }

    private static string? CombineSubtitle(string? primary, string? accountName)
    {
        if (string.IsNullOrEmpty(accountName)) return primary;
        return string.IsNullOrEmpty(primary) ? accountName : $"{primary} · {accountName}";
    }
}
