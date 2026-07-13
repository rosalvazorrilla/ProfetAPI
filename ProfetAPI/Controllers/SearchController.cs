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

    private async Task<int?> ResolveAccountId(int? accountId)
    {
        if (accountId.HasValue)
        {
            if (IsAdmin) return accountId;
            var ok = await _db.AccountInternalUsers.AnyAsync(u => u.AccountId == accountId && u.UserId == UserId);
            return ok ? accountId : null;
        }
        if (IsAdmin) return null;
        return await _db.AccountInternalUsers.Where(u => u.UserId == UserId)
            .Select(u => (int?)u.AccountId).FirstOrDefaultAsync();
    }

    // GET /api/search?q=&accountId=
    [HttpGet]
    [SwaggerOperation(Summary = "Búsqueda unificada tenant-scoped")]
    public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] int? accountId)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            return Ok(new { leads = Array.Empty<object>(), deals = Array.Empty<object>(), contacts = Array.Empty<object>(), companies = Array.Empty<object>() });

        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound(new { message = "Sin cuenta." });
        int acc = acId.Value;
        var like = $"%{q.Trim()}%";
        const int perGroup = 5;

        // Leads (directo por AccountId)
        var leads = await _db.Leads.AsNoTracking()
            .Where(l => l.AccountId == acc && l.Deleted != true &&
                (EF.Functions.Like(l.Name!, like) || EF.Functions.Like(l.Email!, like)
                 || EF.Functions.Like(l.Phone!, like) || EF.Functions.Like(l.Company!, like)))
            .OrderByDescending(l => l.CreatedOn).Take(perGroup)
            .Select(l => new { id = l.LeadId, type = "lead", title = l.Name ?? "Prospecto",
                subtitle = l.Company ?? l.Email, url = $"/prospectos?id={l.LeadId}" })
            .ToListAsync();

        // Deals (directo por AccountId)
        var deals = await _db.Deals.AsNoTracking()
            .Where(d => d.AccountId == acc && EF.Functions.Like(d.DealName, like))
            .OrderByDescending(d => d.CreatedOn).Take(perGroup)
            .Select(d => new { id = d.DealId, type = "deal", title = d.DealName,
                subtitle = d.Status, url = $"/oportunidades?id={d.DealId}" })
            .ToListAsync();

        // Contactos y empresas visibles a la cuenta (vía leads/deals)
        var contactIds = await _db.Leads.Where(l => l.AccountId == acc && l.ContactId != null)
                .Select(l => l.ContactId!.Value)
            .Union(_db.Deals.Where(d => d.AccountId == acc && d.PrimaryContactId != null)
                .Select(d => d.PrimaryContactId!.Value))
            .ToListAsync();

        var contacts = await _db.Contacts.AsNoTracking()
            .Where(c => contactIds.Contains(c.ContactId) &&
                (EF.Functions.Like(c.FirstName!, like) || EF.Functions.Like(c.LastName!, like)
                 || EF.Functions.Like(c.Email!, like) || EF.Functions.Like(c.PhoneNumber!, like)))
            .Take(perGroup)
            .Select(c => new { id = c.ContactId, type = "contact",
                title = ((c.FirstName ?? "") + " " + (c.LastName ?? "")).Trim(),
                subtitle = c.Email ?? c.PhoneNumber, url = $"/contactos?id={c.ContactId}" })
            .ToListAsync();

        var companyIds = await _db.Deals.Where(d => d.AccountId == acc && d.CompanyId != null)
                .Select(d => d.CompanyId!.Value)
            .Union(_db.Contacts.Where(c => contactIds.Contains(c.ContactId) && c.CompanyId != null)
                .Select(c => c.CompanyId!.Value))
            .ToListAsync();

        var companies = await _db.Companies.AsNoTracking()
            .Where(co => companyIds.Contains(co.CompanyId) && EF.Functions.Like(co.Name, like))
            .Take(perGroup)
            .Select(co => new { id = co.CompanyId, type = "company", title = co.Name,
                subtitle = co.City, url = $"/companias?id={co.CompanyId}" })
            .ToListAsync();

        return Ok(new { leads, deals, contacts, companies });
    }
}
