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
[SwaggerTag("Catálogo Global — Variables (Campos Personalizados)")]
public class CustomFieldDefinitionsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    public CustomFieldDefinitionsController(ApplicationDbContext context) => _context = context;

    // GET: api/customfielddefinitions
    [HttpGet]
    [SwaggerOperation(Summary = "Listar variables disponibles", Description = "Devuelve el pool completo de campos capturables en leads. Los Accounts seleccionan cuáles activar.")]
    [SwaggerResponse(200, "Lista de variables", typeof(List<CustomFieldDefinitionResponseDto>))]
    public async Task<IActionResult> GetAll()
    {
        var list = await _context.CustomFieldDefinitions
            .Select(f => new CustomFieldDefinitionResponseDto { Id = f.FieldId, FieldCode = f.FieldCode, FieldName = f.FieldName, FieldType = f.FieldType })
            .ToListAsync();
        return Ok(list);
    }

    // GET: api/customfielddefinitions/5
    [HttpGet("{id}")]
    [SwaggerOperation(Summary = "Obtener variable por ID")]
    [SwaggerResponse(200, "Variable encontrada", typeof(CustomFieldDefinitionResponseDto))]
    [SwaggerResponse(404, "No encontrada")]
    public async Task<IActionResult> GetById(int id)
    {
        var f = await _context.CustomFieldDefinitions.FindAsync(id);
        if (f == null) return NotFound(new { message = "Variable no encontrada." });
        return Ok(new CustomFieldDefinitionResponseDto { Id = f.FieldId, FieldCode = f.FieldCode, FieldName = f.FieldName, FieldType = f.FieldType });
    }

    // POST: api/customfielddefinitions
    [HttpPost]
    [SwaggerOperation(Summary = "Crear variable", Description = "Agrega un nuevo campo al catálogo global de variables capturables.")]
    [SwaggerResponse(201, "Variable creada", typeof(CustomFieldDefinitionResponseDto))]
    [SwaggerResponse(400, "Datos inválidos")]
    public async Task<IActionResult> Create([FromBody] CreateCustomFieldDefinitionDto model)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        // Verificar que el FieldCode no esté duplicado
        var exists = await _context.CustomFieldDefinitions.AnyAsync(f => f.FieldCode == model.FieldCode);
        if (exists) return BadRequest(new { message = $"Ya existe una variable con el código '{model.FieldCode}'." });

        var field = new CustomFieldDefinition { FieldCode = model.FieldCode, FieldName = model.FieldName, FieldType = model.FieldType };
        _context.CustomFieldDefinitions.Add(field);
        await _context.SaveChangesAsync();

        var response = new CustomFieldDefinitionResponseDto { Id = field.FieldId, FieldCode = field.FieldCode, FieldName = field.FieldName, FieldType = field.FieldType };
        return CreatedAtAction(nameof(GetById), new { id = field.FieldId }, response);
    }

    // PUT: api/customfielddefinitions/5
    [HttpPut("{id}")]
    [SwaggerOperation(Summary = "Actualizar variable", Description = "Permite cambiar el nombre visible y tipo de dato. El FieldCode no se puede cambiar.")]
    [SwaggerResponse(200, "Variable actualizada", typeof(CustomFieldDefinitionResponseDto))]
    [SwaggerResponse(404, "No encontrada")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCustomFieldDefinitionDto model)
    {
        var field = await _context.CustomFieldDefinitions.FindAsync(id);
        if (field == null) return NotFound(new { message = "Variable no encontrada." });

        field.FieldName = model.FieldName;
        field.FieldType = model.FieldType;
        await _context.SaveChangesAsync();

        return Ok(new CustomFieldDefinitionResponseDto { Id = field.FieldId, FieldCode = field.FieldCode, FieldName = field.FieldName, FieldType = field.FieldType });
    }

    // DELETE: api/customfielddefinitions/5
    [HttpDelete("{id}")]
    [SwaggerOperation(Summary = "Eliminar variable del catálogo global")]
    [SwaggerResponse(204, "Eliminada")]
    [SwaggerResponse(404, "No encontrada")]
    public async Task<IActionResult> Delete(int id)
    {
        var field = await _context.CustomFieldDefinitions.FindAsync(id);
        if (field == null) return NotFound(new { message = "Variable no encontrada." });

        _context.CustomFieldDefinitions.Remove(field);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
