using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Dtos.Leads;
using ProfetAPI.Services;
using Swashbuckle.AspNetCore.Annotations;
using System.Security.Claims;

namespace ProfetAPI.Controllers;

[Route("api/leads/import")]
[ApiController]
[Authorize]
[SwaggerTag("Prospectos — Importación desde CSV/Excel con ayuda de IA")]
public class LeadImportController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILeadImportService _import;

    public LeadImportController(ApplicationDbContext db, ILeadImportService import)
    {
        _db = db;
        _import = import;
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

    // POST /api/leads/import/upload  — sube el archivo, solo lo parsea (no guarda nada)
    [HttpPost("upload")]
    [SwaggerOperation(Summary = "Subir y parsear un CSV/Excel de prospectos (sin persistir)")]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest(new { message = "Sube un archivo .csv o .xlsx." });
        try
        {
            using var stream = file.OpenReadStream();
            var parsed = await _import.ParseFileAsync(stream, file.FileName);
            if (parsed.Columns.Count == 0) return BadRequest(new { message = "No se detectaron columnas en el archivo." });
            return Ok(parsed);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // POST /api/leads/import/suggest-mapping  — IA sugiere a qué campo corresponde cada columna
    [HttpPost("suggest-mapping")]
    [SwaggerOperation(Summary = "Sugerir el mapeo de columnas a campos del prospecto (IA)")]
    public async Task<IActionResult> SuggestMapping([FromBody] SuggestMappingRequestDto req)
    {
        var result = await _import.SuggestMappingAsync(req);
        return Ok(result);
    }

    // GET /api/leads/import/fields  — catálogo de campos disponibles para mapear
    [HttpGet("fields")]
    [SwaggerOperation(Summary = "Campos del prospecto disponibles para mapear")]
    public IActionResult GetFields() =>
        Ok(LeadImportFields.All.Select(f => new { key = f, label = LeadImportFields.Labels[f] }));

    // POST /api/leads/import/commit  — crea los leads con el mapeo confirmado
    [HttpPost("commit")]
    [SwaggerOperation(Summary = "Ejecutar la importación (transaccional, con deduplicación)")]
    public async Task<IActionResult> Commit([FromQuery] int? accountId, [FromBody] CommitImportRequestDto req)
    {
        var acId = await ResolveAccountId(accountId ?? req.AccountId);
        if (acId == null) return NotFound(new { message = "Sin cuenta asignada." });
        if (req.Rows.Count == 0) return BadRequest(new { message = "No hay filas para importar." });
        if (req.Rows.Count > 2000) return BadRequest(new { message = "Máximo 2000 filas por importación." });

        var result = await _import.CommitAsync(req, acId.Value, UserId);
        return Ok(result);
    }
}
