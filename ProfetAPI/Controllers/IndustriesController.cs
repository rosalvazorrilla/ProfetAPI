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
[SwaggerTag("Catálogo Global — Industrias")]
public class IndustriesController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    public IndustriesController(ApplicationDbContext context) => _context = context;

    // GET: api/industries
    [HttpGet]
    [SwaggerOperation(Summary = "Listar industrias", Description = "Devuelve todos los sectores de industria disponibles.")]
    [SwaggerResponse(200, "Lista de industrias", typeof(List<IndustryResponseDto>))]
    public async Task<IActionResult> GetAll()
    {
        var list = await _context.Industries
            .Select(i => new IndustryResponseDto { Id = i.Id, NameES = i.NameES, NameEN = i.NameEN })
            .ToListAsync();
        return Ok(list);
    }

    // GET: api/industries/5
    [HttpGet("{id}")]
    [SwaggerOperation(Summary = "Obtener industria por ID")]
    [SwaggerResponse(200, "Industria encontrada", typeof(IndustryResponseDto))]
    [SwaggerResponse(404, "No encontrada")]
    public async Task<IActionResult> GetById(long id)
    {
        var i = await _context.Industries.FindAsync(id);
        if (i == null) return NotFound(new { message = "Industria no encontrada." });
        return Ok(new IndustryResponseDto { Id = i.Id, NameES = i.NameES, NameEN = i.NameEN });
    }

    // POST: api/industries
    [HttpPost]
    [SwaggerOperation(Summary = "Crear industria", Description = "Agrega un nuevo sector al catálogo global.")]
    [SwaggerResponse(201, "Industria creada", typeof(IndustryResponseDto))]
    [SwaggerResponse(400, "Datos inválidos")]
    public async Task<IActionResult> Create([FromBody] UpsertIndustryDto model)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var industry = new Industry { NameES = model.NameES, NameEN = model.NameEN };
        _context.Industries.Add(industry);
        await _context.SaveChangesAsync();

        var response = new IndustryResponseDto { Id = industry.Id, NameES = industry.NameES, NameEN = industry.NameEN };
        return CreatedAtAction(nameof(GetById), new { id = industry.Id }, response);
    }

    // PUT: api/industries/5
    [HttpPut("{id}")]
    [SwaggerOperation(Summary = "Actualizar industria")]
    [SwaggerResponse(200, "Industria actualizada", typeof(IndustryResponseDto))]
    [SwaggerResponse(404, "No encontrada")]
    public async Task<IActionResult> Update(long id, [FromBody] UpsertIndustryDto model)
    {
        var industry = await _context.Industries.FindAsync(id);
        if (industry == null) return NotFound(new { message = "Industria no encontrada." });

        industry.NameES = model.NameES;
        industry.NameEN = model.NameEN;
        await _context.SaveChangesAsync();

        return Ok(new IndustryResponseDto { Id = industry.Id, NameES = industry.NameES, NameEN = industry.NameEN });
    }

    // DELETE: api/industries/5
    [HttpDelete("{id}")]
    [SwaggerOperation(Summary = "Eliminar industria")]
    [SwaggerResponse(204, "Eliminada correctamente")]
    [SwaggerResponse(404, "No encontrada")]
    public async Task<IActionResult> Delete(long id)
    {
        var industry = await _context.Industries.FindAsync(id);
        if (industry == null) return NotFound(new { message = "Industria no encontrada." });

        _context.Industries.Remove(industry);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
