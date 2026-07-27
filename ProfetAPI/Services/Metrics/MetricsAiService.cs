using System.Text.Json;
using ProfetAPI.Dtos.Metrics;

namespace ProfetAPI.Services.Metrics;

/// <summary>
/// IA analítica segura: Claude SOLO elige combinaciones válidas del catálogo (whitelist).
/// Nunca genera SQL. Cada salida se valida server-side contra MetricsCatalog antes de ejecutarse.
/// </summary>
public class MetricsAiService(MetricsCatalog catalog, MetricsQueryService engine, Services.IAiClient ai, ILogger<MetricsAiService> logger)
{
    public record Suggestion(MetricQueryDto Query, string Title, string Reason);
    public record AskResult(string Answer, List<MetricQueryDto> Queries, List<MetricSeriesDto> Series);

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

    // ── D6: pregúntale a tus datos (Q&A en lenguaje natural) ───────────────────
    public async Task<AskResult?> AskDataAsync(int accountId, string question, string locale, CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var system = "Eres un analista de datos de un CRM. El usuario pregunta algo en lenguaje natural sobre sus métricas. "
                   + $"Hoy es {today}. Traduce la pregunta a entre 1 y 3 consultas válidas del catálogo (usa chartType=\"kpi\" salvo que la pregunta pida explícitamente un desglose). "
                   + "Resuelve fechas relativas (\"el mes pasado\", \"este trimestre\") a fechas absolutas yyyy-MM-dd en from/to. "
                   + "Si la pregunta no se puede responder SOLO con el catálogo, devuelve \"queries\":[]. Nunca inventes measure/dimension fuera del catálogo.\n\n"
                   + CatalogText();
        var schema = """
{"type":"object","additionalProperties":false,"required":["queries"],"properties":{"queries":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["measure","dimension","chartType","from","to"],"properties":{"measure":{"type":"string"},"dimension":{"type":["string","null"]},"chartType":{"type":"string"},"from":{"type":["string","null"]},"to":{"type":["string","null"]}}}}}}
""";

        List<MetricQueryDto> queries;
        try
        {
            var planJson = await ai.CompleteJsonAsync(system, question, schema, ct);
            using var doc = JsonDocument.Parse(planJson);
            queries = new List<MetricQueryDto>();
            if (doc.RootElement.TryGetProperty("queries", out var arr))
            {
                foreach (var el in arr.EnumerateArray())
                {
                    var measure = el.TryGetProperty("measure", out var m) ? m.GetString() ?? "" : "";
                    if (string.IsNullOrWhiteSpace(measure)) continue;
                    var dimension = el.TryGetProperty("dimension", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                    var chartType = el.TryGetProperty("chartType", out var c) ? c.GetString() ?? "kpi" : "kpi";
                    DateTime? from = el.TryGetProperty("from", out var f) && f.ValueKind == JsonValueKind.String
                        && DateTime.TryParse(f.GetString(), out var fd) ? fd : null;
                    DateTime? to = el.TryGetProperty("to", out var t) && t.ValueKind == JsonValueKind.String
                        && DateTime.TryParse(t.GetString(), out var td) ? td : null;

                    var q = new MetricQueryDto { Measure = measure, Dimension = dimension, ChartType = chartType, From = from, To = to };
                    if (MetricsCatalog.IsValid(q, out _)) queries.Add(q); // anti-alucinación
                }
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "No se pudo interpretar la pregunta para ask-data"); return null; }

        if (queries.Count == 0) return null;

        var series = new List<MetricSeriesDto>();
        foreach (var q in queries)
            series.Add(await engine.RunAsync(q, accountId));

        var facts = string.Join("\n", queries.Zip(series, (q, s) =>
            $"- {q.Measure}{(q.Dimension != null ? $" por {q.Dimension}" : "")}: total={s.Total}"
            + (s.Labels.Count > 0 ? $", detalle=[{string.Join(", ", s.Labels.Zip(s.Values, (l, v) => $"{l}={v}"))}]" : "")));

        var phraseLang = locale == "en" ? "English" : "Spanish";
        var phraseSystem = $"You are a CRM data analyst. Answer the user's question in one or two short sentences, in {phraseLang}, "
                          + "using ONLY the numbers provided below. Do not add caveats about data limitations unless the numbers are zero/empty. Be direct and specific.";
        var phrasePrompt = $"Question: {question}\n\nData:\n{facts}";
        string answer;
        try { answer = (await ai.CompleteTextAsync(phraseSystem, phrasePrompt, ct)).Trim(); }
        catch (Exception ex) { logger.LogWarning(ex, "No se pudo redactar la respuesta de ask-data"); answer = facts; }

        return new AskResult(answer, queries, series);
    }

    // ── D5: resumen narrativo del período ──────────────────────────────────────
    public async Task<string?> NarrativeSummaryAsync(int accountId, int days, string locale, CancellationToken ct = default)
    {
        var now      = DateTime.UtcNow;
        var curFrom  = now.AddDays(-days);
        var prevFrom = curFrom.AddDays(-days);

        var measureKeys = new[] { "leads_count", "deals_won", "deals_amount", "win_rate", "avg_score" };
        var facts = new List<string>();

        foreach (var key in measureKeys)
        {
            var meta = MetricsCatalog.Measures.FirstOrDefault(m => m.Key == key);
            if (meta == null) continue;

            var curQ  = new MetricQueryDto { Measure = key, ChartType = "kpi", From = curFrom, To = now };
            var prevQ = new MetricQueryDto { Measure = key, ChartType = "kpi", From = prevFrom, To = curFrom };
            if (!MetricsCatalog.IsValid(curQ, out _)) continue;

            var cur  = await engine.RunAsync(curQ, accountId);
            var prev = await engine.RunAsync(prevQ, accountId);
            facts.Add($"- {meta.Label}: actual={cur.Total} ({meta.Format}), período anterior={prev.Total}");
        }

        if (facts.Count == 0) return null;

        var lang = locale == "en" ? "English" : "Spanish";
        var system = $"You are a CRM data analyst. Write a short narrative summary (3 to 5 sentences, no markdown, no headers) "
                    + $"in {lang} of what happened in the last {days} days versus the previous period of the same length, "
                    + "using ONLY the numbers given below. Call out the most notable change (best or worst) directly. Be concrete, no filler.";
        var prompt = string.Join("\n", facts);

        try { return (await ai.CompleteTextAsync(system, prompt, ct)).Trim(); }
        catch (Exception ex) { logger.LogWarning(ex, "No se pudo generar el resumen narrativo de la cuenta {AccountId}", accountId); return null; }
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
