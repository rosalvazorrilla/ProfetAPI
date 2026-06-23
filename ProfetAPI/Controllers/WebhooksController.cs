using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Models;
using Swashbuckle.AspNetCore.Annotations;
using System.Security.Claims;

namespace ProfetAPI.Controllers;

[Route("api/webhooks")]
[ApiController]
[Authorize]
[SwaggerTag("CRM — Webhooks entrantes y salientes por cuenta")]
public class WebhooksController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public WebhooksController(ApplicationDbContext db) => _db = db;

    private string? UserId   => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private string? UserRole => User.FindFirstValue(ClaimTypes.Role);
    private bool    IsAdmin  => UserRole == "AdminGlobal";

    private async Task<int?> ResolveAccountId(int? accountId)
    {
        if (IsAdmin && accountId.HasValue) return accountId;
        if (!IsAdmin)
            return await _db.AccountInternalUsers
                .Where(u => u.UserId == UserId)
                .Select(u => (int?)u.AccountId)
                .FirstOrDefaultAsync();
        return accountId;
    }

    // ── GET /api/webhooks ──────────────────────────────────────────────────────

    [HttpGet]
    [SwaggerOperation(Summary = "Listar webhooks de la cuenta")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetAll([FromQuery] int? accountId, [FromQuery] string? direction)
    {
        var resolved = await ResolveAccountId(accountId);
        if (resolved == null) return BadRequest("No se pudo determinar la cuenta.");

        var q = _db.AccountWebhooks.Where(w => w.AccountId == resolved);
        if (!string.IsNullOrEmpty(direction))
            q = q.Where(w => w.Direction == direction);

        var items = await q
            .OrderByDescending(w => w.CreatedAt)
            .Select(w => new
            {
                w.WebhookId,
                w.AccountId,
                w.Name,
                w.Direction,
                w.Platform,
                w.ActionType,
                w.WebhookKey,
                w.TriggerEvent,
                w.TargetUrl,
                w.IsActive,
                w.MetaAppId,
                w.MetaPageId,
                w.MetaVerifyToken,
                w.DestFunnelId,
                w.DestLeadStatus,
                HasMetaSecret  = w.MetaAppSecret != null,
                HasPageToken   = w.MetaPageAccessToken != null,
                HasOutSecret   = w.OutgoingSecret != null,
                w.CreatedAt,
                w.LastTriggeredAt,
                w.TriggerCount,
                w.LastError,
            })
            .ToListAsync();

        return Ok(items);
    }

    // ── GET /api/webhooks/{id} ─────────────────────────────────────────────────

    [HttpGet("{id:int}")]
    [SwaggerOperation(Summary = "Detalle de un webhook")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetOne(int id, [FromQuery] int? accountId)
    {
        var resolved = await ResolveAccountId(accountId);
        if (resolved == null) return BadRequest();

        var w = await _db.AccountWebhooks
            .FirstOrDefaultAsync(x => x.WebhookId == id && x.AccountId == resolved);

        if (w == null) return NotFound();
        return Ok(ToDto(w));
    }

    // ── POST /api/webhooks ─────────────────────────────────────────────────────

    [HttpPost]
    [SwaggerOperation(Summary = "Crear webhook (entrante o saliente)")]
    [ProducesResponseType(201)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> Create([FromQuery] int? accountId, [FromBody] SaveWebhookRequest req)
    {
        var resolved = await ResolveAccountId(accountId);
        if (resolved == null) return BadRequest("No se pudo determinar la cuenta.");

        if (string.IsNullOrWhiteSpace(req.Name))      return BadRequest("Name es requerido.");
        if (string.IsNullOrWhiteSpace(req.Direction))  return BadRequest("Direction es requerido.");

        var wh = BuildWebhook(req, resolved.Value);
        _db.AccountWebhooks.Add(wh);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetOne), new { id = wh.WebhookId },
            new { wh.WebhookId, wh.WebhookKey, wh.MetaVerifyToken, wh.Direction });
    }

    // ── PUT /api/webhooks/{id} ─────────────────────────────────────────────────

    [HttpPut("{id:int}")]
    [SwaggerOperation(Summary = "Actualizar webhook")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Update(int id, [FromQuery] int? accountId, [FromBody] SaveWebhookRequest req)
    {
        var resolved = await ResolveAccountId(accountId);
        if (resolved == null) return BadRequest();

        var wh = await _db.AccountWebhooks
            .FirstOrDefaultAsync(x => x.WebhookId == id && x.AccountId == resolved);
        if (wh == null) return NotFound();

        ApplyUpdate(wh, req);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── DELETE /api/webhooks/{id} ──────────────────────────────────────────────

    [HttpDelete("{id:int}")]
    [SwaggerOperation(Summary = "Eliminar webhook")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Delete(int id, [FromQuery] int? accountId)
    {
        var resolved = await ResolveAccountId(accountId);
        if (resolved == null) return BadRequest();

        var wh = await _db.AccountWebhooks
            .FirstOrDefaultAsync(x => x.WebhookId == id && x.AccountId == resolved);
        if (wh == null) return NotFound();

        _db.AccountWebhooks.Remove(wh);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── POST /api/webhooks/{id}/regenerate-key ─────────────────────────────────

    [HttpPost("{id:int}/regenerate-key")]
    [SwaggerOperation(Summary = "Regenerar URL key del webhook entrante")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> RegenerateKey(int id, [FromQuery] int? accountId)
    {
        var resolved = await ResolveAccountId(accountId);
        if (resolved == null) return BadRequest();

        var wh = await _db.AccountWebhooks
            .FirstOrDefaultAsync(x => x.WebhookId == id && x.AccountId == resolved);
        if (wh == null) return NotFound();

        wh.WebhookKey = Guid.NewGuid().ToString("N");
        await _db.SaveChangesAsync();
        return Ok(new { wh.WebhookKey });
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static AccountWebhook BuildWebhook(SaveWebhookRequest r, int accountId) => new()
    {
        AccountId            = accountId,
        Name                 = r.Name.Trim(),
        Direction            = r.Direction,
        IsActive             = r.IsActive,
        // Incoming
        Platform             = r.Platform?.Trim(),
        ActionType           = r.ActionType?.Trim() ?? "CreateLead",
        WebhookKey           = r.Direction == "Incoming" ? Guid.NewGuid().ToString("N") : null,
        MetaAppId            = r.MetaAppId?.Trim(),
        MetaAppSecret        = r.MetaAppSecret?.Trim(),
        MetaVerifyToken      = r.MetaVerifyToken?.Trim() ?? (r.Direction == "Incoming" ? Guid.NewGuid().ToString("N") : null),
        MetaPageAccessToken  = r.MetaPageAccessToken?.Trim(),
        MetaPageId           = r.MetaPageId?.Trim(),
        DestFunnelId         = r.DestFunnelId,
        DestLeadStatus       = r.DestLeadStatus ?? "Nuevo",
        // Outgoing
        TriggerEvent         = r.TriggerEvent?.Trim(),
        TargetUrl            = r.TargetUrl?.Trim(),
        OutgoingSecret       = r.OutgoingSecret?.Trim(),
        CreatedAt            = DateTime.UtcNow,
    };

    private static void ApplyUpdate(AccountWebhook wh, SaveWebhookRequest r)
    {
        if (!string.IsNullOrWhiteSpace(r.Name))  wh.Name     = r.Name.Trim();
        wh.IsActive = r.IsActive;

        if (r.Platform    != null) wh.Platform    = r.Platform.Trim();
        if (r.ActionType  != null) wh.ActionType  = r.ActionType.Trim();
        if (r.DestFunnelId.HasValue) wh.DestFunnelId  = r.DestFunnelId;
        if (r.DestLeadStatus != null) wh.DestLeadStatus = r.DestLeadStatus;

        if (r.MetaAppId            != null) wh.MetaAppId            = r.MetaAppId.Trim();
        if (r.MetaAppSecret        != null) wh.MetaAppSecret        = r.MetaAppSecret.Trim();
        if (r.MetaVerifyToken      != null) wh.MetaVerifyToken      = r.MetaVerifyToken.Trim();
        if (r.MetaPageAccessToken  != null) wh.MetaPageAccessToken  = r.MetaPageAccessToken.Trim();
        if (r.MetaPageId           != null) wh.MetaPageId           = r.MetaPageId.Trim();

        if (r.TriggerEvent   != null) wh.TriggerEvent   = r.TriggerEvent.Trim();
        if (r.TargetUrl      != null) wh.TargetUrl      = r.TargetUrl.Trim();
        if (r.OutgoingSecret != null) wh.OutgoingSecret = r.OutgoingSecret.Trim();
    }

    private static object ToDto(AccountWebhook w) => new
    {
        w.WebhookId, w.AccountId, w.Name, w.Direction, w.Platform, w.ActionType,
        w.WebhookKey, w.MetaAppId, w.MetaPageId, w.MetaVerifyToken,
        w.DestFunnelId, w.DestLeadStatus,
        w.TriggerEvent, w.TargetUrl,
        w.IsActive, w.CreatedAt, w.LastTriggeredAt, w.TriggerCount, w.LastError,
        HasMetaSecret = w.MetaAppSecret != null,
        HasPageToken  = w.MetaPageAccessToken != null,
        HasOutSecret  = w.OutgoingSecret != null,
    };
}

// ── DTOs ───────────────────────────────────────────────────────────────────────

public record SaveWebhookRequest(
    string  Name,
    string  Direction,
    bool    IsActive            = true,
    // Incoming
    string? Platform            = null,
    string? ActionType          = "CreateLead",
    string? MetaAppId           = null,
    string? MetaAppSecret       = null,
    string? MetaVerifyToken     = null,
    string? MetaPageAccessToken = null,
    string? MetaPageId          = null,
    int?    DestFunnelId        = null,
    string? DestLeadStatus      = "Nuevo",
    // Outgoing
    string? TriggerEvent        = null,
    string? TargetUrl           = null,
    string? OutgoingSecret      = null
);
