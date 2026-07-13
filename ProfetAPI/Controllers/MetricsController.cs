using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Dtos.Metrics;
using ProfetAPI.Services.Metrics;
using Swashbuckle.AspNetCore.Annotations;
using System.Security.Claims;

namespace ProfetAPI.Controllers;

[Route("api/metrics")]
[ApiController]
[Authorize]
[SwaggerTag("Analítica — Catálogo de métricas y motor de consultas")]
public class MetricsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly MetricsCatalog _catalog;
    private readonly MetricsQueryService _engine;
    private readonly MetricsAiService _ai;

    public MetricsController(ApplicationDbContext db, MetricsCatalog catalog, MetricsQueryService engine, MetricsAiService ai)
    {
        _db = db; _catalog = catalog; _engine = engine; _ai = ai;
    }

    private string? UserId  => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    private bool    IsAdmin => User.FindFirst(ClaimTypes.Role)?.Value == "AdminGlobal";

    private async Task<int?> ResolveAccountId(int? accountId)
    {
        if (accountId.HasValue)
        {
            if (IsAdmin) return accountId;
            var ok = await _db.AccountInternalUsers.AnyAsync(u => u.AccountId == accountId && u.UserId == UserId);
            return ok ? accountId : null;
        }
        if (IsAdmin) return null;
        return await _db.AccountInternalUsers.Where(u => u.UserId == UserId)
            .Select(u => (int?)u.AccountId).FirstOrDefaultAsync();
    }

    // GET /api/metrics/catalog?accountId=
    [HttpGet("catalog")]
    [SwaggerOperation(Summary = "Catálogo de métricas disponible para la cuenta")]
    public async Task<IActionResult> GetCatalog([FromQuery] int? accountId)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound(new { message = "Sin cuenta." });
        return Ok(await _catalog.GetForAccountAsync(acId.Value));
    }

    // POST /api/metrics/query?accountId=
    [HttpPost("query")]
    [SwaggerOperation(Summary = "Ejecutar una consulta de métrica (validada contra el catálogo)")]
    [SwaggerResponse(200, "Serie agregada")]
    [SwaggerResponse(400, "Consulta inválida")]
    public async Task<IActionResult> Query([FromQuery] int? accountId, [FromBody] MetricQueryDto q)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound(new { message = "Sin cuenta." });

        if (!MetricsCatalog.IsValid(q, out var error))
            return BadRequest(new { message = error });

        var series = await _engine.RunAsync(q, acId.Value);
        return Ok(series);
    }

    // GET /api/metrics/suggestions?accountId=  — D2-T3: gráficas sugeridas por IA
    [HttpGet("suggestions")]
    [SwaggerOperation(Summary = "Gráficas sugeridas por IA (validadas contra el catálogo)")]
    public async Task<IActionResult> Suggestions([FromQuery] int? accountId)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound(new { message = "Sin cuenta." });
        if (!_ai.IsConfigured) return Ok(Array.Empty<object>());

        var items = await _ai.SuggestAsync(acId.Value);
        return Ok(items.Select(s => new { s.Query, s.Title, s.Reason }));
    }

    // POST /api/metrics/from-prompt?accountId=  — D2-T4: texto → gráfica
    [HttpPost("from-prompt")]
    [SwaggerOperation(Summary = "Generar una gráfica desde una descripción en lenguaje natural")]
    public async Task<IActionResult> FromPrompt([FromQuery] int? accountId, [FromBody] MetricPromptDto body)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound(new { message = "Sin cuenta." });
        if (!_ai.IsConfigured) return StatusCode(503, new { message = "La IA no está configurada." });
        if (string.IsNullOrWhiteSpace(body.Prompt)) return BadRequest(new { message = "Describe la gráfica." });

        var s = await _ai.FromPromptAsync(acId.Value, body.Prompt);
        if (s == null) return Ok(new { found = false, message = "No pude construir esa gráfica con los datos disponibles." });

        var series = await _engine.RunAsync(s.Query, acId.Value);
        return Ok(new { found = true, s.Query, s.Title, s.Reason, series });
    }

    // ── Reportes guardados (D2-T2) ────────────────────────────────────────────

    // GET /api/metrics/reports?accountId=
    [HttpGet("reports")]
    [SwaggerOperation(Summary = "Listar reportes guardados de la cuenta")]
    public async Task<IActionResult> ListReports([FromQuery] int? accountId)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound();
        var reports = await _db.SavedReports.AsNoTracking()
            .Where(r => r.AccountId == acId && !r.Deleted)
            .OrderByDescending(r => r.CreatedOn)
            .Select(r => new { r.Id, r.Name, r.LayoutJson, r.CreatedOn })
            .ToListAsync();
        return Ok(reports);
    }

    // POST /api/metrics/reports?accountId=
    [HttpPost("reports")]
    [SwaggerOperation(Summary = "Guardar un reporte")]
    public async Task<IActionResult> SaveReport([FromQuery] int? accountId, [FromBody] SaveReportDto dto)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound();
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { message = "El reporte necesita un nombre." });

        var report = new Models.SavedReport
        {
            AccountId = acId.Value, UserId = UserId, Name = dto.Name.Trim(),
            LayoutJson = dto.LayoutJson, CreatedOn = DateTime.UtcNow,
        };
        _db.SavedReports.Add(report);
        await _db.SaveChangesAsync();
        return Ok(new { report.Id });
    }

    // PUT /api/metrics/reports/{id}?accountId=
    [HttpPut("reports/{id:int}")]
    [SwaggerOperation(Summary = "Actualizar un reporte")]
    public async Task<IActionResult> UpdateReport(int id, [FromQuery] int? accountId, [FromBody] SaveReportDto dto)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound();
        var report = await _db.SavedReports.FirstOrDefaultAsync(r => r.Id == id && r.AccountId == acId && !r.Deleted);
        if (report == null) return NotFound();
        report.Name = dto.Name.Trim();
        report.LayoutJson = dto.LayoutJson;
        await _db.SaveChangesAsync();
        return Ok(new { report.Id });
    }

    // DELETE /api/metrics/reports/{id}?accountId=
    [HttpDelete("reports/{id:int}")]
    [SwaggerOperation(Summary = "Eliminar un reporte")]
    public async Task<IActionResult> DeleteReport(int id, [FromQuery] int? accountId)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound();
        var report = await _db.SavedReports.FirstOrDefaultAsync(r => r.Id == id && r.AccountId == acId && !r.Deleted);
        if (report == null) return NotFound();
        report.Deleted = true;
        await _db.SaveChangesAsync();
        return Ok(new { deleted = true });
    }
}

public class MetricPromptDto
{
    public string Prompt { get; set; } = "";
}

public class SaveReportDto
{
    public string Name       { get; set; } = "";
    public string LayoutJson { get; set; } = "[]";
}
