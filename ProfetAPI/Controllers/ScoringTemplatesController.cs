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
[SwaggerTag("Catálogo Global — Plantillas de Calificación (Scoring)")]
public class ScoringTemplatesController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    public ScoringTemplatesController(ApplicationDbContext context) => _context = context;

    // GET: api/scoringtemplates
    [HttpGet]
    [SwaggerOperation(Summary = "Listar plantillas de scoring", Description = "Devuelve todos los templates con sus preguntas y respuestas. Se puede filtrar por industria.")]
    [SwaggerResponse(200, "Lista de templates", typeof(List<ScoringTemplateResponseDto>))]
    public async Task<IActionResult> GetAll([FromQuery] long? industryId = null)
    {
        var query = _context.ScoringTemplates
            .Include(t => t.Industry)
            .Include(t => t.Category)
            .Include(t => t.Questions).ThenInclude(q => q.AnswerOptions)
            .AsQueryable();

        if (industryId.HasValue) query = query.Where(t => t.IndustryId == industryId || t.IndustryId == null);

        var list = await query.Select(t => MapToResponse(t)).ToListAsync();
        return Ok(list);
    }

    // GET: api/scoringtemplates/5
    [HttpGet("{id}")]
    [SwaggerOperation(Summary = "Obtener template por ID")]
    [SwaggerResponse(200, "Template encontrado", typeof(ScoringTemplateResponseDto))]
    [SwaggerResponse(404, "No encontrado")]
    public async Task<IActionResult> GetById(int id)
    {
        var t = await _context.ScoringTemplates
            .Include(x => x.Industry).Include(x => x.Category)
            .Include(x => x.Questions).ThenInclude(q => q.AnswerOptions)
            .FirstOrDefaultAsync(x => x.TemplateId == id);

        if (t == null) return NotFound(new { message = "Template no encontrado." });
        return Ok(MapToResponse(t));
    }

    // POST: api/scoringtemplates
    [HttpPost]
    [SwaggerOperation(Summary = "Crear template de scoring", Description = "Crea el template completo con preguntas y respuestas. Cada respuesta tiene sus puntos directos.")]
    [SwaggerResponse(201, "Template creado", typeof(ScoringTemplateResponseDto))]
    [SwaggerResponse(400, "Datos inválidos")]
    public async Task<IActionResult> Create([FromBody] CreateScoringTemplateDto model)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var template = new ScoringTemplate
        {
            Name = model.Name,
            Description = model.Description,
            IndustryId = model.IndustryId,
            CategoryId = model.CategoryId,
            Questions = model.Questions.Select(q => new ScoringTemplateQuestion
            {
                QuestionText = q.QuestionText,
                QuestionType = q.QuestionType,
                IsRequired = q.IsRequired,
                OrderPosition = q.OrderPosition,
                AnswerOptions = q.AnswerOptions.Select(a => new ScoringTemplateAnswerOption
                {
                    AnswerText = a.AnswerText,
                    Points = a.Points,
                    OrderPosition = a.OrderPosition
                }).ToList()
            }).ToList()
        };

        _context.ScoringTemplates.Add(template);
        await _context.SaveChangesAsync();

        // Recargar para obtener navegaciones
        var created = await _context.ScoringTemplates
            .Include(x => x.Industry).Include(x => x.Category)
            .Include(x => x.Questions).ThenInclude(q => q.AnswerOptions)
            .FirstAsync(x => x.TemplateId == template.TemplateId);

        return CreatedAtAction(nameof(GetById), new { id = template.TemplateId }, MapToResponse(created));
    }

    // PUT: api/scoringtemplates/5
    [HttpPut("{id}")]
    [SwaggerOperation(Summary = "Actualizar datos generales del template")]
    [SwaggerResponse(200, "Actualizado", typeof(ScoringTemplateResponseDto))]
    [SwaggerResponse(404, "No encontrado")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateScoringTemplateDto model)
    {
        var t = await _context.ScoringTemplates
            .Include(x => x.Industry).Include(x => x.Category)
            .Include(x => x.Questions).ThenInclude(q => q.AnswerOptions)
            .FirstOrDefaultAsync(x => x.TemplateId == id);

        if (t == null) return NotFound(new { message = "Template no encontrado." });

        t.Name = model.Name;
        t.Description = model.Description;
        t.IndustryId = model.IndustryId;
        t.CategoryId = model.CategoryId;
        await _context.SaveChangesAsync();

        return Ok(MapToResponse(t));
    }

    // DELETE: api/scoringtemplates/5
    [HttpDelete("{id}")]
    [SwaggerOperation(Summary = "Eliminar template de scoring")]
    [SwaggerResponse(204, "Eliminado")]
    [SwaggerResponse(404, "No encontrado")]
    public async Task<IActionResult> Delete(int id)
    {
        var t = await _context.ScoringTemplates.Include(x => x.Questions).ThenInclude(q => q.AnswerOptions).FirstOrDefaultAsync(x => x.TemplateId == id);
        if (t == null) return NotFound(new { message = "Template no encontrado." });

        _context.ScoringTemplates.Remove(t);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // POST: api/scoringtemplates/5/questions
    [HttpPost("{templateId}/questions")]
    [SwaggerOperation(Summary = "Agregar pregunta a un template", Description = "Agrega una pregunta con sus respuestas. Tipos: SingleChoice, MultiChoice, OpenText, Numeric.")]
    [SwaggerResponse(201, "Pregunta agregada", typeof(ScoringTemplateQuestionResponseDto))]
    [SwaggerResponse(404, "Template no encontrado")]
    public async Task<IActionResult> AddQuestion(int templateId, [FromBody] CreateScoringTemplateQuestionDto model)
    {
        var template = await _context.ScoringTemplates.FindAsync(templateId);
        if (template == null) return NotFound(new { message = "Template no encontrado." });

        var question = new ScoringTemplateQuestion
        {
            TemplateId = templateId,
            QuestionText = model.QuestionText,
            QuestionType = model.QuestionType,
            IsRequired = model.IsRequired,
            OrderPosition = model.OrderPosition,
            AnswerOptions = model.AnswerOptions.Select(a => new ScoringTemplateAnswerOption
            {
                AnswerText = a.AnswerText, Points = a.Points, OrderPosition = a.OrderPosition
            }).ToList()
        };

        _context.ScoringTemplateQuestions.Add(question);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = templateId }, new ScoringTemplateQuestionResponseDto
        {
            TemplateQuestionId = question.TemplateQuestionId,
            QuestionText = question.QuestionText,
            QuestionType = question.QuestionType,
            IsRequired = question.IsRequired,
            OrderPosition = question.OrderPosition,
            AnswerOptions = question.AnswerOptions.Select(a => new ScoringTemplateAnswerOptionResponseDto
            { TemplateAnswerId = a.TemplateAnswerId, AnswerText = a.AnswerText, Points = a.Points, OrderPosition = a.OrderPosition }).ToList()
        });
    }

    // PUT: api/scoringtemplates/questions/3
    [HttpPut("questions/{questionId}")]
    [SwaggerOperation(Summary = "Actualizar pregunta del template")]
    [SwaggerResponse(200, "Pregunta actualizada", typeof(ScoringTemplateQuestionResponseDto))]
    [SwaggerResponse(404, "No encontrada")]
    public async Task<IActionResult> UpdateQuestion(int questionId, [FromBody] UpdateScoringTemplateQuestionDto model)
    {
        var q = await _context.ScoringTemplateQuestions.Include(x => x.AnswerOptions).FirstOrDefaultAsync(x => x.TemplateQuestionId == questionId);
        if (q == null) return NotFound(new { message = "Pregunta no encontrada." });

        q.QuestionText = model.QuestionText;
        q.QuestionType = model.QuestionType;
        q.IsRequired = model.IsRequired;
        q.OrderPosition = model.OrderPosition;
        await _context.SaveChangesAsync();

        return Ok(new ScoringTemplateQuestionResponseDto
        {
            TemplateQuestionId = q.TemplateQuestionId, QuestionText = q.QuestionText,
            QuestionType = q.QuestionType, IsRequired = q.IsRequired, OrderPosition = q.OrderPosition,
            AnswerOptions = q.AnswerOptions.Select(a => new ScoringTemplateAnswerOptionResponseDto
            { TemplateAnswerId = a.TemplateAnswerId, AnswerText = a.AnswerText, Points = a.Points, OrderPosition = a.OrderPosition }).ToList()
        });
    }

    // DELETE: api/scoringtemplates/questions/3
    [HttpDelete("questions/{questionId}")]
    [SwaggerOperation(Summary = "Eliminar pregunta del template")]
    [SwaggerResponse(204, "Eliminada")]
    public async Task<IActionResult> DeleteQuestion(int questionId)
    {
        var q = await _context.ScoringTemplateQuestions.Include(x => x.AnswerOptions).FirstOrDefaultAsync(x => x.TemplateQuestionId == questionId);
        if (q == null) return NotFound(new { message = "Pregunta no encontrada." });

        _context.ScoringTemplateQuestions.Remove(q);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // POST: api/scoringtemplates/questions/3/answers
    [HttpPost("questions/{questionId}/answers")]
    [SwaggerOperation(Summary = "Agregar respuesta a una pregunta")]
    [SwaggerResponse(201, "Respuesta agregada", typeof(ScoringTemplateAnswerOptionResponseDto))]
    [SwaggerResponse(404, "Pregunta no encontrada")]
    public async Task<IActionResult> AddAnswer(int questionId, [FromBody] CreateScoringTemplateAnswerOptionDto model)
    {
        var q = await _context.ScoringTemplateQuestions.FindAsync(questionId);
        if (q == null) return NotFound(new { message = "Pregunta no encontrada." });

        var answer = new ScoringTemplateAnswerOption { TemplateQuestionId = questionId, AnswerText = model.AnswerText, Points = model.Points, OrderPosition = model.OrderPosition };
        _context.ScoringTemplateAnswerOptions.Add(answer);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = q.TemplateId },
            new ScoringTemplateAnswerOptionResponseDto { TemplateAnswerId = answer.TemplateAnswerId, AnswerText = answer.AnswerText, Points = answer.Points, OrderPosition = answer.OrderPosition });
    }

    // PUT: api/scoringtemplates/answers/7
    [HttpPut("answers/{answerId}")]
    [SwaggerOperation(Summary = "Actualizar respuesta (texto y puntos)")]
    [SwaggerResponse(200, "Actualizada", typeof(ScoringTemplateAnswerOptionResponseDto))]
    [SwaggerResponse(404, "No encontrada")]
    public async Task<IActionResult> UpdateAnswer(int answerId, [FromBody] UpdateScoringTemplateAnswerOptionDto model)
    {
        var a = await _context.ScoringTemplateAnswerOptions.FindAsync(answerId);
        if (a == null) return NotFound(new { message = "Respuesta no encontrada." });

        a.AnswerText = model.AnswerText;
        a.Points = model.Points;
        a.OrderPosition = model.OrderPosition;
        await _context.SaveChangesAsync();

        return Ok(new ScoringTemplateAnswerOptionResponseDto { TemplateAnswerId = a.TemplateAnswerId, AnswerText = a.AnswerText, Points = a.Points, OrderPosition = a.OrderPosition });
    }

    // DELETE: api/scoringtemplates/answers/7
    [HttpDelete("answers/{answerId}")]
    [SwaggerOperation(Summary = "Eliminar respuesta de una pregunta")]
    [SwaggerResponse(204, "Eliminada")]
    public async Task<IActionResult> DeleteAnswer(int answerId)
    {
        var a = await _context.ScoringTemplateAnswerOptions.FindAsync(answerId);
        if (a == null) return NotFound(new { message = "Respuesta no encontrada." });

        _context.ScoringTemplateAnswerOptions.Remove(a);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // ── Helper ──────────────────────────────────────────────────
    private static ScoringTemplateResponseDto MapToResponse(ScoringTemplate t) => new()
    {
        TemplateId = t.TemplateId,
        Name = t.Name,
        Description = t.Description,
        IndustryId = t.IndustryId,
        IndustryName = t.Industry?.NameES,
        CategoryId = t.CategoryId,
        CategoryName = t.Category?.Name,
        Questions = t.Questions.OrderBy(q => q.OrderPosition).Select(q => new ScoringTemplateQuestionResponseDto
        {
            TemplateQuestionId = q.TemplateQuestionId,
            QuestionText = q.QuestionText,
            QuestionType = q.QuestionType,
            IsRequired = q.IsRequired,
            OrderPosition = q.OrderPosition,
            AnswerOptions = q.AnswerOptions.OrderBy(a => a.OrderPosition).Select(a => new ScoringTemplateAnswerOptionResponseDto
            { TemplateAnswerId = a.TemplateAnswerId, AnswerText = a.AnswerText, Points = a.Points, OrderPosition = a.OrderPosition }).ToList()
        }).ToList()
    };
}
