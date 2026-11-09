using System.Security.Cryptography;
using System.Text;

namespace ProfetAPI.Services;

/// <summary>Comparación de secretos de webhooks en tiempo constante (evita ataques de temporización).</summary>
public static class WebhookSecurity
{
    public static bool SecretsMatch(string? expected, string? provided)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(provided)) return false;
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>
    /// Valida el secreto de un webhook de 2Chat. Si TwoChat:WebhookSecret NO está configurado se permite la
    /// llamada (compatibilidad con los webhooks ya registrados en 2Chat); en cuanto se configura, es obligatorio
    /// mandarlo en ?secret= (o en el header X-Webhook-Secret).
    /// </summary>
    public static bool IsTwoChatWebhookAuthorized(IConfiguration config, HttpRequest req)
    {
        var expected = config["TwoChat:WebhookSecret"];
        if (string.IsNullOrEmpty(expected)) return true;
        var provided = req.Query["secret"].FirstOrDefault() ?? req.Headers["X-Webhook-Secret"].FirstOrDefault();
        return SecretsMatch(expected, provided);
    }
}
