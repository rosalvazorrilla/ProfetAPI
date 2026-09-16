using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;

namespace ProfetAPI.Services;

/// <summary>
/// Lectura de insights de Google Ads con desglose por campaña y por día — separado del
/// GetKpis de GoogleAdsController (que solo trae un total del período, sin desglose) para
/// que el motor de métricas (MetricsQueryService) pueda usarlo sin depender de un Controller.
/// Mismo patrón que MetaAdsService.
/// </summary>
public class GoogleAdsService(
    ApplicationDbContext db,
    IHttpClientFactory httpFactory,
    IConfiguration config,
    SecretProtector secrets,
    ILogger<GoogleAdsService> logger)
{
    public record CampaignDayInsight(
        string   CampaignName,
        DateTime Date,
        long     Clicks,
        long     Impressions,
        decimal  Cost,
        double   Conversions
    );

    /// <summary>
    /// Trae clics/impresiones/costo/conversiones por campaña y por día en el rango dado.
    /// Lista vacía + Error si la cuenta no tiene Google Ads conectado o el token expiró.
    /// </summary>
    public async Task<(List<CampaignDayInsight> Data, string? Error)> GetCampaignDailyInsightsAsync(
        int accountId, DateTime from, DateTime to)
    {
        var account = await db.Accounts.AsNoTracking()
            .Where(a => a.AccountId == accountId)
            .Select(a => new { a.GoogleAdsCustomerId, a.GoogleAdsRefreshTokenEncrypted })
            .FirstOrDefaultAsync();
        if (account?.GoogleAdsCustomerId == null || account.GoogleAdsRefreshTokenEncrypted == null)
            return (new(), "not_connected");

        var refreshToken = secrets.Unprotect(account.GoogleAdsRefreshTokenEncrypted);
        if (refreshToken == null) return (new(), "not_connected");

        var clientId       = config["GoogleAds:ClientId"];
        var clientSecret   = config["GoogleAds:ClientSecret"];
        var developerToken = config["GoogleAds:DeveloperToken"];
        var apiVersion     = config["GoogleAds:ApiVersion"] ?? "v17";
        var http = httpFactory.CreateClient();

        try
        {
            var tokenResp = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"]     = clientId ?? "",
                ["client_secret"] = clientSecret ?? "",
                ["refresh_token"] = refreshToken,
                ["grant_type"]    = "refresh_token",
            }));
            if (!tokenResp.IsSuccessStatusCode)
            {
                logger.LogWarning("Google Ads token refresh falló para cuenta {AccountId}: {Body}", accountId, await tokenResp.Content.ReadAsStringAsync());
                return (new(), "token_expired");
            }
            using var tokenDoc = JsonDocument.Parse(await tokenResp.Content.ReadAsStringAsync());
            var accessToken = tokenDoc.RootElement.TryGetProperty("access_token", out var at) ? at.GetString() : null;
            if (string.IsNullOrEmpty(accessToken)) return (new(), "token_expired");

            var since = from.ToString("yyyy-MM-dd");
            var until = to.ToString("yyyy-MM-dd");
            var query = "SELECT campaign.name, segments.date, metrics.clicks, metrics.impressions, " +
                        $"metrics.cost_micros, metrics.conversions FROM campaign WHERE segments.date BETWEEN '{since}' AND '{until}'";

            var req = new HttpRequestMessage(HttpMethod.Post,
                $"https://googleads.googleapis.com/{apiVersion}/customers/{account.GoogleAdsCustomerId}/googleAds:search");
            req.Headers.Add("Authorization", $"Bearer {accessToken}");
            req.Headers.Add("developer-token", developerToken);
            req.Content = new StringContent(JsonSerializer.Serialize(new { query }), Encoding.UTF8, "application/json");

            var resp = await http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Google Ads campaign insights falló para cuenta {AccountId}: {Body}", accountId, json);
                return (new(), "metrics_unavailable");
            }

            var result = new List<CampaignDayInsight>();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("results", out var results))
            {
                foreach (var row in results.EnumerateArray())
                {
                    var campaignName = row.TryGetProperty("campaign", out var camp) && camp.TryGetProperty("name", out var cn)
                        ? cn.GetString() ?? "Sin campaña" : "Sin campaña";
                    var dateStr = row.TryGetProperty("segments", out var seg) && seg.TryGetProperty("date", out var d)
                        ? d.GetString() : null;
                    if (dateStr == null || !DateTime.TryParse(dateStr, out var date)) continue;
                    if (!row.TryGetProperty("metrics", out var m)) continue;

                    long clicks      = m.TryGetProperty("clicks", out var c)      ? long.Parse(c.GetString() ?? "0")  : 0;
                    long impressions = m.TryGetProperty("impressions", out var i) ? long.Parse(i.GetString() ?? "0")  : 0;
                    long costMicros  = m.TryGetProperty("costMicros", out var cm) ? long.Parse(cm.GetString() ?? "0") : 0;
                    double conversions = m.TryGetProperty("conversions", out var conv) ? conv.GetDouble() : 0;

                    result.Add(new CampaignDayInsight(campaignName, date, clicks, impressions, costMicros / 1_000_000m, conversions));
                }
            }
            return (result, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Excepción leyendo insights de Google Ads para cuenta {AccountId}", accountId);
            return (new(), "metrics_unavailable");
        }
    }
}
