using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Dtos.Admin;
using ProfetAPI.Models;
using Swashbuckle.AspNetCore.Annotations;

namespace ProfetAPI.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "AdminGlobal")]
[SwaggerTag("Catálogo Global — Fuentes de Prospectos")]
public class ProspectSourcesController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    public ProspectSourcesController(ApplicationDbContext context) => _context = context;

    // GET: api/prospectsources
    [HttpGet]
    [SwaggerOperation(Summary = "Listar fuentes de prospectos", Description = "Devuelve las fuentes globales (Facebook, Google, Referido, etc.).")]
    [SwaggerResponse(200, "Lista de fuentes", typeof(List<ProspectSourceResponseDto>))]
    public async Task<IActionResult> GetAll()
    {
        var list = await _context.ProspectSources
            .Select(s => new ProspectSourceResponseDto { SourceId = s.SourceId, Name = s.Name })
            .ToListAsync();
        return Ok(list);
    }

    // GET: api/prospectsources/5
    [HttpGet("{id}")]
    [SwaggerOperation(Summary = "Obtener fuente por ID")]
    [SwaggerResponse(200, "Fuente encontrada", typeof(ProspectSourceResponseDto))]
    [SwaggerResponse(404, "No encontrada")]
    public async Task<IActionResult> GetById(int id)
    {
        var s = await _context.ProspectSources.FindAsync(id);
        if (s == null) return NotFound(new { message = "Fuente no encontrada." });
        return Ok(new ProspectSourceResponseDto { SourceId = s.SourceId, Name = s.Name });
    }

    // POST: api/prospectsources
    [HttpPost]
    [SwaggerOperation(Summary = "Crear fuente de prospectos")]
    [SwaggerResponse(201, "Fuente creada", typeof(ProspectSourceResponseDto))]
    [SwaggerResponse(400, "Datos inválidos")]
    public async Task<IActionResult> Create([FromBody] UpsertProspectSourceDto model)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var source = new ProspectSource { Name = model.Name };
        _context.ProspectSources.Add(source);
        await _context.SaveChangesAsync();

        var response = new ProspectSourceResponseDto { SourceId = source.SourceId, Name = source.Name };
        return CreatedAtAction(nameof(GetById), new { id = source.SourceId }, response);
    }

    // PUT: api/prospectsources/5
    [HttpPut("{id}")]
    [SwaggerOperation(Summary = "Actualizar fuente de prospectos")]
    [SwaggerResponse(200, "Actualizada", typeof(ProspectSourceResponseDto))]
    [SwaggerResponse(404, "No encontrada")]
    public async Task<IActionResult> Update(int id, [FromBody] UpsertProspectSourceDto model)
    {
        var source = await _context.ProspectSources.FindAsync(id);
        if (source == null) return NotFound(new { message = "Fuente no encontrada." });

        source.Name = model.Name;
        await _context.SaveChangesAsync();
        return Ok(new ProspectSourceResponseDto { SourceId = source.SourceId, Name = source.Name });
    }

    // DELETE: api/prospectsources/5
    [HttpDelete("{id}")]
    [SwaggerOperation(Summary = "Eliminar fuente de prospectos")]
    [SwaggerResponse(204, "Eliminada")]
    [SwaggerResponse(404, "No encontrada")]
    public async Task<IActionResult> Delete(int id)
    {
        var source = await _context.ProspectSources.FindAsync(id);
        if (source == null) return NotFound(new { message = "Fuente no encontrada." });

        _context.ProspectSources.Remove(source);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
