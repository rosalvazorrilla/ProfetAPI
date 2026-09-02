using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;

namespace ProfetAPI.Services;

/// <summary>
/// Resuelve a quién le toca un lead nuevo según Account.AssignmentType — el
/// ajuste ya existía en Configuración → General de cada cuenta ("Carrusel /
/// round-robin") pero ningún flujo de creación de leads lo usaba todavía.
/// Se llama SOLO cuando el lead entra sin responsable explícito (ni owner
/// mandado por la integración, ni asignado a mano); si ya viene con owner,
/// no se toca.
/// </summary>
public class LeadAssignmentService(ApplicationDbContext db)
{
    public async Task<string?> ResolveOwnerAsync(int accountId)
    {
        var account = await db.Accounts.AsNoTracking()
            .Where(a => a.AccountId == accountId)
            .Select(a => new { a.AssignmentType })
            .FirstOrDefaultAsync();

        // Sin cuenta o sin modo de asignación configurado: no se asigna nadie.
        if (account == null || string.IsNullOrEmpty(account.AssignmentType))
            return null;

        // Hoy solo existe "Carrusel" en la UI, pero se deja el switch abierto
        // para el día que se agregue otro modo (ej. "Manual", "Fijo").
        return account.AssignmentType switch
        {
            "Carrusel" => await ResolveRoundRobinAsync(accountId),
            _ => null,
        };
    }

    private async Task<string?> ResolveRoundRobinAsync(int accountId)
    {
        // Vendedores activos de la cuenta, orden estable (por Id) para que el
        // carrusel sea predecible.
        var availableUserIds = await db.AccountInternalUsers
            .Where(a => a.AccountId == accountId && !(a.User.Deleted ?? false) && (a.User.Active ?? false))
            .OrderBy(a => a.UserId)
            .Select(a => a.UserId)
            .ToListAsync();

        if (availableUserIds.Count == 0) return null;

        // Último lead de la cuenta que sí tiene responsable — para saber a quién
        // le tocó la última vez y asignar al siguiente en la lista (wrap-around).
        var lastOwnerId = await db.Leads.AsNoTracking()
            .Where(l => l.AccountId == accountId && l.OwnerUserId != null)
            .OrderByDescending(l => l.CreatedOn)
            .Select(l => l.OwnerUserId)
            .FirstOrDefaultAsync();

        if (lastOwnerId == null) return availableUserIds[0];

        var lastIndex = availableUserIds.IndexOf(lastOwnerId);
        if (lastIndex == -1) return availableUserIds[0]; // el último dueño ya no está activo en la cuenta

        var nextIndex = (lastIndex + 1) % availableUserIds.Count;
        return availableUserIds[nextIndex];
    }
}
