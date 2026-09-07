using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using Swashbuckle.AspNetCore.Annotations;

namespace ProfetAPI.Controllers;

// ── Auditoría de envíos automáticos de secuencia (éxitos y fallas) ──────────

[Route("api/automation-send-logs")]
[ApiController]
[Authorize]
[SwaggerTag("CRM — Historial de envíos automáticos")]
public class AutomationSendLogsController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    private string? UserId   => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    private string? UserRole => User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
    private bool IsAdmin     => UserRole == "AdminGlobal";

    public AutomationSendLogsController(ApplicationDbContext db) => _db = db;

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

    // GET /api/automation-send-logs?accountId=&success=&page=
    [HttpGet]
    [SwaggerOperation(Summary = "Listar los envíos automáticos de la cuenta (éxitos y fallas)")]
    public async Task<IActionResult> List([FromQuery] int? accountId, [FromQuery] bool? success, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound(new { message = "Sin cuenta asignada." });

        var q = _db.AutomationSendLogs.Where(l => l.AccountId == acId);
        if (success.HasValue) q = q.Where(l => l.Success == success.Value);

        var total = await q.CountAsync();
        var failedCount = await _db.AutomationSendLogs.CountAsync(l => l.AccountId == acId && !l.Success);

        var data = await q
            .OrderByDescending(l => l.CreatedOn)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new
            {
                l.LogId, l.EntityType, l.EntityId, l.Channel, l.TemplateName,
                l.Success, l.Error, l.CreatedOn,
            })
            .ToListAsync();

        return Ok(new { total, failedCount, page, pageSize, data });
    }
}
