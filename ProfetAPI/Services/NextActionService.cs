using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ProfetAPI.Data;

namespace ProfetAPI.Services;

public record NextActionDto(bool Available, string Summary, string Action, string Priority, string Reason);

/// <summary>
/// P3: "Próxima mejor acción". Junta el contexto del lead (datos, score/tier, timeline reciente)
/// y le pide a Claude un resumen + una acción concreta. Cachea por lead para controlar costo.
/// </summary>
public interface INextActionService
{
    Task<NextActionDto> GetForLeadAsync(long leadId, CancellationToken ct = default);
    bool IsConfigured { get; }
}

public class NextActionService(ApplicationDbContext db, IAiClient ai, IMemoryCache cache, ILogger<NextActionService> logger)
    : INextActionService
{
    public bool IsConfigured => ai.IsConfigured;

    public async Task<NextActionDto> GetForLeadAsync(long leadId, CancellationToken ct = default)
    {
        if (!ai.IsConfigured) return new(false, "", "", "", "");
        if (cache.TryGetValue($"nextaction:lead:{leadId}", out NextActionDto? cached) && cached != null)
            return cached;

        var lead = await db.Leads.AsNoTracking()
            .Where(l => l.LeadId == leadId && l.Deleted != true)
            .Select(l => new
            {
                l.Name, l.Email, l.Phone, l.Company, l.Position, l.Status, l.ProspectSource,
                l.Score, l.ScoreReasoning, l.CreatedOn,
                TierName = l.Tier != null ? l.Tier.Name : null,
            })
            .FirstOrDefaultAsync(ct);
        if (lead == null) return new(false, "", "", "", "");

        var events = await db.TimelineEvents.AsNoTracking()
            .Where(e => e.EntityType == "Lead" && e.EntityId == leadId && !e.Deleted)
            .OrderByDescending(e => e.CreatedOn).Take(8)
            .Select(e => new { e.Type, e.Title, e.CreatedOn })
            .ToListAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine($"Prospecto: {lead.Name} | Empresa: {lead.Company} | Puesto: {lead.Position}");
        sb.AppendLine($"Estatus: {lead.Status} | Fuente: {lead.ProspectSource}");
        sb.AppendLine($"Calificación: {(lead.TierName ?? "sin calificar")} ({lead.Score?.ToString() ?? "?"} pts)");
        if (!string.IsNullOrWhiteSpace(lead.ScoreReasoning)) sb.AppendLine($"Razón del score: {lead.ScoreReasoning}");
        sb.AppendLine($"Creado: {lead.CreatedOn:yyyy-MM-dd}");
        sb.AppendLine("Actividad reciente (lo más nuevo primero):");
        if (events.Count == 0) sb.AppendLine("  (sin actividad registrada)");
        foreach (var e in events) sb.AppendLine($"  - {e.CreatedOn:yyyy-MM-dd} [{e.Type}] {e.Title}");

        const string system = """
Eres coach de ventas. Con el contexto del prospecto, devuelve:
- Summary: 1-2 frases de quién es y en qué punto está.
- Action: UNA acción concreta y accionable para HOY (ej. "Llamar hoy para confirmar presupuesto").
- Priority: "Alta" | "Media" | "Baja".
- Reason: por qué esa acción ahora.
Responde en español, directo, sin relleno.
""";
        var schema = """
{"type":"object","additionalProperties":false,"required":["Summary","Action","Priority","Reason"],"properties":{"Summary":{"type":"string"},"Action":{"type":"string"},"Priority":{"type":"string"},"Reason":{"type":"string"}}}
""";

        try
        {
            var json = await ai.CompleteJsonAsync(system, sb.ToString(), schema, ct);
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var parsed = JsonSerializer.Deserialize<NextActionDto>(json, opts);
            var result = parsed == null
                ? new NextActionDto(false, "", "", "", "")
                : new NextActionDto(true, parsed.Summary, parsed.Action, parsed.Priority, parsed.Reason);

            cache.Set($"nextaction:lead:{leadId}", result, TimeSpan.FromMinutes(10));
            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo generar próxima acción para lead {LeadId}", leadId);
            return new(false, "", "", "", "");
        }
    }
}
