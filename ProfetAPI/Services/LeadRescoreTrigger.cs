using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;

namespace ProfetAPI.Services;

/// <summary>
/// F4-T4: dispara una recalificación IA en segundo plano cuando llega información nueva
/// a un lead (nota agregada, mensaje entrante, etc.), con un cooldown para no recalificar
/// en bucle ni saturar la IA por eventos seguidos del mismo lead.
/// </summary>
public interface ILeadRescoreTrigger
{
    void MaybeRescore(long leadId);
}

public class LeadRescoreTrigger(IServiceScopeFactory scopeFactory, ILogger<LeadRescoreTrigger> logger) : ILeadRescoreTrigger
{
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(10);

    public void MaybeRescore(long leadId)
    {
        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var lead = await db.Leads.AsNoTracking()
                .Where(l => l.LeadId == leadId)
                .Select(l => new { l.ScoredAt })
                .FirstOrDefaultAsync();
            if (lead == null) return;
            if (lead.ScoredAt != null && DateTime.UtcNow - lead.ScoredAt.Value < Cooldown) return;

            var scoringAi = scope.ServiceProvider.GetRequiredService<IScoringAiService>();
            if (!scoringAi.IsConfigured) return;

            try { await scoringAi.ScoreAndPersistAsync(leadId); }
            catch (Exception ex) { logger.LogWarning(ex, "Re-scoring automático falló para lead {LeadId}", leadId); }
        });
    }
}
