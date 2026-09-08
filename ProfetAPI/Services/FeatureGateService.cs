using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;

namespace ProfetAPI.Services;

/// <summary>
/// Reusa el sistema de Plan/Feature/AddOn/Subscription que ya existía (solo administrado
/// desde /admin/planes) pero nunca se consultaba en tiempo real — este es el primer lugar
/// del proyecto que de verdad revisa "¿el cliente tiene esta función?" antes de dejarlo
/// usarla. Un cliente tiene una función si su Plan la incluye O si compró el AddOn que la
/// desbloquea (sin importar el Plan base) — nunca necesita las dos cosas.
/// </summary>
public interface IFeatureGateService
{
    Task<bool> HasFeatureAsync(int customerId, string featureCode);
}

public class FeatureGateService(ApplicationDbContext db) : IFeatureGateService
{
    public async Task<bool> HasFeatureAsync(int customerId, string featureCode)
    {
        var subscription = await db.Subscriptions
            .Where(s => s.CustomerId == customerId && (s.Status == "Active" || s.Status == "Trialing"))
            .Select(s => new { s.SubscriptionId, s.PlanId })
            .FirstOrDefaultAsync();
        if (subscription == null) return false;

        var includedInPlan = await db.PlanFeatures
            .AnyAsync(pf => pf.PlanId == subscription.PlanId && pf.Feature.FeatureCode == featureCode);
        if (includedInPlan) return true;

        var now = DateTime.UtcNow;
        return await db.CustomerPurchasedAddOns
            .AnyAsync(p => p.SubscriptionId == subscription.SubscriptionId
                && p.AddOn.Feature.FeatureCode == featureCode
                && (p.ExpiryDate == null || p.ExpiryDate > now));
    }
}
