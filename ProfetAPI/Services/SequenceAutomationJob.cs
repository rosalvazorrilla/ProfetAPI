using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Models;

namespace ProfetAPI.Services;

/// <summary>
/// Corre una vez al día: revisa, en TODAS las cuentas, las tareas de secuencia vencidas
/// cuyo paso está en modo "Automatico", y las manda una por una con una pequeña espera
/// entre cada una — así se manda "poco a poco" en vez de todas de golpe. No existía
/// ningún job en segundo plano en el proyecto antes de esto.
/// </summary>
public class SequenceAutomationJob(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    ILogger<SequenceAutomationJob> logger) : BackgroundService
{
    private static readonly TimeSpan ThrottleBetweenSends = TimeSpan.FromSeconds(5);

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

            var delay = TimeUntilNextRun();
            logger.LogInformation("Despachador de secuencias: próxima corrida en {Delay}", delay);
            try { await Task.Delay(delay, stoppingToken); }
            catch (TaskCanceledException) { }
        }
    }

    private TimeSpan TimeUntilNextRun()
    {
        var hour = int.TryParse(config["Sequences:DispatchHourUtc"], out var h) ? h : 14; // ~8am hora CDMX
        var now  = DateTime.UtcNow;
        var next = new DateTime(now.Year, now.Month, now.Day, hour, 0, 0, DateTimeKind.Utc);
        if (next <= now) next = next.AddDays(1);
        return next - now;
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db        = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var dispatch  = scope.ServiceProvider.GetRequiredService<ISequenceDispatchService>();

        var now = DateTime.UtcNow;
        var openStatuses = new[] { "Pendiente", "En progreso" };

        var dueTasks = await db.Activities
            .Where(a => a.ActivityType == "Task" && openStatuses.Contains(a.TaskStatus)
                     && a.DueDate != null && a.DueDate <= now
                     && a.SourcePlaybookTaskId != null)
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

        var sent = 0;
        foreach (var task in dueTasks)
        {
            if (ct.IsCancellationRequested) break;
            if (task.SourcePlaybookTaskId == null || !steps.TryGetValue(task.SourcePlaybookTaskId.Value, out var step)) continue;
            if (task.EntityType == "Lead" && task.EntityId.HasValue && pausedLeads.Contains(task.EntityId.Value)) continue;
            if (task.EntityType == "Deal" && task.EntityId.HasValue && pausedDeals.Contains((int)task.EntityId.Value)) continue;

            var ok = await dispatch.SendTaskMessageAsync(task, step);
            if (ok)
            {
                sent++;
                await Task.Delay(ThrottleBetweenSends, ct);
            }
        }

        logger.LogInformation("Despachador de secuencias: {Sent} de {Total} tareas enviadas", sent, dueTasks.Count);
    }
}
