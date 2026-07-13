namespace ProfetAPI.Services;

/// <summary>
/// Abstracción del cliente de IA (Claude). Toda la IA del proyecto (scoring, dashboard,
/// llamadas, inbox…) pasa por aquí. La IA elige/interpreta; el cálculo es determinista.
/// </summary>
public interface IAiClient
{
    /// <summary>
    /// Pide a Claude una respuesta en JSON. Se le pasa el esquema esperado para forzar la forma;
    /// devuelve el JSON crudo (string) listo para deserializar. Lanza si la IA no está configurada.
    /// </summary>
    Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, string? jsonSchema = null, CancellationToken ct = default);

    /// <summary>Respuesta de texto libre (resúmenes, sugerencias).</summary>
    Task<string> CompleteTextAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);

    /// <summary>true si hay API key configurada (para gating/feature-flag en la UI).</summary>
    bool IsConfigured { get; }
}
