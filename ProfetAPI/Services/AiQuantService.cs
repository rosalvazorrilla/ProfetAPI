using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;

namespace ProfetAPI.Services;

/// <summary>
/// "AI QUANT — Lead Score": investiga la EMPRESA de un prospecto con búsqueda web pública
/// (sitio, redes, noticias, vacantes, directorios) + el contexto que aportó el vendedor
/// tras el First Contact Call, y produce una ficha estructurada (presencia digital, perfil
/// del negocio, score cualitativo 0-100, estrategia comercial, fuentes). Distinto del BANT:
/// aquí la IA SÍ calcula el score, es un juicio cualitativo. Se ejecuta desde un job en
/// background sobre una fila AiQuantRun previamente creada en estado "Pending".
/// </summary>
public interface IAiQuantService
{
    Task ExecuteAsync(int runId, CancellationToken ct = default);
    bool IsConfigured { get; }
}

public class AiQuantService(
    ApplicationDbContext db,
    IAiClient ai,
    ITimelineLogger timeline,
    INotificationService notif,
    IConfiguration cfg,
    ILogger<AiQuantService> logger) : IAiQuantService
{
    private const int MaxSearches = 8;

    public bool IsConfigured => ai.IsConfigured;

    public async Task ExecuteAsync(int runId, CancellationToken ct = default)
    {
        var run = await db.AiQuantRuns.FirstOrDefaultAsync(r => r.RunId == runId, ct);
        if (run == null || run.Status is "Done" or "Failed") return;

        run.Status = "Running";
        await db.SaveChangesAsync(ct);

        var sw = Stopwatch.StartNew();
        try
        {
            var lead = await db.Leads.AsNoTracking()
                .Where(l => l.LeadId == run.LeadId)
                .Select(l => new
                {
                    l.Name, l.Email, l.Phone, l.Company, l.Position, l.City,
                    l.ProspectSource, l.AdName, l.InitialMessage, l.ContactId, l.OwnerUserId,
                })
                .FirstOrDefaultAsync(ct);
            if (lead == null) { await FailAsync(run, "Prospecto no encontrado.", sw, ct); return; }

            // Notas de timeline tipo "note" — el input libre que el vendedor deja tras la llamada.
            var notes = await db.TimelineEvents.AsNoTracking()
                .Where(e => e.EntityType == "Lead" && e.EntityId == run.LeadId && e.Type == "note" && !e.Deleted)
                .OrderByDescending(e => e.CreatedOn).Take(10)
                .Select(e => new { e.CreatedOn, e.Detail })
                .ToListAsync(ct);

            // WhatsApp del contacto ligado (mismo criterio que ScoringAiService).
            var waMessages = new List<string>();
            if (lead.ContactId.HasValue)
            {
                waMessages = await db.MessagesWhatsapp.AsNoTracking()
                    .Where(m => m.Contact.LeadId == run.LeadId && m.MessageText != null)
                    .OrderByDescending(m => m.CreatedAt).Take(15)
                    .Select(m => $"[{(m.Direction == "incoming" ? "prospecto" : "vendedor")}] {m.MessageText}")
                    .ToListAsync(ct);
                waMessages.Reverse();
            }

            var emailDomain = lead.Email is { Length: > 0 } && lead.Email.Contains('@')
                ? lead.Email.Split('@')[^1] : null;

            var sb = new StringBuilder();
            sb.AppendLine("DATOS DEL PROSPECTO");
            sb.AppendLine($"- Contacto: {lead.Name} | Puesto: {lead.Position}");
            sb.AppendLine($"- Empresa (nombre según el CRM): {lead.Company}");
            if (emailDomain != null) sb.AppendLine($"- Dominio del correo (semilla de búsqueda): {emailDomain}");
            sb.AppendLine($"- Ciudad: {lead.City} | Fuente: {lead.ProspectSource} | Campaña: {lead.AdName}");
            if (!string.IsNullOrWhiteSpace(lead.InitialMessage))
                sb.AppendLine($"- Mensaje inicial del prospecto: {lead.InitialMessage}");
            if (!string.IsNullOrWhiteSpace(run.InputWebsite))
                sb.AppendLine($"- Sitio web confirmado por el vendedor: {run.InputWebsite}");
            if (!string.IsNullOrWhiteSpace(run.InputCallNotes))
            {
                sb.AppendLine();
                sb.AppendLine("CONTEXTO DE LA LLAMADA (lo que escribió el vendedor tras el First Contact Call):");
                sb.AppendLine(run.InputCallNotes);
            }
            if (notes.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("NOTAS PREVIAS DEL VENDEDOR (timeline, lo más nuevo primero):");
                foreach (var n in notes) sb.AppendLine($"- {n.CreatedOn:yyyy-MM-dd}: {n.Detail}");
            }
            if (waMessages.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("CONVERSACIÓN DE WHATSAPP (más reciente al final):");
                foreach (var m in waMessages) sb.AppendLine(m);
            }
            sb.AppendLine();
            sb.AppendLine("Investiga la EMPRESA y devuelve la ficha en el formato JSON indicado.");

            var result = await ai.CompleteJsonWithWebSearchAsync(SystemPrompt, sb.ToString(), Schema, MaxSearches, ct);

            // Validación mínima: debe parsear y traer un score 0-100.
            using var parsed = JsonDocument.Parse(result.Json);
            int score = parsed.RootElement.TryGetProperty("quantScore", out var qs) ? Math.Clamp(qs.GetInt32(), 0, 100) : 0;
            string? tier = parsed.RootElement.TryGetProperty("tier", out var tr) ? tr.GetString() : null;

            run.ResultJson     = result.Json;
            run.Score          = score;
            run.Tier           = tier;
            run.InputTokens    = result.InputTokens;
            run.OutputTokens   = result.OutputTokens;
            run.WebSearchCount = result.WebSearchCount;
            run.CostUsd        = ComputeCost(result.InputTokens, result.OutputTokens, result.WebSearchCount);
            run.DurationMs     = (int)sw.ElapsedMilliseconds;
            run.Status         = "Done";
            run.CompletedOn    = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            await timeline.LogAsync(run.AccountId, "Lead", run.LeadId, "note",
                $"AI QUANT: {tier ?? "sin tier"} · {score} pts",
                detail: parsed.RootElement.TryGetProperty("summary", out var su) ? su.GetString() : null,
                userId: run.RunByUserId);

            if (!string.IsNullOrWhiteSpace(lead.OwnerUserId))
                await notif.NotifyAsync(lead.OwnerUserId!,
                    $"AI QUANT listo para {lead.Name}: {tier ?? "sin tier"} · {score} pts",
                    url: $"/prospectos?id={run.LeadId}", entityType: "Lead", entityId: run.LeadId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falló la corrida AI QUANT {RunId}", runId);
            await FailAsync(run, ex is HttpRequestException ? "La IA no pudo completar la investigación — intenta de nuevo." : ex.Message, sw, ct);
        }
    }

    private async Task FailAsync(Models.AiQuantRun run, string error, Stopwatch sw, CancellationToken ct)
    {
        run.Status      = "Failed";
        run.Error       = error;
        run.DurationMs  = (int)sw.ElapsedMilliseconds;
        run.CompletedOn = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private decimal ComputeCost(int inTok, int outTok, int searches)
    {
        decimal pIn     = cfg.GetValue("Anthropic:PriceInputPerMTokUsd",  3.0m);
        decimal pOut    = cfg.GetValue("Anthropic:PriceOutputPerMTokUsd", 15.0m);
        decimal pSearch = cfg.GetValue("Anthropic:PriceWebSearchPerKUsd", 10.0m);
        return Math.Round(inTok / 1_000_000m * pIn + outTok / 1_000_000m * pOut + searches / 1_000m * pSearch, 4);
    }

    private const string SystemPrompt = """
        Eres un analista comercial B2B. Tu trabajo es investigar la EMPRESA de un prospecto
        usando ÚNICAMENTE información pública de negocio (su sitio web, redes sociales públicas,
        noticias y medios, vacantes publicadas, directorios de empresas). De la persona solo
        considera su rol/seniority público — NO investigues datos personales.

        Reglas:
        - NO das asesoría legal ni crediticia. Si encuentras que la empresa aparece en la lista
          SAT 69-B (EFOS/EDOS) o notas negativas en prensa, repórtalo como bandera de riesgo con
          su fuente, sin opinar legalmente.
        - Todo lo que sea estimación (tamaño, valor del negocio) márcalo claramente como estimado.
        - CITA cada afirmación relevante con su URL en "sources". Si no encuentras algo, dilo — no inventes.
        - Usa la búsqueda web. Empieza por el dominio del correo y el nombre de la empresa.
        - Responde en español.

        El "quantScore" (0-100) es tu juicio cualitativo del atractivo comercial de este
        prospecto (tamaño del negocio, seriedad/legitimidad, relevancia en su sector, encaje).
        El "tier" se deriva del score: 0-39 "Frío", 40-64 "Tibio", 65-84 "Caliente", 85-100 "Prioritario".
        En "strategy" da recomendaciones concretas y accionables para el vendedor.
        """;

    private const string Schema = """
        {
          "type":"object","additionalProperties":false,
          "required":["digitalPresence","businessProfile","quantScore","tier","scoreBreakdown","dealSizeEstimate","strategy","sources","summary"],
          "properties":{
            "digitalPresence":{"type":"object","additionalProperties":false,
              "required":["hasWebsite","websiteUrl","websiteQuality","socialNetworks","salesSignal"],
              "properties":{
                "hasWebsite":{"type":"boolean"},
                "websiteUrl":{"type":["string","null"]},
                "websiteQuality":{"type":"string","enum":["profesional","basica","landing","ninguna"]},
                "socialNetworks":{"type":"array","items":{"type":"object","additionalProperties":false,
                  "required":["network","url","activity"],
                  "properties":{"network":{"type":"string"},"url":{"type":["string","null"]},
                    "activity":{"type":"string","enum":["activa","abandonada","nula"]}}}},
                "salesSignal":{"type":"string"}}},
            "businessProfile":{"type":"object","additionalProperties":false,
              "required":["sizeEstimate","sizeReasoning","industry","sectorRelevance","riskFlags"],
              "properties":{
                "sizeEstimate":{"type":"string","enum":["micro","chica","mediana","grande"]},
                "sizeReasoning":{"type":"string"},
                "industry":{"type":["string","null"]},
                "sectorRelevance":{"type":"string","enum":["lider","competidor","nicho","desconocido"]},
                "riskFlags":{"type":"array","items":{"type":"string"}}}},
            "quantScore":{"type":"integer"},
            "tier":{"type":"string","enum":["Frío","Tibio","Caliente","Prioritario"]},
            "scoreBreakdown":{"type":"array","items":{"type":"object","additionalProperties":false,
              "required":["dimension","points","note"],
              "properties":{"dimension":{"type":"string"},"points":{"type":"integer"},"note":{"type":"string"}}}},
            "dealSizeEstimate":{"type":"string"},
            "strategy":{"type":"object","additionalProperties":false,
              "required":["howToReach","firstApproachAngle","commercialTips","alerts","doNotDo"],
              "properties":{
                "howToReach":{"type":"string"},
                "firstApproachAngle":{"type":"string"},
                "commercialTips":{"type":"array","items":{"type":"string"}},
                "alerts":{"type":"array","items":{"type":"string"}},
                "doNotDo":{"type":"array","items":{"type":"string"}}}},
            "sources":{"type":"array","items":{"type":"object","additionalProperties":false,
              "required":["title","url"],"properties":{"title":{"type":"string"},"url":{"type":"string"}}}},
            "summary":{"type":"string"}
          }
        }
        """;
}
