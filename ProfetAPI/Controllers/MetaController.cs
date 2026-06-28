using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using System.Text.Json;

namespace ProfetAPI.Controllers;

[Route("api/meta")]
[ApiController]
[Authorize]
[SwaggerTag("Meta — OAuth e integración de páginas de Facebook")]
public class MetaController : ControllerBase
{
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration     _config;
    private readonly ILogger<MetaController> _log;

    public MetaController(IHttpClientFactory http, IConfiguration config, ILogger<MetaController> log)
    {
        _http   = http;
        _config = config;
        _log    = log;
    }

    /// <summary>
    /// Recibe un token de corta duración del SDK de Facebook,
    /// lo intercambia por un token permanente de página y regresa
    /// la lista de páginas administradas por el usuario.
    /// </summary>
    [HttpPost("pages")]
    [SwaggerOperation(Summary = "Intercambia token de Facebook y regresa lista de páginas con tokens permanentes")]
    [ProducesResponseType(typeof(List<MetaPageDto>), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> GetPages([FromBody] GetPagesRequest req)
    {
        var appId     = _config["Meta:AppId"]     ?? "";
        var appSecret = _config["Meta:AppSecret"] ?? "";
        var client    = _http.CreateClient();

        // ── 1. Intercambiar token corto → token largo (60 días) ──────────────
        var exchangeUrl = $"https://graph.facebook.com/v19.0/oauth/access_token" +
                          $"?grant_type=fb_exchange_token" +
                          $"&client_id={appId}" +
                          $"&client_secret={appSecret}" +
                          $"&fb_exchange_token={req.ShortLivedToken}";

        var exchangeResp = await client.GetAsync(exchangeUrl);
        var exchangeJson = await exchangeResp.Content.ReadAsStringAsync();

        if (!exchangeResp.IsSuccessStatusCode)
        {
            _log.LogWarning("Meta token exchange failed: {Json}", exchangeJson);
            return BadRequest("No se pudo intercambiar el token de Meta. Vuelve a conectar tu cuenta de Facebook.");
        }

        using var exchangeDoc  = JsonDocument.Parse(exchangeJson);
        var longLivedToken     = exchangeDoc.RootElement.GetProperty("access_token").GetString() ?? "";

        // ── 2. Traer páginas (me/accounts cubre páginas directas y las de Business Manager
        //       cuando el token tiene pages_show_list) ────────────────────────────────────
        var pages   = new List<MetaPageDto>();
        var seenIds = new HashSet<string>();

        await FetchPages(client,
            $"https://graph.facebook.com/v19.0/me/accounts?fields=name,id,access_token&limit=200&access_token={longLivedToken}",
            pages, seenIds);

        return Ok(pages);
    }

    /// <summary>
    /// Devuelve los formularios de Lead Ads de una página de Facebook.
    /// Requiere el Page Access Token permanente obtenido previamente.
    /// </summary>
    [HttpGet("pages/{pageId}/forms")]
    [SwaggerOperation(Summary = "Obtener formularios de Lead Ads de una página de Facebook")]
    [ProducesResponseType(typeof(List<MetaFormDto>), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> GetForms(string pageId, [FromQuery] string pageAccessToken)
    {
        if (string.IsNullOrWhiteSpace(pageAccessToken))
            return BadRequest("pageAccessToken es requerido.");

        var client = _http.CreateClient();
        var forms  = new List<MetaFormDto>();
        string? next = $"https://graph.facebook.com/v19.0/{pageId}/leadgen_forms" +
                       $"?fields=id,name,status&limit=100&access_token={pageAccessToken}";

        while (next != null)
        {
            var resp = await client.GetAsync(next);
            var json = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("Meta leadgen_forms failed for page {PageId}: {Json}", pageId, json);
                return BadRequest("No se pudieron obtener los formularios. Verifica el token de la página.");
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("data", out var data))
                foreach (var f in data.EnumerateArray())
                    forms.Add(new MetaFormDto(
                        f.GetProperty("id").GetString()     ?? "",
                        f.GetProperty("name").GetString()   ?? "",
                        f.TryGetProperty("status", out var s) ? s.GetString() ?? "" : ""
                    ));

            next = null;
            if (root.TryGetProperty("paging", out var paging) &&
                paging.TryGetProperty("next", out var nextProp))
                next = nextProp.GetString();
        }

        return Ok(forms);
    }

    private static async Task FetchPages(HttpClient client, string url, List<MetaPageDto> pages, HashSet<string> seenIds)
    {
        string? next = url;
        while (next != null)
        {
            var resp = await client.GetAsync(next);
            if (!resp.IsSuccessStatusCode) break;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            if (root.TryGetProperty("data", out var data))
                foreach (var page in data.EnumerateArray())
                {
                    var id = page.GetProperty("id").GetString() ?? "";
                    if (seenIds.Add(id)) // evita duplicados
                        pages.Add(new MetaPageDto(
                            id,
                            page.GetProperty("name").GetString() ?? "",
                            page.TryGetProperty("access_token", out var t) ? t.GetString() ?? "" : ""
                        ));
                }

            next = null;
            if (root.TryGetProperty("paging", out var paging) &&
                paging.TryGetProperty("next", out var nextProp))
                next = nextProp.GetString();
        }
    }
}

public record GetPagesRequest(string ShortLivedToken);
public record MetaPageDto(string PageId, string Name, string PageAccessToken);
public record MetaFormDto(string FormId, string Name, string Status);
