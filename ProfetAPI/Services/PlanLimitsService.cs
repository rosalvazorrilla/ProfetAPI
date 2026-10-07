using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;

namespace ProfetAPI.Services;

/// <summary>Tope numérico de una función según el plan del cliente. Limit null = ilimitado.</summary>
public record PlanLimit(int? Limit, string PlanName)
{
    public bool Unlimited => Limit == null;
}

public record PlanFeatureUsage(string Code, string Name, bool Included, bool ViaAddOn, string? LimitText, int? Limit, int? Used);

public record PlanUsage(string? PlanName, decimal? MonthlyPrice, List<PlanFeatureUsage> Features, int AccountsUsed, int AiRunsThisMonth);

public interface IPlanLimitsService
{
    /// <summary>Tope del plan para una función numérica. Null = el plan no define tope (o no hay suscripción).</summary>
    Task<PlanLimit?> GetLimitAsync(int customerId, string featureCode);
    Task<int> AiRunsThisMonthAsync(int customerId);
    Task<PlanUsage> GetUsageAsync(int customerId);
}

public class PlanLimitsService(ApplicationDbContext db) : IPlanLimitsService
{
    public const string AiFeature = "AI_QUANT_LEAD_SCORE";
    public const string AccountsFeature = "MAX_ACCOUNTS";

    private static int? ParseLimit(string? text) =>
        int.TryParse(text?.Trim(), out var n) && n >= 0 ? n : null;   // "-1", vacío o texto = sin tope

    public async Task<PlanLimit?> GetLimitAsync(int customerId, string featureCode)
    {
        var sub = await db.Subscriptions.AsNoTracking()
            .Where(s => s.CustomerId == customerId && (s.Status == "Active" || s.Status == "Trialing"))
            .Select(s => new { s.PlanId, PlanName = s.Plan.Name }).FirstOrDefaultAsync();
        if (sub == null) return null;

        var row = await db.PlanFeatures.AsNoTracking()
            .Where(pf => pf.PlanId == sub.PlanId && pf.Feature.FeatureCode == featureCode)
            .Select(pf => new { pf.Limit }).FirstOrDefaultAsync();
        return row == null ? null : new PlanLimit(ParseLimit(row.Limit), sub.PlanName);
    }

    public async Task<int> AiRunsThisMonthAsync(int customerId)
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return await db.AiQuantRuns.AsNoTracking()
            .CountAsync(r => r.CustomerId == customerId && r.CreatedOn >= monthStart && r.Status != "Failed");
    }

    public async Task<PlanUsage> GetUsageAsync(int customerId)
    {
        var sub = await db.Subscriptions.AsNoTracking()
            .Where(s => s.CustomerId == customerId && (s.Status == "Active" || s.Status == "Trialing"))
            .Select(s => new { s.SubscriptionId, s.PlanId, PlanName = s.Plan.Name }).FirstOrDefaultAsync();

        var accountsUsed = await db.Accounts.AsNoTracking().CountAsync(a => a.CustomerId == customerId);
        var aiRuns = await AiRunsThisMonthAsync(customerId);
        if (sub == null) return new PlanUsage(null, null, new(), accountsUsed, aiRuns);

        var price = await db.PlanPriceHistories.AsNoTracking()
            .Where(p => p.PlanId == sub.PlanId && p.EndDate == null)
            .OrderByDescending(p => p.EffectiveDate).Select(p => (decimal?)p.MonthlyPrice).FirstOrDefaultAsync();

        // Funciones que maneja la matriz comercial (las que aparecen en algún plan) + lo que este plan incluye.
        var matrix = await db.PlanFeatures.AsNoTracking()
            .Select(pf => new { pf.FeatureId, pf.Feature.FeatureCode, pf.Feature.Name }).Distinct().ToListAsync();
        var mine = await db.PlanFeatures.AsNoTracking().Where(pf => pf.PlanId == sub.PlanId)
            .Select(pf => new { pf.FeatureId, pf.Limit }).ToListAsync();
        var now = DateTime.UtcNow;
        var addOnFeatureIds = await db.CustomerPurchasedAddOns.AsNoTracking()
            .Where(p => p.SubscriptionId == sub.SubscriptionId && (p.ExpiryDate == null || p.ExpiryDate > now))
            .Select(p => p.AddOn.FeatureId).ToListAsync();

        var features = new List<PlanFeatureUsage>();
        foreach (var f in matrix.OrderBy(f => f.FeatureId))
        {
            var m = mine.FirstOrDefault(x => x.FeatureId == f.FeatureId);
            var inPlan = m != null;
            var viaAddOn = !inPlan && addOnFeatureIds.Contains(f.FeatureId);
            int? limit = inPlan ? ParseLimit(m!.Limit) : null;
            int? used = f.FeatureCode switch
            {
                AiFeature       => aiRuns,
                AccountsFeature => accountsUsed,
                _               => null,
            };
            features.Add(new PlanFeatureUsage(f.FeatureCode, f.Name, inPlan || viaAddOn, viaAddOn, inPlan ? m!.Limit : null, limit, used));
        }
        return new PlanUsage(sub.PlanName, price, features, accountsUsed, aiRuns);
    }
}
