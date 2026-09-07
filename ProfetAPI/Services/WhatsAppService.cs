using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;

namespace ProfetAPI.Services;

/// <summary>
/// Envío saliente de WhatsApp vía 2Chat. Extraído de InboxController para que también
/// lo use el despachador automático de secuencias (SequenceDispatchService) y el envío
/// masivo manual, sin duplicar la llamada HTTP a 2Chat en varios lados.
/// </summary>
public interface IWhatsAppService
{
    Task<(bool success, string? error)> SendAsync(int customerId, string toPhone, string text);
}

public class WhatsAppService(ApplicationDbContext db, IHttpClientFactory httpFactory, IConfiguration config) : IWhatsAppService
{
    private const string TwoChatSendUrl = "https://api.p.2chat.io/open/whatsapp/send-message";

    public async Task<(bool success, string? error)> SendAsync(int customerId, string toPhone, string text)
    {
        var customer   = await db.Customers.FindAsync(customerId);
        var apiKey     = customer?.TwoChatApiKey ?? config["TwoChat:GlobalApiKey"] ?? "UAK6e31c29a-c640-4877-81d9-ad67113ec7b5";
        var fromNumber = customer?.WhatsappNumber;
        if (string.IsNullOrEmpty(fromNumber))
            return (false, "No hay número de WhatsApp configurado para este tenant.");

        var client = httpFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-User-API-Key", apiKey);
        var payload = new { to_number = toPhone, from_number = "+" + fromNumber.TrimStart('+'), text };

        try
        {
            var resp = await client.PostAsync(TwoChatSendUrl,
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
            if (!resp.IsSuccessStatusCode)
                return (false, $"2Chat respondió {(int)resp.StatusCode}");
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
