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
    [SwaggerOperation(Summary = "Listar plantillas de scoring (ligero, sin preguntas)")]
    [SwaggerResponse(200, "Lista de templates", typeof(List<ScoringTemplateSummaryDto>))]
    public async Task<IActionResult> GetAll([FromQuery] long? industryId = null)
    {
        var query = _context.ScoringTemplates.AsQueryable();

        if (industryId.HasValue)
            query = query.Where(t => t.IndustryId == industryId || t.IndustryId == null);

        var list = await query
            .Select(t => new ScoringTemplateSummaryDto
            {
                Id            = t.TemplateId,
                Name          = t.Name,
                Description   = t.Description,
                IndustryId    = t.IndustryId,
                IndustryName  = t.Industry != null ? t.Industry.NameES : null,
                CategoryId    = t.CategoryId,
                CategoryName  = t.Category != null ? t.Category.Name : null,
                QuestionCount = t.Questions.Count
            })
            .ToListAsync();

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
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.TemplateId == id);

        if (t == null) return NotFound(new { message = "Template no encontrado." });
        return Ok(MapToResponse(t));
    }

    // POST: api/scoringtemplates
    [HttpPost]
    [SwaggerOperation(Summary = "Crear template de scoring")]
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
                QuestionText = q.Text,
                QuestionType = q.QuestionType,
                IsRequired = q.IsRequired,
                OrderPosition = q.Order,
                AnswerOptions = q.Answers.Select(a => new ScoringTemplateAnswerOption
                {
                    AnswerText = a.Text,
                    Points = a.Score,
                    OrderPosition = a.Order
                }).ToList()
            }).ToList()
        };

        _context.ScoringTemplates.Add(template);
        await _context.SaveChangesAsync();

        var created = await _context.ScoringTemplates
            .Include(x => x.Industry).Include(x => x.Category)
            .Include(x => x.Questions).ThenInclude(q => q.AnswerOptions)
            .AsSplitQuery()
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
            .AsSplitQuery()
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
        var t = await _context.ScoringTemplates
            .Include(x => x.Questions).ThenInclude(q => q.AnswerOptions)
            .FirstOrDefaultAsync(x => x.TemplateId == id);
        if (t == null) return NotFound(new { message = "Template no encontrado." });

        _context.ScoringTemplates.Remove(t);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // POST: api/scoringtemplates/5/questions
    [HttpPost("{templateId}/questions")]
    [SwaggerOperation(Summary = "Agregar pregunta a un template")]
    [SwaggerResponse(201, "Pregunta agregada", typeof(ScoringQuestionResponseDto))]
    [SwaggerResponse(404, "Template no encontrado")]
    public async Task<IActionResult> AddQuestion(int templateId, [FromBody] CreateScoringQuestionDto model)
    {
        var template = await _context.ScoringTemplates.FindAsync(templateId);
        if (template == null) return NotFound(new { message = "Template no encontrado." });

        var question = new ScoringTemplateQuestion
        {
            TemplateId = templateId,
            QuestionText = model.Text,
            QuestionType = model.QuestionType,
            IsRequired = model.IsRequired,
            OrderPosition = model.Order,
            AnswerOptions = model.Answers.Select(a => new ScoringTemplateAnswerOption
            {
                AnswerText = a.Text, Points = a.Score, OrderPosition = a.Order
            }).ToList()
        };

        _context.ScoringTemplateQuestions.Add(question);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = templateId }, new ScoringQuestionResponseDto
        {
            Id = question.TemplateQuestionId,
            Text = question.QuestionText,
            Order = question.OrderPosition,
            QuestionType = question.QuestionType,
            IsRequired = question.IsRequired,
            Answers = question.AnswerOptions.Select(a => new ScoringAnswerResponseDto
            { Id = a.TemplateAnswerId, Text = a.AnswerText, Score = a.Points, Order = a.OrderPosition }).ToList()
        });
    }

    // PUT: api/scoringtemplates/questions/3
    [HttpPut("questions/{questionId}")]
    [SwaggerOperation(Summary = "Actualizar pregunta del template")]
    [SwaggerResponse(200, "Pregunta actualizada", typeof(ScoringQuestionResponseDto))]
    [SwaggerResponse(404, "No encontrada")]
    public async Task<IActionResult> UpdateQuestion(int questionId, [FromBody] UpdateScoringQuestionDto model)
    {
        var q = await _context.ScoringTemplateQuestions
            .Include(x => x.AnswerOptions)
            .FirstOrDefaultAsync(x => x.TemplateQuestionId == questionId);
        if (q == null) return NotFound(new { message = "Pregunta no encontrada." });

        q.QuestionText = model.Text;
        q.QuestionType = model.QuestionType;
        q.IsRequired = model.IsRequired;
        q.OrderPosition = model.Order;
        await _context.SaveChangesAsync();

        return Ok(new ScoringQuestionResponseDto
        {
            Id = q.TemplateQuestionId,
            Text = q.QuestionText,
            Order = q.OrderPosition,
            QuestionType = q.QuestionType,
            IsRequired = q.IsRequired,
            Answers = q.AnswerOptions.Select(a => new ScoringAnswerResponseDto
            { Id = a.TemplateAnswerId, Text = a.AnswerText, Score = a.Points, Order = a.OrderPosition }).ToList()
        });
    }

    // DELETE: api/scoringtemplates/questions/3
    [HttpDelete("questions/{questionId}")]
    [SwaggerOperation(Summary = "Eliminar pregunta del template")]
    [SwaggerResponse(204, "Eliminada")]
    public async Task<IActionResult> DeleteQuestion(int questionId)
    {
        var q = await _context.ScoringTemplateQuestions
            .Include(x => x.AnswerOptions)
            .FirstOrDefaultAsync(x => x.TemplateQuestionId == questionId);
        if (q == null) return NotFound(new { message = "Pregunta no encontrada." });

        _context.ScoringTemplateQuestions.Remove(q);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // POST: api/scoringtemplates/questions/3/answers
    [HttpPost("questions/{questionId}/answers")]
    [SwaggerOperation(Summary = "Agregar respuesta a una pregunta")]
    [SwaggerResponse(201, "Respuesta agregada", typeof(ScoringAnswerResponseDto))]
    [SwaggerResponse(404, "Pregunta no encontrada")]
    public async Task<IActionResult> AddAnswer(int questionId, [FromBody] CreateScoringAnswerDto model)
    {
        var q = await _context.ScoringTemplateQuestions.FindAsync(questionId);
        if (q == null) return NotFound(new { message = "Pregunta no encontrada." });

        var answer = new ScoringTemplateAnswerOption
        {
            TemplateQuestionId = questionId,
            AnswerText = model.Text,
            Points = model.Score,
            OrderPosition = model.Order
        };
        _context.ScoringTemplateAnswerOptions.Add(answer);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = q.TemplateId },
            new ScoringAnswerResponseDto { Id = answer.TemplateAnswerId, Text = answer.AnswerText, Score = answer.Points, Order = answer.OrderPosition });
    }

    // PUT: api/scoringtemplates/answers/7
    [HttpPut("answers/{answerId}")]
    [SwaggerOperation(Summary = "Actualizar respuesta")]
    [SwaggerResponse(200, "Actualizada", typeof(ScoringAnswerResponseDto))]
    [SwaggerResponse(404, "No encontrada")]
    public async Task<IActionResult> UpdateAnswer(int answerId, [FromBody] UpdateScoringAnswerDto model)
    {
        var a = await _context.ScoringTemplateAnswerOptions.FindAsync(answerId);
        if (a == null) return NotFound(new { message = "Respuesta no encontrada." });

        a.AnswerText = model.Text;
        a.Points = model.Score;
        a.OrderPosition = model.Order;
        await _context.SaveChangesAsync();

        return Ok(new ScoringAnswerResponseDto { Id = a.TemplateAnswerId, Text = a.AnswerText, Score = a.Points, Order = a.OrderPosition });
    }

    // DELETE: api/scoringtemplates/answers/7
    [HttpDelete("answers/{answerId}")]
    [SwaggerOperation(Summary = "Eliminar respuesta")]
    [SwaggerResponse(204, "Eliminada")]
    public async Task<IActionResult> DeleteAnswer(int answerId)
    {
        var a = await _context.ScoringTemplateAnswerOptions.FindAsync(answerId);
        if (a == null) return NotFound(new { message = "Respuesta no encontrada." });

        _context.ScoringTemplateAnswerOptions.Remove(a);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // GET: api/scoringtemplates/categories
    [HttpGet("categories")]
    [SwaggerOperation(Summary = "Listar categorías de templates")]
    [SwaggerResponse(200, "Lista de categorías")]
    public async Task<IActionResult> GetCategories()
    {
        var list = await _context.TemplateCategories
            .OrderBy(c => c.Name)
            .Select(c => new { id = c.CategoryId, name = c.Name })
            .ToListAsync();
        return Ok(list);
    }

    // ── Helper ──────────────────────────────────────────────────
    private static ScoringTemplateResponseDto MapToResponse(ScoringTemplate t) => new()
    {
        Id = t.TemplateId,
        Name = t.Name,
        Description = t.Description,
        IndustryId = t.IndustryId,
        IndustryName = t.Industry?.NameES,
        CategoryId = t.CategoryId,
        CategoryName = t.Category?.Name,
        Questions = t.Questions.OrderBy(q => q.OrderPosition).Select(q => new ScoringQuestionResponseDto
        {
            Id = q.TemplateQuestionId,
            Text = q.QuestionText,
            Order = q.OrderPosition,
            QuestionType = q.QuestionType,
            IsRequired = q.IsRequired,
            Answers = q.AnswerOptions.OrderBy(a => a.OrderPosition).Select(a => new ScoringAnswerResponseDto
            { Id = a.TemplateAnswerId, Text = a.AnswerText, Score = a.Points, Order = a.OrderPosition }).ToList()
        }).ToList()
    };
}
