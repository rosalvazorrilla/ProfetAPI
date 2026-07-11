using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using Swashbuckle.AspNetCore.Annotations;

namespace ProfetAPI.Controllers;

[Route("api/account/meta-config")]
[ApiController]
[Authorize]
[SwaggerTag("CRM — Configuración de Meta Business (cuenta de anuncios)")]
public class AccountMetaController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public AccountMetaController(ApplicationDbContext db) => _db = db;

    private string? CurrentUserId   => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    private string? CurrentUserRole => User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
    private bool IsAdminGlobal      => CurrentUserRole == "AdminGlobal";

    private async Task<int?> ResolveAccountId(int? accountId)
    {
        if (IsAdminGlobal && accountId.HasValue) return accountId;
        if (!IsAdminGlobal)
            return await _db.AccountInternalUsers
                .Where(u => u.UserId == CurrentUserId)
                .Select(u => (int?)u.AccountId)
                .FirstOrDefaultAsync();
        return accountId;
    }

    // ── GET /api/account/meta-config ──────────────────────────────────────────
    [HttpGet]
    [SwaggerOperation(Summary = "Obtener configuración de Meta Business para la cuenta")]
    [SwaggerResponse(200, "Configuración actual")]
    public async Task<IActionResult> GetConfig([FromQuery] int? accountId)
    {
        var resolved = await ResolveAccountId(accountId);
        if (resolved == null) return NotFound(new { message = "Sin cuenta asignada." });

        var account = await _db.Accounts
            .AsNoTracking()
            .Where(a => a.AccountId == resolved)
            .Select(a => new { a.MetaAdAccountId })
            .FirstOrDefaultAsync();

        if (account == null) return NotFound();

        // Pages conectadas — extraídas de los webhooks de Meta
        var pages = await _db.AccountWebhooks
            .AsNoTracking()
            .Where(w => w.AccountId == resolved && w.Platform == "MetaLeadAds" && w.MetaPageId != null)
            .GroupBy(w => w.MetaPageId!)
            .Select(g => new
            {
                pageId   = g.Key,
                pageName = g.First().MetaPageName ?? g.Key,
                forms    = g.Select(w => new
                {
                    webhookId  = w.WebhookId,
                    formId     = w.MetaFormId,
                    formName   = w.MetaFormName,
                    isActive   = w.IsActive,
                    hasToken   = w.MetaPageAccessToken != null,
                }).ToList(),
            })
            .ToListAsync();

        return Ok(new
        {
            metaAdAccountId = account.MetaAdAccountId,
            connectedPages  = pages,
        });
    }

    // ── PUT /api/account/meta-config ──────────────────────────────────────────
    [HttpPut]
    [SwaggerOperation(Summary = "Guardar el ID de cuenta de anuncios de Meta")]
    [SwaggerResponse(200, "Guardado exitoso")]
    public async Task<IActionResult> SaveConfig([FromQuery] int? accountId, [FromBody] MetaConfigRequest req)
    {
        var resolved = await ResolveAccountId(accountId);
        if (resolved == null) return NotFound(new { message = "Sin cuenta asignada." });

        var account = await _db.Accounts.FindAsync(resolved);
        if (account == null) return NotFound();

        // Limpiar el prefijo "act_" si el usuario lo pegó completo
        var id = req.MetaAdAccountId?.Trim().TrimStart('a', 'c', 't', '_') ?? "";
        account.MetaAdAccountId = string.IsNullOrWhiteSpace(id) ? null : id;
        await _db.SaveChangesAsync();

        return Ok(new { saved = true, metaAdAccountId = account.MetaAdAccountId });
    }
}

public record MetaConfigRequest(string? MetaAdAccountId);
