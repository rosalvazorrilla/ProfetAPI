using System.Text.Json;

namespace ProfetAPI.Services;

public class MetaAdsService(IHttpClientFactory httpFactory)
{
    private const string GraphBase = "https://graph.facebook.com/v21.0";

    public record CampaignInsight(
        string  CampaignId,
        string  CampaignName,
        long    Impressions,
        long    Reach,
        long    Clicks,
        decimal Spend,
        double  Cpc,
        double  Cpm,
        double  Ctr
    );

    /// <summary>
    /// Llama a /{adAccountId}/insights con level=campaign para el rango de fechas dado.
    /// Retorna lista vacía si el token no tiene ads_read o si ocurre cualquier error.
    /// </summary>
    public async Task<(List<CampaignInsight> Data, string? Error)> GetCampaignInsightsAsync(
        string adAccountId,
        string accessToken,
        DateTime dateFrom,
        DateTime dateTo)
    {
        try
        {
            var http   = httpFactory.CreateClient();
            var since  = dateFrom.ToString("yyyy-MM-dd");
            var until  = dateTo.ToString("yyyy-MM-dd");
            var range  = Uri.EscapeDataString($"{{\"since\":\"{since}\",\"until\":\"{until}\"}}");
            var fields = "campaign_id,campaign_name,impressions,reach,clicks,spend,cpc,cpm,ctr";
            var cleanId = adAccountId.TrimStart().TrimStart('a','c','t','_');

            var url = $"{GraphBase}/act_{cleanId}/insights" +
                      $"?level=campaign&fields={fields}&time_range={range}&limit=200" +
                      $"&access_token={Uri.EscapeDataString(accessToken)}";

            var json = await http.GetStringAsync(url);
            var doc  = JsonDocument.Parse(json);

            // Detect Meta error response
            if (doc.RootElement.TryGetProperty("error", out var errEl))
            {
                var msg = errEl.TryGetProperty("message", out var m) ? m.GetString() : "Meta API error";
                return ([], msg);
            }

            if (!doc.RootElement.TryGetProperty("data", out var dataEl))
                return ([], null);

            var result = new List<CampaignInsight>();
            foreach (var item in dataEl.EnumerateArray())
            {
                result.Add(new CampaignInsight(
                    CampaignId   : item.GetStr("campaign_id") ?? "",
                    CampaignName : item.GetStr("campaign_name") ?? "Sin nombre",
                    Impressions  : item.GetLong("impressions"),
                    Reach        : item.GetLong("reach"),
                    Clicks       : item.GetLong("clicks"),
                    Spend        : item.GetDecimal("spend"),
                    Cpc          : item.GetDbl("cpc"),
                    Cpm          : item.GetDbl("cpm"),
                    Ctr          : item.GetDbl("ctr")
                ));
            }
            return (result, null);
        }
        catch (Exception ex)
        {
            return ([], ex.Message);
        }
    }
}

// ── JSON helper extensions ────────────────────────────────────────────────────
internal static class MetaJsonExt
{
    public static string? GetStr(this JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) ? v.GetString() : null;

    public static long GetLong(this JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return 0;
        return v.ValueKind == JsonValueKind.Number
            ? v.GetInt64()
            : long.TryParse(v.GetString(), out var n) ? n : 0;
    }

    public static decimal GetDecimal(this JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return 0m;
        return v.ValueKind == JsonValueKind.Number
            ? v.GetDecimal()
            : decimal.TryParse(v.GetString(),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0m;
    }

    public static double GetDbl(this JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return 0d;
        return v.ValueKind == JsonValueKind.Number
            ? v.GetDouble()
            : double.TryParse(v.GetString(),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0d;
    }
}
