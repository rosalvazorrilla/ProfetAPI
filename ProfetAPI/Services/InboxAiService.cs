namespace ProfetAPI.Services;

public interface IInboxAiService
{
    Task<string> SummarizeAsync(string threadText, CancellationToken ct = default);
    Task<string> SuggestReplyAsync(string threadText, string contactName, CancellationToken ct = default);
    bool IsConfigured { get; }
}

/// <summary>
/// IA de la bandeja unificada: resume conversaciones largas y sugiere una respuesta editable.
/// La IA nunca envía nada — solo redacta; el vendedor confirma antes de enviar.
/// </summary>
public class InboxAiService(IAiClient ai) : IInboxAiService
{
    public bool IsConfigured => ai.IsConfigured;

    public async Task<string> SummarizeAsync(string threadText, CancellationToken ct = default)
    {
        if (!ai.IsConfigured || string.IsNullOrWhiteSpace(threadText)) return "";
        const string system = "Resume esta conversación con un prospecto/cliente en 2-3 frases: qué quiere, en qué quedó, y si falta responderle algo. Español, directo, sin relleno.";
        return (await ai.CompleteTextAsync(system, threadText, ct)).Trim();
    }

    public async Task<string> SuggestReplyAsync(string threadText, string contactName, CancellationToken ct = default)
    {
        if (!ai.IsConfigured || string.IsNullOrWhiteSpace(threadText)) return "";
        var system = $"""
Eres un asesor de ventas escribiendo por WhatsApp/email a {contactName}. Lee la conversación y redacta UNA
respuesta breve, natural y útil para continuar la conversación (no un resumen, la respuesta en sí).
Español, tono cercano y profesional. Sin firma ni saludo genérico de "Estimado".
""";
        return (await ai.CompleteTextAsync(system, threadText, ct)).Trim();
    }
}
