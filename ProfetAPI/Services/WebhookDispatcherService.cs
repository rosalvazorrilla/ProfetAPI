using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProfetAPI.Services;

public interface IWebhookDispatcherService
{
    Task DispatchAsync(int accountId, string triggerEvent, object payload);
}

public class WebhookDispatcherService : IWebhookDispatcherService
{
    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory   _httpFactory;
    private readonly ILogger<WebhookDispatcherService> _logger;

    public WebhookDispatcherService(
        ApplicationDbContext db,
        IHttpClientFactory httpFactory,
        ILogger<WebhookDispatcherService> logger)
    {
        _db          = db;
        _httpFactory = httpFactory;
        _logger      = logger;
    }

    public async Task DispatchAsync(int accountId, string triggerEvent, object payload)
    {
        var webhooks = await _db.AccountWebhooks
            .Where(w => w.AccountId == accountId
                     && w.Direction == "Outgoing"
                     && w.TriggerEvent == triggerEvent
                     && w.IsActive
                     && w.TargetUrl != null)
            .ToListAsync();

        if (webhooks.Count == 0) return;

        var json    = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var client  = _httpFactory.CreateClient();

        foreach (var wh in webhooks)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, wh.TargetUrl);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                // Firma opcional con HMAC-SHA256
                if (!string.IsNullOrEmpty(wh.OutgoingSecret))
                {
                    var sig = "sha256=" + Convert.ToHexString(
                        HMACSHA256.HashData(Encoding.UTF8.GetBytes(wh.OutgoingSecret), Encoding.UTF8.GetBytes(json))
                    ).ToLower();
                    request.Headers.Add("X-Profet-Signature", sig);
                }

                request.Headers.Add("X-Profet-Event",     triggerEvent);
                request.Headers.Add("X-Profet-AccountId", accountId.ToString());

                var response = await client.SendAsync(request);

                wh.LastTriggeredAt = DateTime.UtcNow;
                wh.TriggerCount++;
                wh.LastError = response.IsSuccessStatusCode
                    ? null
                    : $"HTTP {(int)response.StatusCode}";

                _logger.LogInformation("Outgoing webhook {Id} → {Event} → {Url} — {Status}",
                    wh.WebhookId, triggerEvent, wh.TargetUrl, (int)response.StatusCode);
            }
            catch (Exception ex)
            {
                wh.LastError       = ex.Message[..Math.Min(ex.Message.Length, 300)];
                wh.LastTriggeredAt = DateTime.UtcNow;
                wh.TriggerCount++;
                _logger.LogError(ex, "Error dispatching webhook {Id} → {Url}", wh.WebhookId, wh.TargetUrl);
            }
        }

        await _db.SaveChangesAsync();
    }
}
