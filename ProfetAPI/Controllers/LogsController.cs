using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using Swashbuckle.AspNetCore.Annotations;

namespace ProfetAPI.Controllers;

/// <summary>Visor de logs internos del sistema — solo AdminGlobal.</summary>
[Route("api/logs")]
[ApiController]
[Authorize(Roles = "AdminGlobal")]
[SwaggerTag("Logs internos (solo Admin)")]
public class LogsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    public LogsController(ApplicationDbContext db) => _db = db;

    // GET /api/logs?search=&type=&dateFrom=&dateTo=&page=&pageSize=
    [HttpGet]
    [SwaggerOperation(Summary = "Listar logs con filtros y paginación")]
    public async Task<IActionResult> GetLogs(
        [FromQuery] string? search,
        [FromQuery] string? type,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(page, 1);

        var q = _db.Logs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(type))
            q = q.Where(l => l.Type == type);
        if (dateFrom.HasValue)
            q = q.Where(l => l.Date >= dateFrom.Value);
        if (dateTo.HasValue)
            q = q.Where(l => l.Date <= dateTo.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(l => l.Name.Contains(s) || l.Message.Contains(s));
        }

        var total = await q.CountAsync();

        var data = await q
            .OrderByDescending(l => l.Date)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new { l.Id, l.Date, l.Name, l.Message, l.Type })
            .ToListAsync();

        return Ok(new { total, page, pageSize, data });
    }

    // GET /api/logs/types
    [HttpGet("types")]
    [SwaggerOperation(Summary = "Valores distintos de Type para el filtro")]
    public async Task<IActionResult> GetTypes()
    {
        var types = await _db.Logs.AsNoTracking()
            .Where(l => l.Type != null && l.Type != "")
            .Select(l => l.Type!)
            .Distinct()
            .OrderBy(t => t)
            .Take(100)
            .ToListAsync();
        return Ok(types);
    }
}
