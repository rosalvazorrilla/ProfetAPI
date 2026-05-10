using System.Text;
using System.Text.Json;

namespace ProfetAPI.Services;

public class TwoChatService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private const string BaseUrl = "https://api.p.2chat.io";

    public TwoChatService(IHttpClientFactory httpFactory, IConfiguration config)
    {
        _httpFactory = httpFactory;
        _config = config;
    }

    private string GlobalApiKey => _config["TwoChat:GlobalApiKey"] ?? "UAK6e31c29a-c640-4877-81d9-ad67113ec7b5";
    private string WebhookBaseUrl => _config["TwoChat:WebhookBaseUrl"] ?? "https://profet.mx";

    /// <summary>
    /// Suscribe los webhooks de received y sent para un número de WhatsApp.
    /// Devuelve los UUIDs de los webhooks creados.
    /// </summary>
    public async Task<(string? receiveId, string? sentId)> SubscribeWebhooks(string phoneNumber, string? customApiKey = null)
    {
        var apiKey = customApiKey ?? GlobalApiKey;
        var receiveId = await Subscribe(apiKey, phoneNumber, "whatsapp.message.received", $"{WebhookBaseUrl}/api/whatsapp/webhook");
        var sentId    = await Subscribe(apiKey, phoneNumber, "whatsapp.message.sent",     $"{WebhookBaseUrl}/api/whatsapp/webhook/sent");
        return (receiveId, sentId);
    }

    /// <summary>Elimina un webhook por su UUID.</summary>
    public async Task UnsubscribeWebhook(string webhookId, string? customApiKey = null)
    {
        if (string.IsNullOrWhiteSpace(webhookId)) return;
        var apiKey = customApiKey ?? GlobalApiKey;
        var client = _httpFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-User-API-Key", apiKey);
        await client.DeleteAsync($"{BaseUrl}/open/webhooks/{webhookId}");
    }

    private async Task<string?> Subscribe(string apiKey, string phoneNumber, string eventType, string hookUrl)
    {
        var client = _httpFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-User-API-Key", apiKey);

        var payload = JsonSerializer.Serialize(new { hook_url = hookUrl, on_number = phoneNumber });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await client.PostAsync($"{BaseUrl}/open/webhooks/subscribe/{eventType}", content);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("webhook", out var wh) &&
            wh.TryGetProperty("uuid", out var uuid))
            return uuid.GetString();

        return null;
    }
}
