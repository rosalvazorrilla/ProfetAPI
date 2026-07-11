using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace ProfetAPI.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
[SwaggerTag("Dashboard — Métricas y KPIs del CRM")]
public class DashboardController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly MetaAdsService _metaAds;

    public DashboardController(ApplicationDbContext db, MetaAdsService metaAds)
    {
        _db      = db;
        _metaAds = metaAds;
    }

    private string? CurrentUserId   => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    private string? CurrentUserRole => User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
    private bool IsAdminGlobal      => CurrentUserRole == "AdminGlobal";

    // ── GET /api/dashboard/stats?accountId=&days=30 ───────────────────────────

    [HttpGet("stats")]
    [SwaggerOperation(Summary = "Métricas principales del dashboard")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> GetStats([FromQuery] int? accountId, [FromQuery] int days = 30)
    {
        int resolvedAccountId;
        if (accountId.HasValue)
        {
            if (!IsAdminGlobal)
            {
                var belongs = await _db.AccountInternalUsers
                    .AnyAsync(a => a.AccountId == accountId && a.UserId == CurrentUserId);
                if (!belongs) return Forbid();
            }
            resolvedAccountId = accountId.Value;
        }
        else
        {
            if (IsAdminGlobal) return BadRequest(new { message = "AdminGlobal debe especificar accountId." });
            var assignment = await _db.AccountInternalUsers
                .Where(a => a.UserId == CurrentUserId)
                .FirstOrDefaultAsync();
            if (assignment == null) return NotFound(new { message = "Sin cuenta asignada." });
            resolvedAccountId = assignment.AccountId;
        }

        var now       = DateTime.UtcNow;
        var curFrom   = now.AddDays(-days);
        var prevFrom  = curFrom.AddDays(-days);
        var prevTo    = curFrom;

        // ── Leads ─────────────────────────────────────────────────────────────
        var leadsQ = _db.Leads.AsNoTracking()
            .Where(l => l.AccountId == resolvedAccountId && l.Deleted != true);

        var leadsCur  = await leadsQ.CountAsync(l => l.CreatedOn >= curFrom);
        var leadsPrev = await leadsQ.CountAsync(l => l.CreatedOn >= prevFrom && l.CreatedOn < prevTo);
        var leadsTotal = await leadsQ.CountAsync();

        // ── Deals ─────────────────────────────────────────────────────────────
        var dealsQ = _db.Deals.AsNoTracking()
            .Where(d => d.AccountId == resolvedAccountId);

        var dealsOpen       = await dealsQ.CountAsync(d => d.Status == "Abierto");
        var dealsOpenAmount = await dealsQ.Where(d => d.Status == "Abierto")
            .SumAsync(d => (decimal?)(d.QuotedAmount ?? d.FinalAmount ?? 0)) ?? 0;

        var dealsWonCur   = await dealsQ.CountAsync(d => d.Status == "Ganado" && d.CloseDate >= curFrom);
        var dealsWonPrev  = await dealsQ.CountAsync(d => d.Status == "Ganado" && d.CloseDate >= prevFrom && d.CloseDate < prevTo);
        var dealsLostCur  = await dealsQ.CountAsync(d => d.Status == "Perdido" && d.CloseDate >= curFrom);
        var dealsLostPrev = await dealsQ.CountAsync(d => d.Status == "Perdido" && d.CloseDate >= prevFrom && d.CloseDate < prevTo);

        // Ticket promedio (deals ganados del período actual)
        var wonAmounts = await dealsQ
            .Where(d => d.Status == "Ganado" && d.CloseDate >= curFrom)
            .Select(d => d.FinalAmount ?? d.QuotedAmount ?? 0)
            .ToListAsync();
        var avgTicketCur = wonAmounts.Count > 0 ? wonAmounts.Average() : 0;

        var wonAmountsPrev = await dealsQ
            .Where(d => d.Status == "Ganado" && d.CloseDate >= prevFrom && d.CloseDate < prevTo)
            .Select(d => d.FinalAmount ?? d.QuotedAmount ?? 0)
            .ToListAsync();
        var avgTicketPrev = wonAmountsPrev.Count > 0 ? wonAmountsPrev.Average() : 0;

        // Tasa de conversión (ganados / (ganados + perdidos) en el período)
        var totalClosed    = dealsWonCur + dealsLostCur;
        var convCur        = totalClosed > 0 ? Math.Round((double)dealsWonCur / totalClosed * 100, 1) : 0;
        var totalClosedPrev = dealsWonPrev + dealsLostPrev;
        var convPrev       = totalClosedPrev > 0 ? Math.Round((double)dealsWonPrev / totalClosedPrev * 100, 1) : 0;

        // ── Serie mensual — últimos 7 meses ───────────────────────────────────
        var seriesStart = now.AddMonths(-6).Date;
        var seriesStart1 = new DateTime(seriesStart.Year, seriesStart.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var leadsMonthly = await leadsQ
            .Where(l => l.CreatedOn >= seriesStart1)
            .GroupBy(l => new { l.CreatedOn.Year, l.CreatedOn.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
            .ToListAsync();

        var dealsMonthly = await dealsQ
            .Where(d => d.CloseDate >= seriesStart1 && (d.Status == "Ganado" || d.Status == "Perdido"))
            .GroupBy(d => new { d.CloseDate!.Value.Year, d.CloseDate!.Value.Month, d.Status })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Status, Count = g.Count(),
                Amount = g.Sum(d => d.FinalAmount ?? d.QuotedAmount ?? 0) })
            .ToListAsync();

        var months = Enumerable.Range(0, 7)
            .Select(i => now.AddMonths(-6 + i))
            .Select(d => new DateTime(d.Year, d.Month, 1))
            .ToList();

        var monthlyLeads = months.Select(m => new
        {
            month   = m.ToString("yyyy-MM"),
            label   = m.ToString("MMM yy"),
            count   = leadsMonthly.FirstOrDefault(x => x.Year == m.Year && x.Month == m.Month)?.Count ?? 0,
        }).ToList();

        var monthlyDeals = months.Select(m => new
        {
            month  = m.ToString("yyyy-MM"),
            label  = m.ToString("MMM yy"),
            won    = dealsMonthly.FirstOrDefault(x => x.Year == m.Year && x.Month == m.Month && x.Status == "Ganado")?.Count ?? 0,
            lost   = dealsMonthly.FirstOrDefault(x => x.Year == m.Year && x.Month == m.Month && x.Status == "Perdido")?.Count ?? 0,
            amount = dealsMonthly.FirstOrDefault(x => x.Year == m.Year && x.Month == m.Month && x.Status == "Ganado")?.Amount ?? 0,
        }).ToList();

        // ── Leads por fuente ─────────────────────────────────────────────────
        var sourceGroups = await leadsQ
            .Where(l => l.CreatedOn >= curFrom)
            .GroupBy(l => l.ProspectSource ?? "Sin fuente")
            .Select(g => new { source = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .Take(6)
            .ToListAsync();

        var sourceTotal = sourceGroups.Sum(x => x.count);
        var leadsBySource = sourceGroups.Select(x => new
        {
            x.source,
            x.count,
            pct = sourceTotal > 0 ? Math.Round((double)x.count / sourceTotal * 100, 1) : 0,
        }).ToList();

        // ── Embudo por etapa (deals abiertos) ────────────────────────────────
        var stagesFunnel = await _db.Stages
            .AsNoTracking()
            .Where(s => s.Funnel!.AccountId == resolvedAccountId)
            .OrderBy(s => s.Order)
            .Select(s => new
            {
                s.StageId,
                s.Name,
                s.Color,
                count = _db.Deals.Count(d => d.AccountId == resolvedAccountId && d.StageId == s.StageId && d.Status == "Abierto"),
            })
            .ToListAsync();

        var totalInFunnel = stagesFunnel.Sum(s => s.count);

        var funnelWithPct = stagesFunnel.Select(s => new
        {
            s.StageId, s.Name, s.Color, s.count,
            pct = totalInFunnel > 0 ? Math.Round((double)s.count / totalInFunnel * 100, 1) : 0,
        }).ToList();

        // ── Rendimiento por vendedor ──────────────────────────────────────────
        var teamLeads = await _db.Leads
            .AsNoTracking()
            .Where(l => l.AccountId == resolvedAccountId && l.Deleted != true
                && l.CreatedOn >= curFrom && l.OwnerUserId != null)
            .GroupBy(l => new { l.OwnerUserId, l.Owner!.UserName })
            .Select(g => new { userId = g.Key.OwnerUserId, name = g.Key.UserName, leadsCount = g.Count() })
            .ToListAsync();

        var teamDeals = await _db.Deals
            .AsNoTracking()
            .Where(d => d.AccountId == resolvedAccountId
                && d.DealUsers.Any(du => du.RoleInDeal == "Owner")
                && (d.Status == "Ganado" || d.Status == "Abierto"))
            .SelectMany(d => d.DealUsers.Where(du => du.RoleInDeal == "Owner").Take(1),
                (d, du) => new { du.UserId, du.User.UserName, d.Status,
                    amount = d.FinalAmount ?? d.QuotedAmount ?? 0 })
            .GroupBy(x => new { x.UserId, x.UserName })
            .Select(g => new
            {
                userId = g.Key.UserId,
                name   = g.Key.UserName,
                open   = g.Count(x => x.Status == "Abierto"),
                won    = g.Count(x => x.Status == "Ganado"),
                amount = g.Where(x => x.Status == "Ganado").Sum(x => x.amount),
            })
            .ToListAsync();

        // Merge team data by userId
        var allUserIds = teamLeads.Select(x => x.userId)
            .Union(teamDeals.Select(x => x.userId))
            .Distinct().ToList();

        var teamStats = allUserIds.Select(uid => new
        {
            name      = teamLeads.FirstOrDefault(x => x.userId == uid)?.name
                     ?? teamDeals.FirstOrDefault(x => x.userId == uid)?.name ?? uid,
            leads     = teamLeads.FirstOrDefault(x => x.userId == uid)?.leadsCount ?? 0,
            open      = teamDeals.FirstOrDefault(x => x.userId == uid)?.open ?? 0,
            won       = teamDeals.FirstOrDefault(x => x.userId == uid)?.won ?? 0,
            amount    = teamDeals.FirstOrDefault(x => x.userId == uid)?.amount ?? 0,
        })
        .OrderByDescending(x => x.won)
        .Take(10)
        .ToList();

        return Ok(new
        {
            period = new { from = curFrom, to = now, days },

            leads = new { current = leadsCur, previous = leadsPrev, total = leadsTotal },

            dealsOpen = new { count = dealsOpen, amount = dealsOpenAmount },

            dealsWon  = new { current = dealsWonCur,  previous = dealsWonPrev,
                              amount = wonAmounts.Sum() },
            dealsLost = new { current = dealsLostCur, previous = dealsLostPrev },

            avgTicket      = new { current = Math.Round(avgTicketCur, 2),  previous = Math.Round(avgTicketPrev, 2) },
            conversionRate = new { current = convCur, previous = convPrev },

            monthlyLeads,
            monthlyDeals,
            leadsBySource,
            stagesFunnel  = funnelWithPct,
            teamStats,
        });
    }

    // ── GET /api/dashboard/meta-kpis?accountId=&days=30 ──────────────────────
    [HttpGet("meta-kpis")]
    [SwaggerOperation(Summary = "KPIs de Meta Lead Ads — leads por campaña, anuncio, formulario y salud de webhooks")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> GetMetaKpis([FromQuery] int? accountId, [FromQuery] int days = 30)
    {
        int resolvedAccountId;
        if (accountId.HasValue)
        {
            if (!IsAdminGlobal)
            {
                var belongs = await _db.AccountInternalUsers
                    .AnyAsync(a => a.AccountId == accountId && a.UserId == CurrentUserId);
                if (!belongs) return Forbid();
            }
            resolvedAccountId = accountId.Value;
        }
        else
        {
            if (IsAdminGlobal) return BadRequest(new { message = "AdminGlobal debe especificar accountId." });
            var assignment = await _db.AccountInternalUsers
                .Where(a => a.UserId == CurrentUserId)
                .FirstOrDefaultAsync();
            if (assignment == null) return NotFound(new { message = "Sin cuenta asignada." });
            resolvedAccountId = assignment.AccountId;
        }

        var now      = DateTime.UtcNow;
        var curFrom  = now.AddDays(-days);
        var prevFrom = curFrom.AddDays(-days);
        var prevTo   = curFrom;

        // ── Leads de Meta ────────────────────────────────────────────────────
        var metaQ = _db.Leads.AsNoTracking()
            .Where(l => l.AccountId == resolvedAccountId
                     && l.Deleted != true
                     && l.ProspectSource == "Meta Lead Ads");

        var totalCur  = await metaQ.CountAsync(l => l.CreatedOn >= curFrom);
        var totalPrev = await metaQ.CountAsync(l => l.CreatedOn >= prevFrom && l.CreatedOn < prevTo);

        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var thisMonth  = await metaQ.CountAsync(l => l.CreatedOn >= monthStart);

        var converted  = await metaQ.CountAsync(l => l.CreatedOn >= curFrom && l.Status != "Nuevo");

        // ── Por campaña ───────────────────────────────────────────────────────
        var campaignGroups = await metaQ
            .Where(l => l.CreatedOn >= curFrom)
            .GroupBy(l => l.CampaignName ?? "Sin campaña")
            .Select(g => new { name = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .Take(8)
            .ToListAsync();

        var campTotal  = campaignGroups.Sum(x => x.count);
        var byCampaign = campaignGroups.Select(x => new
        {
            x.name, x.count,
            pct = campTotal > 0 ? Math.Round((double)x.count / campTotal * 100, 1) : 0.0,
        }).ToList();

        // ── Por anuncio ───────────────────────────────────────────────────────
        var adGroups = await metaQ
            .Where(l => l.CreatedOn >= curFrom && l.AdName != null)
            .GroupBy(l => l.AdName!)
            .Select(g => new { name = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .Take(8)
            .ToListAsync();

        var adTotal = adGroups.Sum(x => x.count);
        var byAd    = adGroups.Select(x => new
        {
            x.name, x.count,
            pct = adTotal > 0 ? Math.Round((double)x.count / adTotal * 100, 1) : 0.0,
        }).ToList();

        // ── Serie diaria ──────────────────────────────────────────────────────
        var dailyRaw = await metaQ
            .Where(l => l.CreatedOn >= curFrom)
            .GroupBy(l => new { l.CreatedOn.Year, l.CreatedOn.Month, l.CreatedOn.Day })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, count = g.Count() })
            .ToListAsync();

        var dailySeries = Enumerable.Range(0, days)
            .Select(i => now.AddDays(-(days - 1) + i).Date)
            .Select(d => new
            {
                date  = d.ToString("yyyy-MM-dd"),
                label = d.ToString("dd MMM"),
                count = dailyRaw.FirstOrDefault(x => x.Year == d.Year && x.Month == d.Month && x.Day == d.Day)?.count ?? 0,
            }).ToList();

        // ── Webhooks Meta + salud ─────────────────────────────────────────────
        var metaWebhooks = await _db.AccountWebhooks
            .AsNoTracking()
            .Where(w => w.AccountId == resolvedAccountId && w.Platform == "MetaLeadAds")
            .Select(w => new
            {
                w.WebhookId, w.Name, w.MetaFormName, w.IsActive,
                w.TriggerCount, w.LastTriggeredAt, w.LastError,
                w.MetaAdAccountId, w.MetaPageAccessToken,
            })
            .ToListAsync();

        var webhookIds  = metaWebhooks.Select(w => w.WebhookId).ToList();
        var eventStats  = await _db.WebhookEventLogs
            .AsNoTracking()
            .Where(e => webhookIds.Contains(e.WebhookId) && e.ReceivedAt >= curFrom)
            .GroupBy(e => new { e.WebhookId, e.Status })
            .Select(g => new { g.Key.WebhookId, g.Key.Status, count = g.Count() })
            .ToListAsync();

        var byForm = metaWebhooks.Select(w =>
        {
            var success = eventStats.FirstOrDefault(e => e.WebhookId == w.WebhookId && e.Status == "Success")?.count ?? 0;
            var error   = eventStats.FirstOrDefault(e => e.WebhookId == w.WebhookId && e.Status == "Error")?.count ?? 0;
            var total   = success + error;
            return new
            {
                webhookId       = w.WebhookId,
                name            = w.Name,
                formName        = w.MetaFormName ?? w.Name,
                isActive        = w.IsActive,
                lastTriggeredAt = w.LastTriggeredAt,
                lastError       = w.LastError,
                totalEvents     = total,
                successCount    = success,
                errorCount      = error,
                successRate     = total > 0 ? Math.Round((double)success / total * 100, 1) : (total == 0 ? 100.0 : 0.0),
            };
        })
        .OrderByDescending(x => x.successCount)
        .ToList();

        var totalEvents   = eventStats.Sum(e => e.count);
        var successEvents = eventStats.Where(e => e.Status == "Success").Sum(e => e.count);
        var webhookSuccessRate = totalEvents > 0
            ? Math.Round((double)successEvents / totalEvents * 100, 1)
            : 100.0;

        var formTotal = byForm.Sum(x => x.successCount);

        // ── Meta Ads Insights (opcional — requiere MetaAdAccountId en Account) ──
        var adInsights    = new List<MetaAdsService.CampaignInsight>();
        string? adsError  = null;
        bool adsConfigured = false;

        // Usa el MetaAdAccountId de la cuenta; el token lo toma del primer webhook Meta activo
        var accountMetaId = await _db.Accounts
            .AsNoTracking()
            .Where(a => a.AccountId == resolvedAccountId)
            .Select(a => a.MetaAdAccountId)
            .FirstOrDefaultAsync();

        if (!string.IsNullOrWhiteSpace(accountMetaId))
        {
            var token = metaWebhooks
                .Where(w => !string.IsNullOrWhiteSpace(w.MetaPageAccessToken) && w.IsActive)
                .Select(w => w.MetaPageAccessToken)
                .FirstOrDefault();

            if (token != null)
            {
                adsConfigured = true;
                var (data, err) = await _metaAds.GetCampaignInsightsAsync(accountMetaId, token, curFrom, now);
                adInsights.AddRange(data);
                adsError = err;
            }
        }

        // ── Cruce por nombre de campaña ───────────────────────────────────────
        var adsDict   = adInsights
            .GroupBy(a => a.CampaignName.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First());

        var leadsDict = byCampaign.ToDictionary(
            c => c.name.ToLowerInvariant(), c => c);

        var allKeys        = leadsDict.Keys.Union(adsDict.Keys).ToList();
        var totalLeadsAll  = byCampaign.Sum(c => c.count);

        var campaignBreakdown = allKeys.Select(key =>
        {
            var lead  = leadsDict.GetValueOrDefault(key);
            var ads   = adsDict.GetValueOrDefault(key);
            var leads = lead?.count ?? 0;
            var spend = ads?.Spend ?? 0m;
            return new
            {
                campaignName = lead?.name ?? ads?.CampaignName ?? key,
                leads,
                pct         = totalLeadsAll > 0 ? Math.Round((double)leads / totalLeadsAll * 100, 1) : 0.0,
                impressions = ads?.Impressions ?? 0L,
                reach       = ads?.Reach       ?? 0L,
                clicks      = ads?.Clicks      ?? 0L,
                spend       = Math.Round(spend, 2),
                cpc         = Math.Round(ads?.Cpc ?? 0d, 2),
                cpm         = Math.Round(ads?.Cpm ?? 0d, 2),
                ctr         = Math.Round(ads?.Ctr ?? 0d, 2),
                cpl         = leads > 0 && spend > 0 ? Math.Round((double)spend / leads, 2) : 0.0,
                adsMatched  = ads != null,
            };
        })
        .OrderByDescending(x => x.leads)
        .ThenByDescending(x => x.spend)
        .ToList();

        return Ok(new
        {
            period = new { from = curFrom, to = now, days },

            summary = new
            {
                totalLeads        = totalCur,
                totalLeadsPrev    = totalPrev,
                thisMonth,
                converted,
                conversionRate    = totalCur > 0 ? Math.Round((double)converted / totalCur * 100, 1) : 0.0,
                webhookSuccessRate,
                adsConfigured,
                adsError,
                totalSpend        = Math.Round(adInsights.Sum(a => a.Spend), 2),
                totalImpressions  = adInsights.Sum(a => a.Impressions),
                totalClicks       = adInsights.Sum(a => a.Clicks),
                avgCpl            = totalCur > 0 && adInsights.Count > 0
                    ? Math.Round((double)adInsights.Sum(a => a.Spend) / totalCur, 2)
                    : 0.0,
                avgCpc            = adInsights.Count > 0 && adInsights.Any(a => a.Cpc > 0)
                    ? Math.Round(adInsights.Where(a => a.Cpc > 0).Average(a => a.Cpc), 2)
                    : 0.0,
            },

            byCampaign,
            byAd,
            byForm = byForm.Select(x => new
            {
                x.webhookId, x.name, x.formName, x.isActive,
                x.lastTriggeredAt, x.lastError,
                x.totalEvents, x.successCount, x.errorCount, x.successRate,
                count = x.successCount,
                pct   = formTotal > 0 ? Math.Round((double)x.successCount / formTotal * 100, 1) : 0.0,
            }).ToList(),
            dailySeries,
            campaignBreakdown,
        });
    }
}
