using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Dtos.Metrics;

namespace ProfetAPI.Services.Metrics;

/// <summary>
/// Motor de consultas de métricas: ejecuta EF parametrizado, SIEMPRE con AccountId, solo con
/// medidas/dimensiones del catálogo (whitelist). Nunca construye SQL desde strings del cliente/IA.
/// </summary>
public class MetricsQueryService(ApplicationDbContext db)
{
    public async Task<MetricSeriesDto> RunAsync(MetricQueryDto q, int accountId)
    {
        var result = new MetricSeriesDto { ChartType = q.ChartType, Measure = q.Measure, Dimension = q.Dimension };
        var dim = q.ChartType == "kpi" ? null : q.Dimension;

        var isDeal = q.Measure is "deals_open" or "deals_won" or "deals_lost" or "deals_amount" or "win_rate";
        if (isDeal) await RunDealAsync(q, accountId, dim, result);
        else        await RunLeadAsync(q, accountId, dim, result);

        return result;
    }

    // ── Medidas de LEADS ──────────────────────────────────────────────────────
    private async Task RunLeadAsync(MetricQueryDto q, int accountId, string? dim, MetricSeriesDto result)
    {
        var query = db.Leads.AsNoTracking().Where(l => l.AccountId == accountId && l.Deleted != true);

        if (q.From != null) query = query.Where(l => l.CreatedOn >= q.From);
        if (q.To   != null) query = query.Where(l => l.CreatedOn <= q.To);
        if (q.FilterTierId != null) query = query.Where(l => l.TierId == q.FilterTierId);
        if (!string.IsNullOrWhiteSpace(q.FilterSource)) query = query.Where(l => l.ProspectSource == q.FilterSource);
        if (!string.IsNullOrWhiteSpace(q.FilterOwner))  query = query.Where(l => l.OwnerUserId == q.FilterOwner);
        if (q.Measure == "leads_qualified") query = query.Where(l => l.TierId != null);

        // KPI (sin dimensión)
        if (dim == null)
        {
            result.Total = q.Measure == "avg_score"
                ? Math.Round(await query.Where(l => l.Score != null).AverageAsync(l => (decimal?)l.Score) ?? 0m, 1)
                : await query.CountAsync();
            result.Labels.Add("Total"); result.Values.Add(result.Total);
            return;
        }

        // Agrupado por dimensión
        List<(string Label, decimal Value)> rows;
        switch (dim)
        {
            case "source":
                rows = await AggLead(query.GroupBy(l => l.ProspectSource ?? "Sin fuente"), q.Measure);
                break;
            case "status":
                rows = await AggLead(query.GroupBy(l => l.Status), q.Measure);
                break;
            case "owner":
                rows = (await AggLead(query.GroupBy(l => l.Owner != null ? l.Owner.UserName ?? "Sin dueño" : "Sin dueño"), q.Measure));
                break;
            case "tier":
                rows = await query.GroupBy(l => l.Tier != null ? l.Tier.Name : "Sin nivel")
                    .Select(g => new { Label = g.Key, Value = q.Measure == "avg_score"
                        ? Math.Round(g.Average(x => x.Score ?? 0m), 1) : (decimal)g.Count() })
                    .Select(x => new ValueTuple<string, decimal>(x.Label!, x.Value)).ToListAsync();
                break;
            case "time":
                rows = await query.GroupBy(l => new { l.CreatedOn.Year, l.CreatedOn.Month })
                    .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                    .Select(g => new { g.Key, Value = q.Measure == "avg_score"
                        ? Math.Round(g.Average(x => x.Score ?? 0m), 1) : (decimal)g.Count() })
                    .Select(x => new ValueTuple<string, decimal>($"{x.Key.Year}-{x.Key.Month:D2}", x.Value)).ToListAsync();
                break;
            default:
                rows = new();
                break;
        }

        Fill(result, rows, dim == "time");
    }

    private static async Task<List<(string, decimal)>> AggLead(IQueryable<IGrouping<string, Models.Lead>> grouped, string measure)
    {
        if (measure == "avg_score")
            return (await grouped.Select(g => new { g.Key, V = Math.Round(g.Average(x => x.Score ?? 0m), 1) }).ToListAsync())
                .Select(x => (x.Key, x.V)).ToList();
        return (await grouped.Select(g => new { g.Key, V = (decimal)g.Count() }).ToListAsync())
            .Select(x => (x.Key, x.V)).ToList();
    }

    // ── Medidas de DEALS ──────────────────────────────────────────────────────
    private async Task RunDealAsync(MetricQueryDto q, int accountId, string? dim, MetricSeriesDto result)
    {
        var query = db.Deals.AsNoTracking().Where(d => d.AccountId == accountId);

        // Estado según la medida
        if (q.Measure == "deals_open") query = query.Where(d => d.Status == "Abierto");
        if (q.Measure == "deals_won")  query = query.Where(d => d.Status == "Ganado");
        if (q.Measure == "deals_lost") query = query.Where(d => d.Status == "Perdido");

        // Fecha: ganadas/perdidas por CloseDate; abiertas/monto por CreatedOn
        var byClose = q.Measure is "deals_won" or "deals_lost" or "win_rate";
        if (q.From != null) query = byClose ? query.Where(d => d.CloseDate >= q.From) : query.Where(d => d.CreatedOn >= q.From);
        if (q.To   != null) query = byClose ? query.Where(d => d.CloseDate <= q.To)   : query.Where(d => d.CreatedOn <= q.To);

        // win_rate: ganadas / (ganadas + perdidas)
        if (q.Measure == "win_rate")
        {
            var closed = db.Deals.AsNoTracking().Where(d => d.AccountId == accountId
                && (d.Status == "Ganado" || d.Status == "Perdido"));
            if (q.From != null) closed = closed.Where(d => d.CloseDate >= q.From);
            if (q.To   != null) closed = closed.Where(d => d.CloseDate <= q.To);

            if (dim == "time")
            {
                var g = await closed.GroupBy(d => new { d.CloseDate!.Value.Year, d.CloseDate!.Value.Month })
                    .OrderBy(x => x.Key.Year).ThenBy(x => x.Key.Month)
                    .Select(x => new { x.Key, Won = x.Count(d => d.Status == "Ganado"), Total = x.Count() })
                    .ToListAsync();
                Fill(result, g.Select(x => ($"{x.Key.Year}-{x.Key.Month:D2}",
                    x.Total > 0 ? Math.Round((decimal)x.Won / x.Total * 100, 1) : 0m)).ToList(), true);
                return;
            }
            var won = await closed.CountAsync(d => d.Status == "Ganado");
            var tot = await closed.CountAsync();
            result.Total = tot > 0 ? Math.Round((decimal)won / tot * 100, 1) : 0m;
            result.Labels.Add("Total"); result.Values.Add(result.Total);
            return;
        }

        decimal Amount(Models.Deal d) => d.FinalAmount ?? d.QuotedAmount ?? 0m;
        bool isAmount = q.Measure == "deals_amount";

        if (dim == null)
        {
            result.Total = isAmount
                ? await query.SumAsync(d => (decimal?)(d.FinalAmount ?? d.QuotedAmount ?? 0)) ?? 0m
                : await query.CountAsync();
            result.Labels.Add("Total"); result.Values.Add(result.Total);
            return;
        }

        List<(string, decimal)> rows;
        switch (dim)
        {
            case "stage":
                rows = await query.GroupBy(d => d.Stage != null ? d.Stage.Name : "Sin etapa")
                    .Select(g => new { g.Key, V = isAmount ? g.Sum(x => x.FinalAmount ?? x.QuotedAmount ?? 0) : g.Count() })
                    .Select(x => new ValueTuple<string, decimal>(x.Key!, (decimal)x.V)).ToListAsync();
                break;
            case "status":
                rows = await query.GroupBy(d => d.Status)
                    .Select(g => new { g.Key, V = isAmount ? g.Sum(x => x.FinalAmount ?? x.QuotedAmount ?? 0) : g.Count() })
                    .Select(x => new ValueTuple<string, decimal>(x.Key, (decimal)x.V)).ToListAsync();
                break;
            case "time":
                rows = await query.GroupBy(d => new { d.CreatedOn.Year, d.CreatedOn.Month })
                    .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                    .Select(g => new { g.Key, V = isAmount ? g.Sum(x => x.FinalAmount ?? x.QuotedAmount ?? 0) : g.Count() })
                    .Select(x => new ValueTuple<string, decimal>($"{x.Key.Year}-{x.Key.Month:D2}", (decimal)x.V)).ToListAsync();
                break;
            default:
                rows = new();
                break;
        }

        Fill(result, rows, dim == "time");
    }

    private static void Fill(MetricSeriesDto result, List<(string Label, decimal Value)> rows, bool keepOrder)
    {
        var ordered = keepOrder ? rows : rows.OrderByDescending(r => r.Value).ToList();
        foreach (var (label, value) in ordered)
        {
            result.Labels.Add(label);
            result.Values.Add(value);
        }
        result.Total = rows.Sum(r => r.Value);
    }
}
