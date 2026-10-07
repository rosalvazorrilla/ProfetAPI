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
    private readonly IDealImportService _dealImport;
    private readonly IImportTemplateService _templates;

    public LeadImportController(ApplicationDbContext db, ILeadImportService import, IDealImportService dealImport, IImportTemplateService templates)
    {
        _db = db;
        _import = import;
        _dealImport = dealImport;
        _templates = templates;
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
    [SwaggerOperation(Summary = "Campos disponibles para mapear (prospectos u oportunidades)")]
    public IActionResult GetFields([FromQuery] string? entity = null) =>
        Ok(DealImportFields.For(entity).Select(f => new { key = f, label = DealImportFields.LabelsFor(entity)[f] }));

    // GET /api/leads/import/template?entity=leads|deals&accountId=&format=xlsx|csv  — archivo de ejemplo con instrucciones y catálogos
    [HttpGet("template")]
    [SwaggerOperation(Summary = "Descargar plantilla de ejemplo con instrucciones y catálogos")]
    public async Task<IActionResult> Template([FromQuery] string entity = "leads", [FromQuery] int? accountId = null, [FromQuery] string format = "xlsx")
    {
        if (entity != "leads" && entity != "deals") return BadRequest(new { message = "entity debe ser leads o deals." });
        var acId = await ResolveAccountId(accountId);
        var (bytes, contentType, fileName) = await _templates.BuildAsync(entity, acId, format == "csv" ? "csv" : "xlsx");
        return File(bytes, contentType, fileName);
    }

    // POST /api/leads/import/deals/validate  — revisión previa de oportunidades (no guarda nada)
    [HttpPost("deals/validate")]
    [SwaggerOperation(Summary = "Validar filas de oportunidades antes de importar")]
    public async Task<IActionResult> ValidateDeals([FromQuery] int? accountId, [FromBody] ValidateImportRequestDto req)
    {
        var acId = await ResolveAccountId(accountId ?? req.AccountId);
        if (acId == null) return NotFound(new { message = "Sin cuenta asignada." });
        if (req.Rows.Count > 2000) return BadRequest(new { message = "Máximo 2000 filas por importación." });
        return Ok(await _dealImport.ValidateAsync(req, acId.Value));
    }

    // POST /api/leads/import/deals/commit  — crea las oportunidades con el mapeo confirmado
    [HttpPost("deals/commit")]
    [SwaggerOperation(Summary = "Importar oportunidades (transaccional, con deduplicación)")]
    public async Task<IActionResult> CommitDeals([FromQuery] int? accountId, [FromBody] CommitImportRequestDto req)
    {
        var acId = await ResolveAccountId(accountId ?? req.AccountId);
        if (acId == null) return NotFound(new { message = "Sin cuenta asignada." });
        if (req.Rows.Count == 0) return BadRequest(new { message = "No hay filas para importar." });
        if (req.Rows.Count > 2000) return BadRequest(new { message = "Máximo 2000 filas por importación." });
        return Ok(await _dealImport.CommitAsync(req, acId.Value, UserId));
    }

    // POST /api/leads/import/validate  — revisa fila por fila qué fallaría (no guarda nada)
    [HttpPost("validate")]
    [SwaggerOperation(Summary = "Validar filas antes de importar y listar los errores por fila")]
    public async Task<IActionResult> Validate([FromQuery] int? accountId, [FromBody] ValidateImportRequestDto req)
    {
        var acId = await ResolveAccountId(accountId ?? req.AccountId);
        if (acId == null) return NotFound(new { message = "Sin cuenta asignada." });
        if (req.Rows.Count > 2000) return BadRequest(new { message = "Máximo 2000 filas por importación." });
        return Ok(await _import.ValidateAsync(req, acId.Value));
    }

    // POST /api/leads/import/suggest-fixes  — IA propone la corrección de cada problema (el usuario decide)
    [HttpPost("suggest-fixes")]
    [SwaggerOperation(Summary = "Sugerir correcciones de filas con error (IA)")]
    public async Task<IActionResult> SuggestFixes([FromBody] SuggestFixesRequestDto req) =>
        Ok(await _import.SuggestFixesAsync(req));

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
