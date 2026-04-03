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
[SwaggerTag("Catálogo Global — Plantillas de Embudos")]
public class FunnelTemplatesController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    public FunnelTemplatesController(ApplicationDbContext context) => _context = context;

    // GET: api/funneltemplates
    [HttpGet]
    [SwaggerOperation(Summary = "Listar plantillas de embudos", Description = "Devuelve todas las plantillas con sus etapas.")]
    [SwaggerResponse(200, "Lista de plantillas", typeof(List<FunnelTemplateResponseDto>))]
    public async Task<IActionResult> GetAll()
    {
        var list = await _context.FunnelTemplates
            .Include(t => t.Stages)
            .Select(t => new FunnelTemplateResponseDto
            {
                TemplateId = t.TemplateId,
                Name = t.Name,
                Description = t.Description,
                Stages = t.Stages.OrderBy(s => s.Order).Select(s => new FunnelTemplateStageResponseDto
                {
                    TemplateStageId = s.TemplateStageId,
                    StageName = s.StageName,
                    Order = s.Order
                }).ToList()
            }).ToListAsync();
        return Ok(list);
    }

    // GET: api/funneltemplates/5
    [HttpGet("{id}")]
    [SwaggerOperation(Summary = "Obtener plantilla por ID")]
    [SwaggerResponse(200, "Plantilla encontrada", typeof(FunnelTemplateResponseDto))]
    [SwaggerResponse(404, "No encontrada")]
    public async Task<IActionResult> GetById(int id)
    {
        var t = await _context.FunnelTemplates.Include(x => x.Stages).FirstOrDefaultAsync(x => x.TemplateId == id);
        if (t == null) return NotFound(new { message = "Plantilla no encontrada." });

        return Ok(new FunnelTemplateResponseDto
        {
            TemplateId = t.TemplateId, Name = t.Name, Description = t.Description,
            Stages = t.Stages.OrderBy(s => s.Order).Select(s => new FunnelTemplateStageResponseDto
            { TemplateStageId = s.TemplateStageId, StageName = s.StageName, Order = s.Order }).ToList()
        });
    }

    // POST: api/funneltemplates
    [HttpPost]
    [SwaggerOperation(Summary = "Crear plantilla de embudo", Description = "Crea la plantilla y sus etapas en una sola operación.")]
    [SwaggerResponse(201, "Plantilla creada", typeof(FunnelTemplateResponseDto))]
    [SwaggerResponse(400, "Datos inválidos")]
    public async Task<IActionResult> Create([FromBody] CreateFunnelTemplateDto model)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var template = new FunnelTemplate
        {
            Name = model.Name,
            Description = model.Description,
            Stages = model.Stages.Select(s => new FunnelTemplateStage { StageName = s.StageName, Order = s.Order }).ToList()
        };

        _context.FunnelTemplates.Add(template);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = template.TemplateId }, new FunnelTemplateResponseDto
        {
            TemplateId = template.TemplateId, Name = template.Name, Description = template.Description,
            Stages = template.Stages.Select(s => new FunnelTemplateStageResponseDto
            { TemplateStageId = s.TemplateStageId, StageName = s.StageName, Order = s.Order }).ToList()
        });
    }

    // PUT: api/funneltemplates/5
    [HttpPut("{id}")]
    [SwaggerOperation(Summary = "Actualizar nombre/descripción de plantilla")]
    [SwaggerResponse(200, "Actualizada", typeof(FunnelTemplateResponseDto))]
    [SwaggerResponse(404, "No encontrada")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateFunnelTemplateDto model)
    {
        var template = await _context.FunnelTemplates.Include(t => t.Stages).FirstOrDefaultAsync(t => t.TemplateId == id);
        if (template == null) return NotFound(new { message = "Plantilla no encontrada." });

        template.Name = model.Name;
        template.Description = model.Description;
        await _context.SaveChangesAsync();

        return Ok(new FunnelTemplateResponseDto
        {
            TemplateId = template.TemplateId, Name = template.Name, Description = template.Description,
            Stages = template.Stages.OrderBy(s => s.Order).Select(s => new FunnelTemplateStageResponseDto
            { TemplateStageId = s.TemplateStageId, StageName = s.StageName, Order = s.Order }).ToList()
        });
    }

    // DELETE: api/funneltemplates/5
    [HttpDelete("{id}")]
    [SwaggerOperation(Summary = "Eliminar plantilla", Description = "Elimina la plantilla y sus etapas en cascada.")]
    [SwaggerResponse(204, "Eliminada")]
    [SwaggerResponse(404, "No encontrada")]
    public async Task<IActionResult> Delete(int id)
    {
        var template = await _context.FunnelTemplates.Include(t => t.Stages).FirstOrDefaultAsync(t => t.TemplateId == id);
        if (template == null) return NotFound(new { message = "Plantilla no encontrada." });

        _context.FunnelTemplates.Remove(template);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // POST: api/funneltemplates/5/stages
    [HttpPost("{templateId}/stages")]
    [SwaggerOperation(Summary = "Agregar etapa a una plantilla")]
    [SwaggerResponse(201, "Etapa agregada", typeof(FunnelTemplateStageResponseDto))]
    [SwaggerResponse(404, "Plantilla no encontrada")]
    public async Task<IActionResult> AddStage(int templateId, [FromBody] UpsertFunnelTemplateStageDto model)
    {
        var template = await _context.FunnelTemplates.FindAsync(templateId);
        if (template == null) return NotFound(new { message = "Plantilla no encontrada." });

        var stage = new FunnelTemplateStage { TemplateId = templateId, StageName = model.StageName, Order = model.Order };
        _context.FunnelTemplateStages.Add(stage);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = templateId },
            new FunnelTemplateStageResponseDto { TemplateStageId = stage.TemplateStageId, StageName = stage.StageName, Order = stage.Order });
    }

    // PUT: api/funneltemplates/stages/3
    [HttpPut("stages/{stageId}")]
    [SwaggerOperation(Summary = "Actualizar etapa de plantilla")]
    [SwaggerResponse(200, "Etapa actualizada", typeof(FunnelTemplateStageResponseDto))]
    [SwaggerResponse(404, "Etapa no encontrada")]
    public async Task<IActionResult> UpdateStage(int stageId, [FromBody] UpsertFunnelTemplateStageDto model)
    {
        var stage = await _context.FunnelTemplateStages.FindAsync(stageId);
        if (stage == null) return NotFound(new { message = "Etapa no encontrada." });

        stage.StageName = model.StageName;
        stage.Order = model.Order;
        await _context.SaveChangesAsync();

        return Ok(new FunnelTemplateStageResponseDto { TemplateStageId = stage.TemplateStageId, StageName = stage.StageName, Order = stage.Order });
    }

    // DELETE: api/funneltemplates/stages/3
    [HttpDelete("stages/{stageId}")]
    [SwaggerOperation(Summary = "Eliminar etapa de plantilla")]
    [SwaggerResponse(204, "Eliminada")]
    [SwaggerResponse(404, "No encontrada")]
    public async Task<IActionResult> DeleteStage(int stageId)
    {
        var stage = await _context.FunnelTemplateStages.FindAsync(stageId);
        if (stage == null) return NotFound(new { message = "Etapa no encontrada." });

        _context.FunnelTemplateStages.Remove(stage);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
