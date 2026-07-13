using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using Swashbuckle.AspNetCore.Annotations;
using System.Security.Claims;

namespace ProfetAPI.Controllers;

[Route("api/notifications")]
[ApiController]
[Authorize]
[SwaggerTag("Notificaciones in-app del usuario")]
public class NotificationsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    public NotificationsController(ApplicationDbContext db) => _db = db;

    private string? UserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    // GET /api/notifications?onlyUnread=false&page=1
    [HttpGet]
    [SwaggerOperation(Summary = "Listar mis notificaciones")]
    public async Task<IActionResult> List([FromQuery] bool onlyUnread = false, [FromQuery] int page = 1)
    {
        const int pageSize = 30;
        var q = _db.Notifications.AsNoTracking().Where(n => n.UserId == UserId);
        if (onlyUnread) q = q.Where(n => n.Status != true);

        var items = await q.OrderByDescending(n => n.Date)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(n => new
            {
                n.Id, n.Message, n.URL, read = n.Status == true, n.Date, n.EntityType, n.EntityId,
                icon = n.Type != null ? n.Type.Icon : null,
            })
            .ToListAsync();

        var unread = await _db.Notifications.CountAsync(n => n.UserId == UserId && n.Status != true);
        return Ok(new { items, unread });
    }

    // GET /api/notifications/unread-count
    [HttpGet("unread-count")]
    [SwaggerOperation(Summary = "Cantidad de notificaciones sin leer")]
    public async Task<IActionResult> UnreadCount()
    {
        var unread = await _db.Notifications.CountAsync(n => n.UserId == UserId && n.Status != true);
        return Ok(new { unread });
    }

    // POST /api/notifications/{id}/read
    [HttpPost("{id:int}/read")]
    [SwaggerOperation(Summary = "Marcar una notificación como leída")]
    public async Task<IActionResult> MarkRead(int id)
    {
        var n = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
        if (n == null) return NotFound();
        n.Status = true;
        await _db.SaveChangesAsync();
        return Ok(new { read = true });
    }

    // POST /api/notifications/read-all
    [HttpPost("read-all")]
    [SwaggerOperation(Summary = "Marcar todas como leídas")]
    public async Task<IActionResult> MarkAllRead()
    {
        var unread = await _db.Notifications.Where(n => n.UserId == UserId && n.Status != true).ToListAsync();
        foreach (var n in unread) n.Status = true;
        await _db.SaveChangesAsync();
        return Ok(new { marked = unread.Count });
    }
}
