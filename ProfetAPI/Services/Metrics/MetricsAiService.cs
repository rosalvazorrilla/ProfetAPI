using System.Text.Json;
using ProfetAPI.Dtos.Metrics;

namespace ProfetAPI.Services.Metrics;

/// <summary>
/// IA analítica segura: Claude SOLO elige combinaciones válidas del catálogo (whitelist).
/// Nunca genera SQL. Cada salida se valida server-side contra MetricsCatalog antes de ejecutarse.
/// </summary>
public class MetricsAiService(MetricsCatalog catalog, Services.IAiClient ai, ILogger<MetricsAiService> logger)
{
    public record Suggestion(MetricQueryDto Query, string Title, string Reason);

    public bool IsConfigured => ai.IsConfigured;

    private string CatalogText()
    {
        var measures = string.Join("\n", MetricsCatalog.Measures.Select(m =>
            $"- {m.Key} ({m.Label}) dims:[{string.Join(",", m.SupportedDimensions)}]"));
        var charts = string.Join(", ", MetricsCatalog.ChartTypes.Select(c => c.Key));
        return $"MEDIDAS:\n{measures}\nTIPOS DE GRÁFICA: {charts}\nREGLA: dimension debe estar en la lista de la medida; para kpi usa dimension=null.";
    }

    // ── D2-T3: sugerencias ────────────────────────────────────────────────────
    public async Task<List<Suggestion>> SuggestAsync(int accountId, CancellationToken ct = default)
    {
        var system = "Eres analista de datos de un CRM. Propón entre 6 y 8 gráficas ÚTILES para el negocio usando SOLO el catálogo. "
                   + "Cada gráfica: measure, dimension (o null si kpi), chartType, un Title corto y un Reason (por qué es útil). Responde en español.\n\n" + CatalogText();
        var schema = """
{"type":"object","additionalProperties":false,"required":["items"],"properties":{"items":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["measure","dimension","chartType","title","reason"],"properties":{"measure":{"type":"string"},"dimension":{"type":["string","null"]},"chartType":{"type":"string"},"title":{"type":"string"},"reason":{"type":"string"}}}}}}
""";
        var json = await ai.CompleteJsonAsync(system, "Propón las gráficas más útiles para arrancar.", schema, ct);
        return ParseSuggestions(json);
    }

    // ── D2-T4: texto → gráfica ────────────────────────────────────────────────
    public async Task<Suggestion?> FromPromptAsync(int accountId, string prompt, CancellationToken ct = default)
    {
        var system = "Traduce la petición del usuario a UNA gráfica válida del catálogo. Si pide algo fuera del catálogo, devuelve measure vacío. Responde en español.\n\n" + CatalogText();
        var schema = """
{"type":"object","additionalProperties":false,"required":["measure","dimension","chartType","title","reason"],"properties":{"measure":{"type":"string"},"dimension":{"type":["string","null"]},"chartType":{"type":"string"},"title":{"type":"string"},"reason":{"type":"string"}}}
""";
        var json = await ai.CompleteJsonAsync(system, prompt, schema, ct);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var s = ToSuggestion(doc.RootElement);
            return s;
        }
        catch { return null; }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private List<Suggestion> ParseSuggestions(string json)
    {
        var list = new List<Suggestion>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("items", out var items)) return list;
            foreach (var el in items.EnumerateArray())
            {
                var s = ToSuggestion(el);
                if (s != null) list.Add(s);
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "No se pudieron parsear sugerencias de métricas"); }
        return list;
    }

    /// <summary>Convierte un elemento JSON en Suggestion, validándolo contra el catálogo (descarta inválidos).</summary>
    private static Suggestion? ToSuggestion(JsonElement el)
    {
        var measure   = el.TryGetProperty("measure", out var m) ? m.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(measure)) return null;
        var dimension = el.TryGetProperty("dimension", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
        var chartType = el.TryGetProperty("chartType", out var c) ? c.GetString() ?? "bar" : "bar";
        var title     = el.TryGetProperty("title", out var t) ? t.GetString() ?? measure : measure;
        var reason    = el.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";

        var query = new MetricQueryDto { Measure = measure, Dimension = dimension, ChartType = chartType };
        if (!MetricsCatalog.IsValid(query, out _)) return null;   // anti-alucinación
        return new Suggestion(query, title, reason);
    }
}
