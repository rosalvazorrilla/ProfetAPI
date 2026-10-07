using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProfetAPI.Services;

public interface IWebhookDispatcherService
{
    /// <summary>Manda el evento a los webhooks salientes activos de la cuenta que lo escuchan (con reintentos).</summary>
    Task DispatchAsync(int accountId, string triggerEvent, object payload);
}

public static class WebhookDispatchExtensions
{
    /// <summary>Lo manda en segundo plano, con su propio scope: no frena la petición del usuario ni la hace fallar.</summary>
    public static void DispatchInBackground(this IServiceScopeFactory scopes, int accountId, string triggerEvent, object payload)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IWebhookDispatcherService>().DispatchAsync(accountId, triggerEvent, payload);
            }
            catch (Exception ex)
            {
                scopes.CreateScope().ServiceProvider.GetRequiredService<ILogger<WebhookDispatcherService>>()
                    .LogError(ex, "Fallo al despachar el webhook {Event} de la cuenta {AccountId}", triggerEvent, accountId);
            }
        });
    }
}

public class WebhookDispatcherService : IWebhookDispatcherService
{
    /// <summary>Intentos totales por envío: el primero + 2 reintentos (3 en total).</summary>
    public const int MaxAttempts = 3;

    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory   _httpFactory;
    private readonly IFeatureGateService  _gate;
    private readonly ILogger<WebhookDispatcherService> _logger;
    private readonly TimeSpan[] _retryDelays;

    public WebhookDispatcherService(
        ApplicationDbContext db,
        IHttpClientFactory httpFactory,
        IFeatureGateService gate,
        ILogger<WebhookDispatcherService> logger,
        TimeSpan[]? retryDelays = null)
    {
        _db          = db;
        _httpFactory = httpFactory;
        _gate        = gate;
        _logger      = logger;
        // Espera antes del 2.º y del 3.er intento (el servidor del cliente suele recuperarse en segundos).
        _retryDelays = retryDelays ?? new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8) };
    }

    /// <summary>Falla "temporal" que vale la pena reintentar: error del servidor, límite de peticiones o tiempo agotado.</summary>
    private static bool IsRetryable(int statusCode) => statusCode >= 500 || statusCode == 429 || statusCode == 408;

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

        // API externa / Webhooks es de Evolution; un cliente migrado sin ese derecho no manda eventos.
        var customerId = await _db.Accounts.Where(a => a.AccountId == accountId).Select(a => a.CustomerId).FirstOrDefaultAsync();
        if (customerId != 0 && !await _gate.IsAllowedAsync(customerId, "EXTERNAL_API")) return;

        var json   = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);

        foreach (var wh in webhooks)
        {
            string? lastError = null;
            int attempts = 0;
            bool delivered = false;

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                attempts = attempt;
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, wh.TargetUrl);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                    if (!string.IsNullOrEmpty(wh.OutgoingSecret))
                    {
                        var sig = "sha256=" + Convert.ToHexString(
                            HMACSHA256.HashData(Encoding.UTF8.GetBytes(wh.OutgoingSecret), Encoding.UTF8.GetBytes(json))
                        ).ToLower();
                        request.Headers.Add("X-Profet-Signature", sig);
                    }
                    request.Headers.Add("X-Profet-Event", triggerEvent);
                    request.Headers.Add("X-Profet-AccountId", accountId.ToString());
                    request.Headers.Add("X-Profet-Attempt", attempt.ToString());

                    var response = await client.SendAsync(request);
                    var code = (int)response.StatusCode;
                    _logger.LogInformation("Webhook saliente {Id} → {Event} → {Url} — HTTP {Status} (intento {Attempt}/{Max})",
                        wh.WebhookId, triggerEvent, wh.TargetUrl, code, attempt, MaxAttempts);

                    if (response.IsSuccessStatusCode) { delivered = true; lastError = null; break; }

                    lastError = $"HTTP {code}";
                    if (!IsRetryable(code)) break; // 4xx: el servidor del cliente rechazó el mensaje; repetirlo no ayuda
                }
                catch (Exception ex)
                {
                    lastError = ex.Message[..Math.Min(ex.Message.Length, 200)];
                    _logger.LogWarning(ex, "Webhook saliente {Id} falló (intento {Attempt}/{Max})", wh.WebhookId, attempt, MaxAttempts);
                }

                if (attempt < MaxAttempts)
                {
                    var wait = _retryDelays[Math.Min(attempt - 1, _retryDelays.Length - 1)];
                    if (wait > TimeSpan.Zero) await Task.Delay(wait);
                }
            }

            wh.LastTriggeredAt = DateTime.UtcNow;
            wh.TriggerCount++;
            wh.LastError = delivered ? null : $"{lastError} (tras {attempts} intento{(attempts == 1 ? "" : "s")})";
        }

        await _db.SaveChangesAsync();
    }
}
