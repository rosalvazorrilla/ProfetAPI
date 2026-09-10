using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;

namespace ProfetAPI.Services;

/// <summary>
/// Procesa las corridas de "AI QUANT — Lead Score" en segundo plano: toma las filas
/// AiQuantRun en estado "Pending" y las ejecuta una por una. Se eligió un job durable
/// (no Task.Run) porque cada corrida es cara, debe sobrevivir reinicios y necesita que
/// el ledger de costo quede confiable. Mismo patrón que SequenceAutomationJob.
/// </summary>
public class AiQuantJob(
    IServiceScopeFactory scopeFactory,
    ILogger<AiQuantJob> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StaleAfter   = TimeSpan.FromMinutes(15);
    private const int BatchSize = 3;
    private const string FeatureCode = "AI_QUANT_LEAD_SCORE";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "Error en la corrida del job de AI QUANT"); }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (TaskCanceledException) { }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db          = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Fallar corridas colgadas (proceso reiniciado a media investigación).
        var staleCutoff = DateTime.UtcNow - StaleAfter;
        var stale = await db.AiQuantRuns
            .Where(r => r.Status == "Running" && r.CreatedOn < staleCutoff)
            .ToListAsync(ct);
        foreach (var r in stale)
        {
            r.Status = "Failed";
            r.Error = "La corrida excedió el tiempo máximo.";
            r.CompletedOn = DateTime.UtcNow;
        }
        if (stale.Count > 0) await db.SaveChangesAsync(ct);

        var pending = await db.AiQuantRuns
            .Where(r => r.Status == "Pending")
            .OrderBy(r => r.CreatedOn)
            .Take(BatchSize)
            .Select(r => new { r.RunId, r.CustomerId })
            .ToListAsync(ct);
        if (pending.Count == 0) return;

        var featureGate = scope.ServiceProvider.GetRequiredService<IFeatureGateService>();
        var service     = scope.ServiceProvider.GetRequiredService<IAiQuantService>();

        foreach (var p in pending)
        {
            if (ct.IsCancellationRequested) break;

            // Defensa en profundidad sobre el gate del controlador.
            if (!await featureGate.HasFeatureAsync(p.CustomerId, FeatureCode))
            {
                var run = await db.AiQuantRuns.FirstAsync(r => r.RunId == p.RunId, ct);
                run.Status = "Failed";
                run.Error = "El cliente ya no tiene la función AI QUANT contratada.";
                run.CompletedOn = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                continue;
            }

            await service.ExecuteAsync(p.RunId, ct);
        }
    }
}
