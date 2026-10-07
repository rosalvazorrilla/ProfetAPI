using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;

namespace ProfetAPI.Services;

/// <summary>Qué puede ver un usuario dentro de una cuenta. All = todo; si no, solo lo que pertenece a esos usuarios.</summary>
public record VisibleScope(bool All, List<string> UserIds)
{
    public static readonly VisibleScope Everything = new(true, new List<string>());
    public bool Includes(string? ownerUserId) => All || (ownerUserId != null && UserIds.Contains(ownerUserId));
}

public interface IVisibilityService
{
    /// <summary>Alcance de visibilidad del usuario en una cuenta.</summary>
    Task<VisibleScope> ForAccountAsync(ClaimsPrincipal user, int accountId);
}

/// <summary>
/// Regla "un vendedor solo ve a sus propios clientes".
///  • Ven todo: AdminGlobal, PM, Admin, ManagerAdmin, Manager y el resto del personal que no sea vendedor.
///  • Vendedores (UserCRM, User, UserEdit): solo los prospectos y oportunidades donde son el responsable;
///    si lideran un equipo, también los de las personas de ese equipo.
///  • Solo se aplica a clientes ya migrados al nuevo esquema; los demás conservan la visibilidad de siempre.
/// Lo no asignado a nadie lo ve solo quien tiene visibilidad total (quien asigna).
/// </summary>
public class VisibilityService(ApplicationDbContext db) : IVisibilityService
{
    public static readonly HashSet<string> RestrictedRoles = new() { "UserCRM", "User", "UserEdit" };

    public async Task<VisibleScope> ForAccountAsync(ClaimsPrincipal user, int accountId)
    {
        var role = user.FindFirst(ClaimTypes.Role)?.Value;
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId == null || role == null || !RestrictedRoles.Contains(role)) return VisibleScope.Everything;

        var customerId = await db.Accounts.AsNoTracking().Where(a => a.AccountId == accountId)
            .Select(a => a.CustomerId).FirstOrDefaultAsync();
        var migrated = customerId != 0 && await db.Customers.AsNoTracking().Where(c => c.Id == customerId).Select(c => c.IsMigrated).FirstOrDefaultAsync();
        if (!migrated) return VisibleScope.Everything;

        var ids = new List<string> { userId };
        var teamIds = await db.Teams.AsNoTracking().Where(t => t.CustomerId == customerId && t.LeaderId == userId).Select(t => t.Id).ToListAsync();
        if (teamIds.Count > 0)
            ids.AddRange(await db.UserTeams.AsNoTracking().Where(ut => teamIds.Contains(ut.TeamId)).Select(ut => ut.UserId).Distinct().ToListAsync());
        return new VisibleScope(false, ids.Distinct().ToList());
    }
}

/// <summary>Bloquea (404) el acceso por id a un prospecto que el vendedor no puede ver. Se usa en las rutas con {id} o {leadId}.</summary>
public class LeadVisibilityFilter(ApplicationDbContext db, IVisibilityService visibility) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var role = context.HttpContext.User.FindFirst(ClaimTypes.Role)?.Value;
        if (role == null || !VisibilityService.RestrictedRoles.Contains(role)) { await next(); return; }

        var values = context.RouteData.Values;
        var raw = (values.TryGetValue("leadId", out var l) ? l : values.TryGetValue("id", out var i) ? i : null)?.ToString();
        if (!long.TryParse(raw, out var leadId)) { await next(); return; }

        var lead = await db.Leads.AsNoTracking().Where(x => x.LeadId == leadId)
            .Select(x => new { x.AccountId, x.OwnerUserId }).FirstOrDefaultAsync();
        if (lead?.AccountId != null)
        {
            var scope = await visibility.ForAccountAsync(context.HttpContext.User, lead.AccountId.Value);
            if (!scope.Includes(lead.OwnerUserId))
            {
                context.Result = new NotFoundObjectResult(new { message = "Prospecto no encontrado." });
                return;
            }
        }
        await next();
    }
}

/// <summary>Igual que el de prospectos, para oportunidades: el vendedor solo entra a las suyas (rol en la oportunidad).</summary>
public class DealVisibilityFilter(ApplicationDbContext db, IVisibilityService visibility) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var role = context.HttpContext.User.FindFirst(ClaimTypes.Role)?.Value;
        if (role == null || !VisibilityService.RestrictedRoles.Contains(role)) { await next(); return; }

        if (!context.RouteData.Values.TryGetValue("id", out var raw) || !int.TryParse(raw?.ToString(), out var dealId)) { await next(); return; }

        var accountId = await db.Deals.AsNoTracking().Where(d => d.DealId == dealId).Select(d => (int?)d.AccountId).FirstOrDefaultAsync();
        if (accountId != null)
        {
            var scope = await visibility.ForAccountAsync(context.HttpContext.User, accountId.Value);
            if (!scope.All)
            {
                var ids = scope.UserIds;
                var visible = await db.DealUsers.AsNoTracking().AnyAsync(du => du.DealId == dealId && ids.Contains(du.UserId));
                if (!visible)
                {
                    context.Result = new NotFoundObjectResult(new { message = "Oportunidad no encontrada." });
                    return;
                }
            }
        }
        await next();
    }
}
