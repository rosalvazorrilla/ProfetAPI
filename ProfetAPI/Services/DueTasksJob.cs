using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;

namespace ProfetAPI.Services;

/// <summary>
/// Vigila las fechas límite de las tareas y avisa al responsable, una sola vez por cada estado:
///  • "por vencer": la tarea vence en las próximas 24 horas;
///  • "vencida": la fecha límite ya pasó (solo si venció en los últimos 7 días, para no inundar con tareas viejas).
/// La marca de "ya avisé" vive en la propia tarea (DueSoonNotifiedOn / OverdueNotifiedOn): consultar la tabla de
/// notificaciones para deduplicar sería recorrer más de un millón de filas sin índice.
/// </summary>
public class DueTasksJob(IServiceScopeFactory scopeFactory, ILogger<DueTasksJob> logger) : BackgroundService
{
    private static readonly TimeSpan RunInterval = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan Warmup = TimeSpan.FromMinutes(2);
    private const int MaxPerRun = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(Warmup, stoppingToken); } catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "Error en el aviso de vencimientos"); }

            try { await Task.Delay(RunInterval, stoppingToken); }
            catch (TaskCanceledException) { }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var alerts = scope.ServiceProvider.GetRequiredService<IAlertService>();
        db.Database.SetCommandTimeout(90);

        var now = DateTime.UtcNow;
        var soonLimit = now.AddHours(24);
        var overdueFloor = now.AddDays(-7);

        var tasks = await db.Activities
            .Where(a => a.DueDate != null && a.IsCompleted != true
                && a.TaskStatus != "Completada" && a.TaskStatus != "Cancelada"
                && a.DueDate >= overdueFloor && a.DueDate <= soonLimit
                && ((a.DueDate > now && a.DueSoonNotifiedOn == null) || (a.DueDate <= now && a.OverdueNotifiedOn == null)))
            .OrderBy(a => a.DueDate)
            .Take(MaxPerRun)
            .ToListAsync(ct);

        int sent = 0;
        foreach (var t in tasks)
        {
            var recipient = t.AssignedToUserId ?? t.OwnerUserId;
            var overdue = t.DueDate <= now;

            if (!string.IsNullOrEmpty(recipient))
            {
                var subject = string.IsNullOrWhiteSpace(t.Subject) ? "Tarea" : t.Subject!;
                var message = overdue ? $"Tarea vencida: {subject}" : $"Tarea por vencer en menos de 24 h: {subject}";
                var url = t.EntityType switch
                {
                    "Lead" when t.EntityId.HasValue => $"/prospectos?id={t.EntityId}",
                    "Deal" when t.EntityId.HasValue => $"/oportunidades?id={t.EntityId}",
                    _ => "/canales/tareas",
                };
                await alerts.SendAsync(recipient, AlertType.TaskDue, message, url, entityType: t.EntityType, entityId: t.EntityId);
                sent++;
            }

            // Se marca aunque no haya responsable, para no reintentarla cada hora.
            if (overdue) t.OverdueNotifiedOn = now; else t.DueSoonNotifiedOn = now;
        }

        if (tasks.Count > 0) await db.SaveChangesAsync(ct);
        if (tasks.Count > 0) logger.LogInformation("Aviso de vencimientos: {Sent} avisos de {Total} tareas", sent, tasks.Count);
    }
}
