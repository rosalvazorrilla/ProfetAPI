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
[SwaggerTag("Catálogo Global — Motivos de Pérdida")]
public class LeadLostReasonsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    public LeadLostReasonsController(ApplicationDbContext context) => _context = context;

    // GET: api/leadlostreasons
    [HttpGet]
    [SwaggerOperation(Summary = "Listar motivos de pérdida", Description = "Devuelve el catálogo global. Los Accounts seleccionan cuáles aplican para ellos.")]
    [SwaggerResponse(200, "Lista de motivos", typeof(List<LeadLostReasonResponseDto>))]
    public async Task<IActionResult> GetAll([FromQuery] bool soloActivos = false)
    {
        var query = _context.LeadLostReasons.AsQueryable();
        if (soloActivos) query = query.Where(r => r.IsActive);

        var list = await query
            .Select(r => new LeadLostReasonResponseDto { Id = r.Id, Description = r.Description, ConversionRate = r.ConversionRate, IsActive = r.IsActive })
            .ToListAsync();
        return Ok(list);
    }

    // GET: api/leadlostreasons/5
    [HttpGet("{id}")]
    [SwaggerOperation(Summary = "Obtener motivo por ID")]
    [SwaggerResponse(200, "Motivo encontrado", typeof(LeadLostReasonResponseDto))]
    [SwaggerResponse(404, "No encontrado")]
    public async Task<IActionResult> GetById(int id)
    {
        var r = await _context.LeadLostReasons.FindAsync(id);
        if (r == null) return NotFound(new { message = "Motivo no encontrado." });
        return Ok(new LeadLostReasonResponseDto { Id = r.Id, Description = r.Description, ConversionRate = r.ConversionRate, IsActive = r.IsActive });
    }

    // POST: api/leadlostreasons
    [HttpPost]
    [SwaggerOperation(Summary = "Crear motivo de pérdida", Description = "Agrega un nuevo motivo al catálogo global.")]
    [SwaggerResponse(201, "Motivo creado", typeof(LeadLostReasonResponseDto))]
    [SwaggerResponse(400, "Datos inválidos")]
    public async Task<IActionResult> Create([FromBody] CreateLeadLostReasonDto model)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var reason = new LeadLostReason { Description = model.Description, ConversionRate = model.ConversionRate };
        _context.LeadLostReasons.Add(reason);
        await _context.SaveChangesAsync();

        var response = new LeadLostReasonResponseDto { Id = reason.Id, Description = reason.Description, ConversionRate = reason.ConversionRate, IsActive = reason.IsActive };
        return CreatedAtAction(nameof(GetById), new { id = reason.Id }, response);
    }

    // PUT: api/leadlostreasons/5
    [HttpPut("{id}")]
    [SwaggerOperation(Summary = "Actualizar motivo de pérdida")]
    [SwaggerResponse(200, "Motivo actualizado", typeof(LeadLostReasonResponseDto))]
    [SwaggerResponse(404, "No encontrado")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateLeadLostReasonDto model)
    {
        var reason = await _context.LeadLostReasons.FindAsync(id);
        if (reason == null) return NotFound(new { message = "Motivo no encontrado." });

        reason.Description = model.Description;
        reason.ConversionRate = model.ConversionRate;
        reason.IsActive = model.IsActive;
        await _context.SaveChangesAsync();

        return Ok(new LeadLostReasonResponseDto { Id = reason.Id, Description = reason.Description, ConversionRate = reason.ConversionRate, IsActive = reason.IsActive });
    }

    // DELETE: api/leadlostreasons/5
    [HttpDelete("{id}")]
    [SwaggerOperation(Summary = "Eliminar motivo del catálogo global")]
    [SwaggerResponse(204, "Eliminado")]
    [SwaggerResponse(404, "No encontrado")]
    public async Task<IActionResult> Delete(int id)
    {
        var reason = await _context.LeadLostReasons.FindAsync(id);
        if (reason == null) return NotFound(new { message = "Motivo no encontrado." });

        _context.LeadLostReasons.Remove(reason);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
