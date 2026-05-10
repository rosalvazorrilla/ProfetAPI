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
[SwaggerTag("Catálogo Global — Templates de Motivos de Pérdida")]
public class LeadLostReasonsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    public LeadLostReasonsController(ApplicationDbContext context) => _context = context;

    [HttpGet]
    [SwaggerOperation(Summary = "Listar templates de motivos de pérdida")]
    [SwaggerResponse(200, "Lista de templates", typeof(List<LeadLostReasonResponseDto>))]
    public async Task<IActionResult> GetAll([FromQuery] bool soloActivos = false)
    {
        var query = _context.LeadLostReasonTemplates.AsQueryable();
        if (soloActivos) query = query.Where(r => r.IsActive);

        var list = await query
            .Select(r => new LeadLostReasonResponseDto { TemplateId = r.TemplateId, Description = r.Description, CountsForCharts = r.CountsForCharts, IsActive = r.IsActive })
            .ToListAsync();
        return Ok(list);
    }

    [HttpGet("{id}")]
    [SwaggerOperation(Summary = "Obtener template por ID")]
    [SwaggerResponse(200, "Template encontrado", typeof(LeadLostReasonResponseDto))]
    [SwaggerResponse(404, "No encontrado")]
    public async Task<IActionResult> GetById(int id)
    {
        var r = await _context.LeadLostReasonTemplates.FindAsync(id);
        if (r == null) return NotFound(new { message = "Template no encontrado." });
        return Ok(new LeadLostReasonResponseDto { TemplateId = r.TemplateId, Description = r.Description, CountsForCharts = r.CountsForCharts, IsActive = r.IsActive });
    }

    [HttpPost]
    [SwaggerOperation(Summary = "Crear template de motivo de pérdida")]
    [SwaggerResponse(201, "Template creado", typeof(LeadLostReasonResponseDto))]
    public async Task<IActionResult> Create([FromBody] CreateLeadLostReasonDto model)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var template = new LeadLostReasonTemplate { Description = model.Description, CountsForCharts = model.CountsForCharts };
        _context.LeadLostReasonTemplates.Add(template);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = template.TemplateId },
            new LeadLostReasonResponseDto { TemplateId = template.TemplateId, Description = template.Description, CountsForCharts = template.CountsForCharts, IsActive = template.IsActive });
    }

    [HttpPut("{id}")]
    [SwaggerOperation(Summary = "Actualizar template de motivo de pérdida")]
    [SwaggerResponse(200, "Template actualizado", typeof(LeadLostReasonResponseDto))]
    [SwaggerResponse(404, "No encontrado")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateLeadLostReasonDto model)
    {
        var template = await _context.LeadLostReasonTemplates.FindAsync(id);
        if (template == null) return NotFound(new { message = "Template no encontrado." });

        template.Description = model.Description;
        template.CountsForCharts = model.CountsForCharts;
        template.IsActive = model.IsActive;
        await _context.SaveChangesAsync();

        return Ok(new LeadLostReasonResponseDto { TemplateId = template.TemplateId, Description = template.Description, CountsForCharts = template.CountsForCharts, IsActive = template.IsActive });
    }

    [HttpDelete("{id}")]
    [SwaggerOperation(Summary = "Eliminar template")]
    [SwaggerResponse(204, "Eliminado")]
    [SwaggerResponse(404, "No encontrado")]
    public async Task<IActionResult> Delete(int id)
    {
        var template = await _context.LeadLostReasonTemplates.FindAsync(id);
        if (template == null) return NotFound(new { message = "Template no encontrado." });

        template.IsActive = false;
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
