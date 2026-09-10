using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Models;
using ProfetAPI.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace ProfetAPI.Controllers;

// ── AI QUANT — Lead Score: lanzar y consultar corridas de investigación IA ──

[Route("api")]
[ApiController]
[Authorize]
[SwaggerTag("CRM — AI QUANT (investigación IA del prospecto)")]
public class AiQuantController(
    ApplicationDbContext db,
    IFeatureGateService featureGate,
    IAiQuantService aiQuant) : ControllerBase
{
    private const string FeatureCode = "AI_QUANT_LEAD_SCORE";
    private const decimal EstimatedRunCostUsd = 0.50m; // colchón para el chequeo de tope

    private string? UserId   => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    private bool IsAdmin     => User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value == "AdminGlobal";

    // ── POST /api/leads/{id}/ai-quant/run ─────────────────────────────────────
    [HttpPost("leads/{id:long}/ai-quant/run")]
    [SwaggerOperation(Summary = "Lanzar una corrida de AI QUANT para un prospecto (async)")]
    public async Task<IActionResult> Run(long id, [FromBody] AiQuantRunRequest? req)
    {
        var lead = await db.Leads.AsNoTracking()
            .Where(l => l.LeadId == id && (l.Deleted ?? false) == false)
            .Select(l => new { l.AccountId })
            .FirstOrDefaultAsync();
        if (lead?.AccountId == null) return NotFound(new { message = "Prospecto no encontrado." });
        var accountId = lead.AccountId.Value;

        var customerId = await db.Accounts.AsNoTracking()
            .Where(a => a.AccountId == accountId).Select(a => a.CustomerId).FirstOrDefaultAsync();

        if (!IsAdmin)
        {
            var belongs = await db.AccountInternalUsers
                .AnyAsync(u => u.AccountId == accountId && u.UserId == UserId);
            if (!belongs) return Forbid();
        }

        if (!await featureGate.HasFeatureAsync(customerId, FeatureCode))
            return StatusCode(403, new { message = "Esta función no está incluida en tu plan.", featureCode = FeatureCode });

        if (!aiQuant.IsConfigured)
            return StatusCode(503, new { message = "El análisis con IA no está disponible en este momento." });

        // Chequeo de tope mensual (soft): gasto del mes en curso + costo estimado de esta corrida.
        var cap = await db.Customers.AsNoTracking()
            .Where(c => c.Id == customerId).Select(c => c.AiQuantMonthlyCapUsd).FirstOrDefaultAsync();
        if (cap.HasValue)
        {
            var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var spent = await db.AiQuantRuns
                .Where(r => r.CustomerId == customerId && r.CreatedOn >= monthStart && r.CostUsd != null)
                .SumAsync(r => r.CostUsd) ?? 0m;
            if (spent + EstimatedRunCostUsd > cap.Value)
                return StatusCode(402, new { message = "Llegaste al tope mensual de AI QUANT. Contacta a tu administrador para subirlo." });
        }

        // Ya hay una corrida en curso para este lead — no encimar.
        var inFlight = await db.AiQuantRuns
            .AnyAsync(r => r.LeadId == id && (r.Status == "Pending" || r.Status == "Running"));
        if (inFlight) return Conflict(new { message = "Ya hay un análisis en curso para este prospecto." });

        var run = new AiQuantRun
        {
            LeadId         = id,
            AccountId      = accountId,
            CustomerId     = customerId,
            RunByUserId    = UserId,
            Status         = "Pending",
            InputWebsite   = string.IsNullOrWhiteSpace(req?.Website) ? null : req!.Website!.Trim(),
            InputCallNotes = string.IsNullOrWhiteSpace(req?.CallNotes) ? null : req!.CallNotes!.Trim(),
        };
        db.AiQuantRuns.Add(run);
        await db.SaveChangesAsync();

        return Accepted(new { runId = run.RunId, status = run.Status });
    }

    // ── GET /api/leads/{id}/ai-quant ──────────────────────────────────────────
    [HttpGet("leads/{id:long}/ai-quant")]
    [SwaggerOperation(Summary = "Corrida más reciente de AI QUANT del prospecto (para polling)")]
    public async Task<IActionResult> Latest(long id)
    {
        var run = await db.AiQuantRuns.AsNoTracking()
            .Where(r => r.LeadId == id)
            .OrderByDescending(r => r.CreatedOn)
            .FirstOrDefaultAsync();
        if (run == null) return Ok(new { status = (string?)null });

        var runByName = await ResolveUserName(run.RunByUserId);
        return Ok(new
        {
            run.RunId, run.Status, run.Score, run.Tier,
            result = run.ResultJson,   // JSON crudo — el frontend lo parsea
            run.Error, cost = run.CostUsd,
            run.CreatedOn, run.CompletedOn, runByName,
        });
    }

    // ── GET /api/leads/{id}/ai-quant/history ──────────────────────────────────
    [HttpGet("leads/{id:long}/ai-quant/history")]
    [SwaggerOperation(Summary = "Corridas pasadas de AI QUANT del prospecto")]
    public async Task<IActionResult> History(long id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var q = db.AiQuantRuns.AsNoTracking().Where(r => r.LeadId == id);
        var total = await q.CountAsync();
        var rows = await q.OrderByDescending(r => r.CreatedOn)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new { r.RunId, r.Status, r.Score, r.Tier, r.CostUsd, r.CreatedOn, r.RunByUserId })
            .ToListAsync();

        var names = await ResolveUserNames(rows.Select(r => r.RunByUserId));
        var data = rows.Select(r => new
        {
            r.RunId, r.Status, r.Score, r.Tier, r.CostUsd, r.CreatedOn,
            runByName = r.RunByUserId != null && names.TryGetValue(r.RunByUserId, out var n) ? n : null,
        });
        return Ok(new { total, page, pageSize, data });
    }

    // ── GET /api/ai-quant/usage?customerId=&month=yyyy-MM ──────────────────────
    [HttpGet("ai-quant/usage")]
    [SwaggerOperation(Summary = "Gasto de AI QUANT del cliente en un mes")]
    public async Task<IActionResult> Usage([FromQuery] int? customerId, [FromQuery] string? month)
    {
        int custId;
        if (IsAdmin)
        {
            if (customerId == null) return BadRequest(new { message = "AdminGlobal debe indicar customerId." });
            custId = customerId.Value;
        }
        else
        {
            custId = await db.Users.Where(u => u.Id == UserId).Select(u => u.CustomerId).FirstOrDefaultAsync()
                  ?? await db.AccountInternalUsers.Where(u => u.UserId == UserId)
                       .Select(u => (int?)u.Account.CustomerId).FirstOrDefaultAsync() ?? 0;
            if (custId == 0) return NotFound(new { message = "Sin cliente asignado." });
        }

        DateTime monthStart = DateTime.TryParseExact((month ?? ""), "yyyy-MM", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? new DateTime(parsed.Year, parsed.Month, 1, 0, 0, 0, DateTimeKind.Utc)
            : new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthEnd = monthStart.AddMonths(1);

        var runsInMonth = db.AiQuantRuns.AsNoTracking()
            .Where(r => r.CustomerId == custId && r.CreatedOn >= monthStart && r.CreatedOn < monthEnd);
        var runs = await runsInMonth.CountAsync();
        var cost = await runsInMonth.Where(r => r.CostUsd != null).SumAsync(r => r.CostUsd) ?? 0m;
        var cap  = await db.Customers.AsNoTracking().Where(c => c.Id == custId)
            .Select(c => c.AiQuantMonthlyCapUsd).FirstOrDefaultAsync();

        return Ok(new { month = monthStart.ToString("yyyy-MM"), runs, costUsd = cost, cap });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private async Task<string?> ResolveUserName(string? userId)
    {
        if (string.IsNullOrEmpty(userId)) return null;
        var names = await ResolveUserNames(new[] { userId });
        return names.TryGetValue(userId, out var n) ? n : null;
    }

    private async Task<Dictionary<string, string>> ResolveUserNames(IEnumerable<string?> userIds)
    {
        var ids = userIds.Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).Distinct().ToList();
        if (ids.Count == 0) return new();
        return await db.UserProfiles.AsNoTracking()
            .Where(p => ids.Contains(p.UserId))
            .Select(p => new { p.UserId, Name = ((p.FirstName ?? "") + " " + (p.LastName ?? "")).Trim() })
            .ToDictionaryAsync(x => x.UserId, x => string.IsNullOrWhiteSpace(x.Name) ? "—" : x.Name);
    }
}

public class AiQuantRunRequest
{
    public string? Website { get; set; }
    public string? CallNotes { get; set; }
}
