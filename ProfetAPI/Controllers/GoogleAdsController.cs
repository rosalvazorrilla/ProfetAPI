using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Services;
using Swashbuckle.AspNetCore.Annotations;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace ProfetAPI.Controllers;

/// <summary>
/// D4: conexión OAuth con Google Ads (cuentas dinámicas vía listAccessibleCustomers,
/// igual que el flujo de Meta) y lectura de KPIs de la cuenta vinculada.
/// </summary>
[Route("api/googleads")]
[ApiController]
[Authorize]
[SwaggerTag("Google Ads — OAuth e integración de KPIs")]
public class GoogleAdsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly SecretProtector _secrets;
    private readonly GoogleAdsOAuthPendingStore _pending;
    private readonly ILogger<GoogleAdsController> _log;

    public GoogleAdsController(
        ApplicationDbContext db, IHttpClientFactory http, IConfiguration config,
        SecretProtector secrets, GoogleAdsOAuthPendingStore pending, ILogger<GoogleAdsController> log)
    {
        _db = db; _http = http; _config = config; _secrets = secrets; _pending = pending; _log = log;
    }

    private string? UserId  => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    private bool    IsAdmin => User.FindFirst(ClaimTypes.Role)?.Value == "AdminGlobal";
    private string  ApiVersion => _config["GoogleAds:ApiVersion"] ?? "v17";

    private async Task<bool> HasAccess(int accountId)
    {
        if (IsAdmin) return true;
        return await _db.AccountInternalUsers.AnyAsync(u => u.AccountId == accountId && u.UserId == UserId);
    }

    // ── OAuth: intercambia el código (Google Identity Services, ux_mode "popup") ────

    [HttpPost("connect")]
    [SwaggerOperation(Summary = "Intercambia el código de OAuth y regresa las cuentas de Google Ads accesibles")]
    public async Task<IActionResult> Connect([FromBody] ConnectGoogleAdsRequest req)
    {
        var resolvedAccountId = await ResolveAccountId(req.AccountId);
        if (resolvedAccountId == null) return NotFound(new { message = "Sin cuenta asignada." });
        if (string.IsNullOrWhiteSpace(req.Code)) return BadRequest(new { message = "Código de autorización requerido." });

        var clientId     = _config["GoogleAds:ClientId"];
        var clientSecret = _config["GoogleAds:ClientSecret"];
        var developerToken = _config["GoogleAds:DeveloperToken"];
        var client = _http.CreateClient();

        // 1. Intercambiar el código → access_token + refresh_token.
        // redirect_uri debe ser literalmente "postmessage" cuando el código viene de
        // google.accounts.oauth2.initCodeClient con ux_mode: "popup".
        var tokenResp = await client.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = req.Code,
            ["client_id"] = clientId ?? "",
            ["client_secret"] = clientSecret ?? "",
            ["redirect_uri"] = "postmessage",
            ["grant_type"] = "authorization_code",
        }));
        var tokenJson = await tokenResp.Content.ReadAsStringAsync();
        if (!tokenResp.IsSuccessStatusCode)
        {
            _log.LogWarning("Google Ads token exchange failed: {Json}", tokenJson);
            return BadRequest(new { message = "No se pudo autorizar con Google. Vuelve a intentar la conexión." });
        }

        using var tokenDoc = JsonDocument.Parse(tokenJson);
        var accessToken  = tokenDoc.RootElement.TryGetProperty("access_token", out var at) ? at.GetString() : null;
        var refreshToken = tokenDoc.RootElement.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        if (string.IsNullOrEmpty(accessToken)) return BadRequest(new { message = "Google no devolvió un token de acceso." });
        if (string.IsNullOrEmpty(refreshToken))
            return BadRequest(new { message = "Google no devolvió un refresh token. Revoca el acceso previo en https://myaccount.google.com/permissions e intenta de nuevo." });

        // 2. Listar cuentas de Google Ads accesibles para este login.
        var listReq = new HttpRequestMessage(HttpMethod.Get, $"https://googleads.googleapis.com/{ApiVersion}/customers:listAccessibleCustomers");
        listReq.Headers.Add("Authorization", $"Bearer {accessToken}");
        listReq.Headers.Add("developer-token", developerToken);
        var listResp = await client.SendAsync(listReq);
        var listJson = await listResp.Content.ReadAsStringAsync();
        if (!listResp.IsSuccessStatusCode)
        {
            _log.LogWarning("Google Ads listAccessibleCustomers failed: {Json}", listJson);
            return BadRequest(new { message = "No se pudieron obtener las cuentas de Google Ads. Verifica el Developer Token." });
        }

        var customerIds = new List<string>();
        using (var listDoc = JsonDocument.Parse(listJson))
            if (listDoc.RootElement.TryGetProperty("resourceNames", out var names))
                foreach (var n in names.EnumerateArray())
                {
                    var value = n.GetString() ?? "";
                    var id = value.Contains('/') ? value[(value.LastIndexOf('/') + 1)..] : value;
                    if (!string.IsNullOrEmpty(id)) customerIds.Add(id);
                }

        if (customerIds.Count == 0)
            return BadRequest(new { message = "Esa cuenta de Google no tiene acceso a ninguna cuenta de Google Ads." });

        var accounts = new List<GoogleAdsAccountOption>();
        foreach (var customerId in customerIds)
        {
            var name = await TryGetDescriptiveName(client, customerId, accessToken!, developerToken);
            accounts.Add(new GoogleAdsAccountOption(customerId, name ?? customerId));
        }

        var refreshTokenEncrypted = _secrets.Protect(refreshToken)!;
        var nonce = _pending.Create(resolvedAccountId.Value, refreshTokenEncrypted, accounts);

        return Ok(new { nonce, accounts });
    }

    private async Task<string?> TryGetDescriptiveName(HttpClient client, string customerId, string accessToken, string? developerToken)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"https://googleads.googleapis.com/{ApiVersion}/customers/{customerId}/googleAds:search");
            req.Headers.Add("Authorization", $"Bearer {accessToken}");
            req.Headers.Add("developer-token", developerToken);
            req.Content = new StringContent(JsonSerializer.Serialize(new { query = "SELECT customer.descriptive_name FROM customer LIMIT 1" }), Encoding.UTF8, "application/json");
            var resp = await client.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0) return null;
            var customer = results[0].GetProperty("customer");
            return customer.TryGetProperty("descriptiveName", out var dn) ? dn.GetString() : null;
        }
        catch { return null; }
    }

    // ── Confirmar vínculo ────────────────────────────────────────────────────────

    [HttpPost("link")]
    [SwaggerOperation(Summary = "Confirma qué cuenta de Google Ads vincular a la cuenta de Profet")]
    public async Task<IActionResult> Link([FromBody] LinkGoogleAdsRequest req)
    {
        var resolvedAccountId = await ResolveAccountId(req.AccountId);
        if (resolvedAccountId == null) return NotFound(new { message = "Sin cuenta asignada." });

        var pending = _pending.Consume(req.Nonce, resolvedAccountId.Value);
        if (pending == null) return BadRequest(new { message = "La conexión expiró. Vuelve a conectar tu cuenta de Google Ads." });

        var option = pending.Accounts.FirstOrDefault(a => a.CustomerId == req.CustomerId);
        if (option == null) return BadRequest(new { message = "Esa cuenta no estaba en la lista de cuentas disponibles." });

        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.AccountId == resolvedAccountId.Value);
        if (account == null) return NotFound();

        account.GoogleAdsCustomerId = option.CustomerId;
        account.GoogleAdsAccountName = option.Name;
        account.GoogleAdsRefreshTokenEncrypted = pending.RefreshTokenEncrypted;
        await _db.SaveChangesAsync();

        return Ok(new { connected = true, customerId = option.CustomerId, accountName = option.Name });
    }

    // ── Configuración actual ─────────────────────────────────────────────────────

    [HttpGet("config")]
    [SwaggerOperation(Summary = "Estado de la conexión de Google Ads para la cuenta")]
    public async Task<IActionResult> GetConfig([FromQuery] int? accountId)
    {
        var resolved = await ResolveAccountId(accountId);
        if (resolved == null) return NotFound(new { message = "Sin cuenta asignada." });

        var account = await _db.Accounts.AsNoTracking()
            .Where(a => a.AccountId == resolved)
            .Select(a => new { a.GoogleAdsCustomerId, a.GoogleAdsAccountName })
            .FirstOrDefaultAsync();
        if (account == null) return NotFound();

        return Ok(new
        {
            connected = account.GoogleAdsCustomerId != null,
            customerId = account.GoogleAdsCustomerId,
            accountName = account.GoogleAdsAccountName,
        });
    }

    [HttpDelete("config")]
    [SwaggerOperation(Summary = "Desconecta la cuenta de Google Ads vinculada")]
    public async Task<IActionResult> Disconnect([FromQuery] int? accountId)
    {
        var resolved = await ResolveAccountId(accountId);
        if (resolved == null) return NotFound(new { message = "Sin cuenta asignada." });

        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.AccountId == resolved);
        if (account == null) return NotFound();

        account.GoogleAdsCustomerId = null;
        account.GoogleAdsAccountName = null;
        account.GoogleAdsRefreshTokenEncrypted = null;
        await _db.SaveChangesAsync();

        return Ok(new { disconnected = true });
    }

    private async Task<int?> ResolveAccountId(int? accountId)
    {
        if (accountId.HasValue)
        {
            if (!await HasAccess(accountId.Value)) return null;
            return accountId;
        }
        if (IsAdmin) return null;
        return await _db.AccountInternalUsers
            .Where(a => a.UserId == UserId)
            .Select(a => (int?)a.AccountId).FirstOrDefaultAsync();
    }

    // ── KPIs ─────────────────────────────────────────────────────────────────────

    [HttpGet("kpis")]
    [SwaggerOperation(Summary = "KPIs de Google Ads de la cuenta vinculada (clics, costo, conversiones)")]
    public async Task<IActionResult> GetKpis([FromQuery] int? accountId, [FromQuery] int days = 30)
    {
        var resolved = await ResolveAccountId(accountId);
        if (resolved == null) return NotFound(new { message = "Sin cuenta asignada." });

        var account = await _db.Accounts.AsNoTracking()
            .Where(a => a.AccountId == resolved)
            .Select(a => new { a.GoogleAdsCustomerId, a.GoogleAdsAccountName, a.GoogleAdsRefreshTokenEncrypted })
            .FirstOrDefaultAsync();
        if (account?.GoogleAdsCustomerId == null || account.GoogleAdsRefreshTokenEncrypted == null)
            return Ok(new { connected = false });

        var refreshToken = _secrets.Unprotect(account.GoogleAdsRefreshTokenEncrypted);
        if (refreshToken == null) return Ok(new { connected = false });

        var clientId     = _config["GoogleAds:ClientId"];
        var clientSecret = _config["GoogleAds:ClientSecret"];
        var developerToken = _config["GoogleAds:DeveloperToken"];
        var client = _http.CreateClient();

        var tokenResp = await client.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId ?? "",
            ["client_secret"] = clientSecret ?? "",
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
        }));
        if (!tokenResp.IsSuccessStatusCode)
        {
            _log.LogWarning("Google Ads refresh token exchange failed for account {AccountId}: {Body}", resolved, await tokenResp.Content.ReadAsStringAsync());
            return Ok(new { connected = false, error = "token_expired" });
        }
        using var tokenDoc = JsonDocument.Parse(await tokenResp.Content.ReadAsStringAsync());
        var accessToken = tokenDoc.RootElement.TryGetProperty("access_token", out var at) ? at.GetString() : null;
        if (string.IsNullOrEmpty(accessToken)) return Ok(new { connected = false });

        var range = days <= 7 ? "LAST_7_DAYS" : days <= 14 ? "LAST_14_DAYS" : "LAST_30_DAYS";
        var query = $"SELECT metrics.clicks, metrics.impressions, metrics.cost_micros, metrics.conversions FROM customer WHERE segments.date DURING {range}";

        var searchReq = new HttpRequestMessage(HttpMethod.Post, $"https://googleads.googleapis.com/{ApiVersion}/customers/{account.GoogleAdsCustomerId}/googleAds:search");
        searchReq.Headers.Add("Authorization", $"Bearer {accessToken}");
        searchReq.Headers.Add("developer-token", developerToken);
        searchReq.Content = new StringContent(JsonSerializer.Serialize(new { query }), Encoding.UTF8, "application/json");
        var searchResp = await client.SendAsync(searchReq);
        var searchJson = await searchResp.Content.ReadAsStringAsync();
        if (!searchResp.IsSuccessStatusCode)
        {
            _log.LogWarning("Google Ads metrics search failed for account {AccountId}: {Body}", resolved, searchJson);
            return Ok(new { connected = true, accountName = account.GoogleAdsAccountName, error = "metrics_unavailable" });
        }

        long clicks = 0, impressions = 0, costMicros = 0;
        double conversions = 0;
        using (var doc = JsonDocument.Parse(searchJson))
            if (doc.RootElement.TryGetProperty("results", out var results))
                foreach (var row in results.EnumerateArray())
                {
                    if (!row.TryGetProperty("metrics", out var m)) continue;
                    if (m.TryGetProperty("clicks", out var c)) clicks += long.Parse(c.GetString() ?? "0");
                    if (m.TryGetProperty("impressions", out var i)) impressions += long.Parse(i.GetString() ?? "0");
                    if (m.TryGetProperty("costMicros", out var cm)) costMicros += long.Parse(cm.GetString() ?? "0");
                    if (m.TryGetProperty("conversions", out var conv)) conversions += conv.GetDouble();
                }

        var cost = costMicros / 1_000_000.0;
        return Ok(new
        {
            connected = true,
            accountName = account.GoogleAdsAccountName,
            days,
            clicks,
            impressions,
            cost = Math.Round(cost, 2),
            conversions = Math.Round(conversions, 1),
            ctr = impressions > 0 ? Math.Round(clicks * 100.0 / impressions, 2) : 0,
            avgCpc = clicks > 0 ? Math.Round(cost / clicks, 2) : 0,
            costPerConversion = conversions > 0 ? Math.Round(cost / conversions, 2) : (double?)null,
        });
    }
}

public record ConnectGoogleAdsRequest(int? AccountId, string Code);
public record LinkGoogleAdsRequest(int? AccountId, string Nonce, string CustomerId);
