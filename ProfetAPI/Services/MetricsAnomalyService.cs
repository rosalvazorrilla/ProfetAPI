using System.Collections.Concurrent;

namespace ProfetAPI.Services;

/// <summary>
/// D7: detecta caídas o subidas anormales en las métricas del dashboard (vs. período anterior)
/// y notifica a la cuenta. Se evalúa de forma perezosa cada vez que se calculan las stats del
/// dashboard (sin necesidad de un job programado); un dedup en memoria evita notificar varias
/// veces el mismo día por la misma cuenta+métrica.
/// </summary>
public interface IMetricsAnomalyService
{
    void CheckAndNotify(int accountId, string metricKey, string metricLabel, double current, double previous);
}

public class MetricsAnomalyService(IServiceScopeFactory scopeFactory, ILogger<MetricsAnomalyService> logger) : IMetricsAnomalyService
{
    private static readonly ConcurrentDictionary<string, byte> NotifiedToday = new();
    private const double Threshold = 0.30;   // 30% de cambio
    private const double MinBase   = 5;      // ignora ruido en bases muy chicas

    public void CheckAndNotify(int accountId, string metricKey, string metricLabel, double current, double previous)
    {
        if (previous < MinBase) return;

        var change = (current - previous) / previous;
        if (Math.Abs(change) < Threshold) return;

        var cacheKey = $"{accountId}:{metricKey}:{DateTime.UtcNow:yyyy-MM-dd}";
        if (!NotifiedToday.TryAdd(cacheKey, 0)) return; // ya notificado hoy

        var pct       = Math.Round(Math.Abs(change) * 100, 0);
        var isDrop    = change < 0;
        var emoji     = isDrop ? "⚠️" : "📈";
        var direction = isDrop ? "cayeron" : "subieron";
        var message   = $"{emoji} {metricLabel} {direction} {pct}% comparado con el período anterior";

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var notify = scope.ServiceProvider.GetRequiredService<INotificationService>();
            try { await notify.NotifyAccountAsync(accountId, message, url: "/dashboard", entityType: "Anomaly"); }
            catch (Exception ex) { logger.LogWarning(ex, "No se pudo notificar anomalía de {Metric} a la cuenta {AccountId}", metricKey, accountId); }
        });
    }
}
