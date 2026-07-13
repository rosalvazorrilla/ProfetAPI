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
