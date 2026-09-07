using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Models;

namespace ProfetAPI.Services;

/// <summary>
/// Corre cada hora: revisa, en TODAS las cuentas, las tareas de secuencia vencidas cuyo
/// paso está en modo "Automatico", y las manda una por una con una pequeña espera entre
/// cada una — así se manda "poco a poco" en vez de todas de golpe. Respeta horario hábil
/// local por cliente (zona horaria de Customer.TimeZoneId, default México) — no manda un
/// WhatsApp a las 3am solo porque en UTC ya "tocaba". No existía ningún job en segundo
/// plano en el proyecto antes de esto.
/// </summary>
public class SequenceAutomationJob(
    IServiceScopeFactory scopeFactory,
    ILogger<SequenceAutomationJob> logger) : BackgroundService
{
    private static readonly TimeSpan RunInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan ThrottleBetweenSends = TimeSpan.FromSeconds(5);
    private const string DefaultTimeZoneId = "America/Mexico_City";
    private const int BusinessHourStart = 9;   // 9am hora local
    private const int BusinessHourEnd   = 19;  // 7pm hora local

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error en la corrida del despachador de secuencias");
            }

            try { await Task.Delay(RunInterval, stoppingToken); }
            catch (TaskCanceledException) { }
        }
    }

    /// <summary>Hora local del cliente ahora mismo, según su zona horaria configurada
    /// (o México por default) — con fallback silencioso si el id de zona no es válido.</summary>
    private static int LocalHourNow(string? timeZoneId)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(timeZoneId) ? DefaultTimeZoneId : timeZoneId);
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Hour;
        }
        catch
        {
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
                TimeZoneInfo.FindSystemTimeZoneById(DefaultTimeZoneId)).Hour;
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db       = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var dispatch = scope.ServiceProvider.GetRequiredService<ISequenceDispatchService>();

        var now = DateTime.UtcNow;
        var openStatuses = new[] { "Pendiente", "En progreso" };

        var dueTasks = await db.Activities
            .Where(a => a.ActivityType == "Task" && openStatuses.Contains(a.TaskStatus)
                     && a.DueDate != null && a.DueDate <= now
                     && a.SourcePlaybookTaskId != null
                     && a.AutomationFailCount < SequenceDispatchService.MaxAutomationFailures)
            .ToListAsync(ct);

        if (dueTasks.Count == 0) return;

        var stepIds = dueTasks.Select(t => t.SourcePlaybookTaskId!.Value).Distinct().ToList();
        var steps = await db.PlaybookTasks
            .Where(s => stepIds.Contains(s.TaskId) && s.AutomationMode == "Automatico")
            .ToDictionaryAsync(s => s.TaskId, ct);

        var leadIds = dueTasks.Where(t => t.EntityType == "Lead" && t.EntityId.HasValue).Select(t => t.EntityId!.Value).Distinct().ToList();
        var dealIds = dueTasks.Where(t => t.EntityType == "Deal" && t.EntityId.HasValue).Select(t => (int)t.EntityId!.Value).Distinct().ToList();
        var pausedLeads = (await db.Leads.Where(l => leadIds.Contains(l.LeadId) && l.SequencePaused).Select(l => l.LeadId).ToListAsync(ct)).ToHashSet();
        var pausedDeals = (await db.Deals.Where(d => dealIds.Contains(d.DealId) && d.SequencePaused).Select(d => d.DealId).ToListAsync(ct)).ToHashSet();

        // AccountId -> zona horaria del Customer dueño de esa cuenta (una sola consulta).
        var accountIds = dueTasks.Where(t => t.AccountId.HasValue).Select(t => t.AccountId!.Value).Distinct().ToList();
        var accountTimeZones = await db.Accounts.AsNoTracking()
            .Where(a => accountIds.Contains(a.AccountId))
            .Select(a => new { a.AccountId, TimeZoneId = a.Customer.TimeZoneId })
            .ToDictionaryAsync(a => a.AccountId, a => a.TimeZoneId, ct);

        var sent = 0;
        var skippedOffHours = 0;
        foreach (var task in dueTasks)
        {
            if (ct.IsCancellationRequested) break;
            if (task.SourcePlaybookTaskId == null || !steps.TryGetValue(task.SourcePlaybookTaskId.Value, out var step)) continue;
            if (task.EntityType == "Lead" && task.EntityId.HasValue && pausedLeads.Contains(task.EntityId.Value)) continue;
            if (task.EntityType == "Deal" && task.EntityId.HasValue && pausedDeals.Contains((int)task.EntityId.Value)) continue;

            var tzId = task.AccountId.HasValue && accountTimeZones.TryGetValue(task.AccountId.Value, out var tz) ? tz : null;
            var localHour = LocalHourNow(tzId);
            if (localHour < BusinessHourStart || localHour >= BusinessHourEnd) { skippedOffHours++; continue; }

            var ok = await dispatch.SendTaskMessageAsync(task, step);
            if (ok)
            {
                sent++;
                await Task.Delay(ThrottleBetweenSends, ct);
            }
        }

        logger.LogInformation("Despachador de secuencias: {Sent} enviadas, {Skipped} fuera de horario local, de {Total} vencidas",
            sent, skippedOffHours, dueTasks.Count);
    }
}
