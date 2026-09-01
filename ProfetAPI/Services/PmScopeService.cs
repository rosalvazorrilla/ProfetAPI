using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;

namespace ProfetAPI.Services;

/// <summary>
/// Restringe qué clientes puede ver/gestionar un usuario interno según su rol.
/// AdminGlobal ve todo (sin restricción). PM solo ve los clientes que tiene
/// asignados en PmCustomerAssignments. Se usa en todos los controladores del
/// panel Admin Global que exponen datos por cliente.
/// </summary>
public class PmScopeService(ApplicationDbContext db)
{
    /// <summary>
    /// null = sin restricción (AdminGlobal, ve todos los clientes).
    /// Lista = los CustomerId a los que un PM tiene acceso (puede estar vacía).
    /// </summary>
    public async Task<List<int>?> GetAccessibleCustomerIdsAsync(ClaimsPrincipal user)
    {
        if (user.IsInRole("AdminGlobal")) return null;

        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return new List<int>();

        return await db.PmCustomerAssignments
            .Where(a => a.PmUserId == userId)
            .Select(a => a.CustomerId)
            .ToListAsync();
    }

    public async Task<bool> CanAccessCustomerAsync(ClaimsPrincipal user, int customerId)
    {
        var ids = await GetAccessibleCustomerIdsAsync(user);
        return ids == null || ids.Contains(customerId);
    }
}
