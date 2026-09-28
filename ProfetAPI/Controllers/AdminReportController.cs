using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using Swashbuckle.AspNetCore.Annotations;

namespace ProfetAPI.Controllers;

/// <summary>
/// Reporte ejecutivo de uso real de la plataforma — cuánto se está usando cada función
/// de IA/automatización, con datos de la base, no estimados. Pensado para mostrar avance
/// a junta/socios: "esto es lo que la IA ya está haciendo por los clientes", no una demo.
/// Solo AdminGlobal — agrega across todos los clientes (o uno solo si se pasa customerId).
/// </summary>
[Route("api/admin/usage-report")]
[ApiController]
[Authorize(Roles = "AdminGlobal")]
[SwaggerTag("Reporte ejecutivo de uso de la plataforma")]
public class AdminReportController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(Summary = "Uso real de IA/automatización en un período, con desglose por cliente")]
    public async Task<IActionResult> GetUsageReport([FromQuery] int days = 30, [FromQuery] int? customerId = null)
    {
        var now  = DateTime.UtcNow;
        var from = now.AddDays(-days);

        // Reporte de fondo, no una pantalla que se abre a cada rato — sin cliente
        // puntual escanea varias tablas de TODAS las cuentas, y el timeout por
        // defecto (30s) no siempre alcanza en el tier de Azure SQL de Profet_new.
        db.Database.SetCommandTimeout(90);

        // ── Resolver las cuentas dentro del alcance (un cliente puntual, o todas) ──
        var accountsQ = db.Accounts.AsNoTracking().Where(a => a.Status == "Activo");
        if (customerId.HasValue) accountsQ = accountsQ.Where(a => a.CustomerId == customerId.Value);
        var accounts = await accountsQ
            .Select(a => new { a.AccountId, a.CustomerId, CustomerName = a.Customer.Name })
            .ToListAsync();
        var accountIds = accounts.Select(a => a.AccountId).ToList();

        // ── Leads: UN solo fetch por el índice que sí existe (AccountId, CreatedOn),
        // todo lo demás (calificación IA, quién necesita ayuda, desglose por cliente)
        // se calcula en memoria sobre esas filas. Filtrar por ScoredAt/Status/Owner
        // directo en SQL no tiene índice, y en el tier de Azure SQL de Profet_new eso
        // se traduce en un table scan que se queda esperando cupo de I/O
        // (IO_QUEUE_LIMIT) sin avanzar — lo confirmamos armando este mismo reporte. ──
        var leadsInRange = await db.Leads.AsNoTracking()
            .Where(l => (l.Deleted ?? false) == false && l.AccountId != null && accountIds.Contains(l.AccountId!.Value)
                     && l.CreatedOn >= from)
            .Select(l => new { l.AccountId, l.ScoredAt, l.ScoreSource, l.Status, l.OwnerUserId, l.CreatedOn })
            .ToListAsync();

        var leadsCreated      = leadsInRange.Count;
        var leadsScoredAi     = leadsInRange.Count(l => l.ScoredAt != null && (l.ScoreSource == "AI" || l.ScoreSource == "Hybrid"));
        var leadsScoredManual = leadsInRange.Count(l => l.ScoredAt != null && l.ScoreSource == "Manual");

        // "Necesita ayuda" — mismo criterio que el panel de Best practices del
        // dashboard, aplicado a los leads creados en este período.
        var needingAttention = leadsInRange.Count(l =>
            (l.Status == "Nuevo" || l.Status == "Contactado" || l.Status == "Calificado") &&
            (l.OwnerUserId == null || (l.Status == "Nuevo" ? l.CreatedOn < now.AddHours(-24) : l.CreatedOn < now.AddDays(-3))));

        // ── AI QUANT ──────────────────────────────────────────────────────────────
        var aiQuantQ = db.AiQuantRuns.AsNoTracking()
            .Where(r => r.CreatedOn >= from && accountIds.Contains(r.AccountId));
        var aiQuantRuns = await aiQuantQ.CountAsync(r => r.Status == "Done");
        var aiQuantCost = await aiQuantQ.Where(r => r.Status == "Done").SumAsync(r => (decimal?)r.CostUsd) ?? 0m;
        var aiQuantAvgScore = await aiQuantQ.Where(r => r.Status == "Done" && r.Score != null)
            .Select(r => (double)r.Score!.Value).DefaultIfEmpty().AverageAsync();
        var aiQuantByAccount = await aiQuantQ.Where(r => r.Status == "Done")
            .GroupBy(r => r.AccountId).Select(g => new { AccountId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AccountId, x => x.Count);

        // ── Secuencias / automatizaciones ────────────────────────────────────────
        var seqTasksQ = db.Activities.AsNoTracking()
            .Where(a => a.ActivityType == "Task" && a.SourcePlaybookTaskId != null && a.CreatedOn >= from
                     && a.AccountId != null && accountIds.Contains(a.AccountId!.Value));
        var seqTasksGenerated = await seqTasksQ.CountAsync();
        var seqTasksCompleted = await seqTasksQ.CountAsync(a => a.TaskStatus == "Completada");
        var seqLeadsReached = await seqTasksQ.Where(a => a.EntityType == "Lead")
            .Select(a => a.EntityId).Distinct().CountAsync();

        // ── WhatsApp ──────────────────────────────────────────────────────────────
        var waContactIds = db.ContactsWhatsapp.AsNoTracking()
            .Where(c => c.AccountId != null && accountIds.Contains(c.AccountId!.Value))
            .Select(c => c.Id);
        var waMsgsQ = db.MessagesWhatsapp.AsNoTracking()
            .Where(m => m.CreatedAt >= from && waContactIds.Contains(m.ContactId));
        var waSent     = await waMsgsQ.CountAsync(m => m.Direction == "outgoing");
        var waReceived = await waMsgsQ.CountAsync(m => m.Direction == "incoming");

        // ── Email ─────────────────────────────────────────────────────────────────
        var emailsSent = await db.EmailLogs.AsNoTracking()
            .CountAsync(e => e.SentAt >= from && e.AccountId != null && accountIds.Contains(e.AccountId!.Value));

        // ── Oportunidades ─────────────────────────────────────────────────────────
        var dealsQ = db.Deals.AsNoTracking().Where(d => accountIds.Contains(d.AccountId));
        var dealsWon    = await dealsQ.CountAsync(d => d.Status == "Ganado" && d.CloseDate >= from);
        var dealsLost   = await dealsQ.CountAsync(d => d.Status == "Perdido" && d.CloseDate >= from);
        var dealsWonAmt = await dealsQ.Where(d => d.Status == "Ganado" && d.CloseDate >= from)
            .SumAsync(d => (decimal?)d.QuotedAmount) ?? 0m;
        var totalClosed = dealsWon + dealsLost;
        var winRate = totalClosed > 0 ? Math.Round(dealsWon * 100.0 / totalClosed, 1) : 0;

        // ── Desglose por cliente (top 8 por actividad de IA — para ver quién usa qué) ──
        var scoredByAccount = leadsInRange
            .Where(l => l.ScoredAt != null && (l.ScoreSource == "AI" || l.ScoreSource == "Hybrid"))
            .GroupBy(l => l.AccountId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var byCustomer = accounts
            .GroupBy(a => new { a.CustomerId, a.CustomerName })
            .Select(g => new
            {
                customerId    = g.Key.CustomerId,
                customerName  = g.Key.CustomerName,
                leadsScoredAi = g.Sum(a => scoredByAccount.GetValueOrDefault(a.AccountId)),
                aiQuantRuns   = g.Sum(a => aiQuantByAccount.GetValueOrDefault(a.AccountId)),
            })
            .Where(x => x.leadsScoredAi + x.aiQuantRuns > 0)
            .OrderByDescending(x => x.leadsScoredAi + x.aiQuantRuns)
            .Take(8)
            .ToList();

        return Ok(new
        {
            period = new { days, from, to = now },
            aiScoring = new { leadsScoredAi, leadsScoredManual, leadsCreated,
                pct = leadsCreated > 0 ? Math.Round(leadsScoredAi * 100.0 / leadsCreated, 1) : 0 },
            aiQuant = new { runs = aiQuantRuns, costUsd = Math.Round(aiQuantCost, 2), avgScore = Math.Round(aiQuantAvgScore, 1) },
            sequences = new { tasksGenerated = seqTasksGenerated, tasksCompleted = seqTasksCompleted, leadsReached = seqLeadsReached },
            whatsapp = new { messagesSent = waSent, messagesReceived = waReceived },
            email = new { sent = emailsSent },
            deals = new { won = dealsWon, lost = dealsLost, wonAmount = dealsWonAmt, winRatePct = winRate },
            attention = new { needingHelp = needingAttention },
            byCustomer,
        });
    }
}
