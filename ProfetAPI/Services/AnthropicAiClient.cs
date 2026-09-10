using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProfetAPI.Services;

/// <summary>
/// Implementación de <see cref="IAiClient"/> contra la Messages API de Anthropic vía HttpClient.
/// Se usa HTTP directo (API estable, versionada) en lugar del SDK para no atarse a su versión.
/// TODO producción: mover la API key a Azure Key Vault (hoy se lee de config/entorno Anthropic__ApiKey).
/// </summary>
public class AnthropicAiClient : IAiClient
{
    private const string Endpoint       = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<AnthropicAiClient> _logger;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly int    _maxTokens;

    public AnthropicAiClient(IHttpClientFactory httpFactory, IConfiguration config, ILogger<AnthropicAiClient> logger)
    {
        _httpFactory = httpFactory;
        _logger      = logger;
        _apiKey      = config["Anthropic:ApiKey"] ?? "";
        _model       = config["Anthropic:Model"] ?? "claude-sonnet-5";
        _maxTokens   = int.TryParse(config["Anthropic:MaxTokens"], out var mt) ? mt : 4000;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<string> CompleteTextAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
        => await SendAsync(systemPrompt, userPrompt, ct);

    public async Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, string? jsonSchema = null, CancellationToken ct = default)
    {
        var sys = systemPrompt;
        if (!string.IsNullOrWhiteSpace(jsonSchema))
            sys += "\n\nResponde ÚNICAMENTE con JSON válido que cumpla este esquema. " +
                   "Sin texto extra, sin markdown, sin ```.\n" + jsonSchema;
        else
            sys += "\n\nResponde ÚNICAMENTE con JSON válido. Sin texto extra, sin markdown.";

        var raw = await SendAsync(sys, userPrompt, ct);
        return ExtractJson(raw);
    }

    public async Task<AiWebSearchResult> CompleteJsonWithWebSearchAsync(
        string systemPrompt, string userPrompt, string jsonSchema, int maxSearches, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Anthropic no está configurado (falta Anthropic:ApiKey).");

        var sys = systemPrompt +
            "\n\nAl final, responde ÚNICAMENTE con JSON válido que cumpla este esquema. " +
            "Sin texto extra, sin markdown, sin ```.\n" + jsonSchema;

        var payload = new
        {
            model      = _model,
            max_tokens = Math.Max(_maxTokens, 8000),
            system     = sys,
            tools      = new object[]
            {
                new { type = "web_search_20250305", name = "web_search", max_uses = Math.Max(1, maxSearches) },
            },
            messages   = new[] { new { role = "user", content = userPrompt } },
        };

        var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(180); // una corrida con búsqueda puede tardar 1-2 min
        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        req.Headers.TryAddWithoutValidation("x-api-key", _apiKey);
        req.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);

        using var resp = await client.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("Anthropic API (web search) {Status}: {Body}", (int)resp.StatusCode, body);
            throw new HttpRequestException($"Anthropic API error {(int)resp.StatusCode}");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var sb = new StringBuilder();
        if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            foreach (var block in content.EnumerateArray())
                if (block.TryGetProperty("type", out var t) && t.GetString() == "text"
                    && block.TryGetProperty("text", out var txt))
                    sb.Append(txt.GetString());

        int inTok = 0, outTok = 0, searches = 0;
        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("input_tokens", out var it))  inTok  = it.GetInt32();
            if (usage.TryGetProperty("output_tokens", out var ot)) outTok = ot.GetInt32();
            if (usage.TryGetProperty("server_tool_use", out var stu)
                && stu.TryGetProperty("web_search_requests", out var wsr))
                searches = wsr.GetInt32();
        }

        return new AiWebSearchResult(ExtractJson(sb.ToString()), inTok, outTok, searches);
    }

    // ── HTTP ───────────────────────────────────────────────────────────────────

    private async Task<string> SendAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Anthropic no está configurado (falta Anthropic:ApiKey).");

        var payload = new
        {
            model      = _model,
            max_tokens = _maxTokens,
            system     = systemPrompt,
            messages   = new[] { new { role = "user", content = userPrompt } },
        };

        var client = _httpFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        req.Headers.TryAddWithoutValidation("x-api-key", _apiKey);
        req.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);

        using var resp = await client.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("Anthropic API {Status}: {Body}", (int)resp.StatusCode, body);
            throw new HttpRequestException($"Anthropic API error {(int)resp.StatusCode}");
        }

        // Respuesta: { "content": [ { "type": "text", "text": "..." } ], ... }
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var block in content.EnumerateArray())
                if (block.TryGetProperty("type", out var t) && t.GetString() == "text"
                    && block.TryGetProperty("text", out var txt))
                    sb.Append(txt.GetString());
            return sb.ToString();
        }
        return "";
    }

    /// <summary>Quita fences ```json y extrae el primer objeto/array JSON del texto.</summary>
    private static string ExtractJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "{}";
        var cleaned = Regex.Replace(raw.Trim(), @"^```(?:json)?|```$", "", RegexOptions.Multiline).Trim();

        int start = cleaned.IndexOfAny(new[] { '{', '[' });
        int end   = cleaned.LastIndexOfAny(new[] { '}', ']' });
        if (start >= 0 && end > start) return cleaned.Substring(start, end - start + 1);
        return cleaned;
    }
}
