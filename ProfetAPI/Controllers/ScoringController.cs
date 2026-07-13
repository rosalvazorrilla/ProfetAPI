using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Dtos.Scoring;
using ProfetAPI.Services;
using Swashbuckle.AspNetCore.Annotations;
using System.Security.Claims;

namespace ProfetAPI.Controllers;

[Route("api/scoring")]
[ApiController]
[Authorize]
[SwaggerTag("Scoring — Generador y configuración con IA")]
public class ScoringController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IScoringAiService _ai;

    public ScoringController(ApplicationDbContext db, IScoringAiService ai)
    {
        _db = db;
        _ai = ai;
    }

    private string? UserId  => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    private bool    IsAdmin => User.FindFirst(ClaimTypes.Role)?.Value == "AdminGlobal";

    private async Task<bool> HasAccess(int accountId)
    {
        if (IsAdmin) return true;
        return await _db.AccountInternalUsers.AnyAsync(u => u.AccountId == accountId && u.UserId == UserId);
    }

    // POST /api/scoring/generate-questions  — F2: propuesta editable desde un prompt
    [HttpPost("generate-questions")]
    [SwaggerOperation(Summary = "Generar criterios de calificación desde un prompt (IA)")]
    [SwaggerResponse(200, "Propuesta editable")]
    public async Task<IActionResult> Generate([FromBody] GenerateQuestionsRequestDto req)
    {
        if (!await HasAccess(req.AccountId)) return Forbid();
        if (!_ai.IsConfigured) return StatusCode(503, new { message = "La IA no está configurada (falta Anthropic:ApiKey)." });
        if (string.IsNullOrWhiteSpace(req.Prompt)) return BadRequest(new { message = "Describe tu negocio en el prompt." });

        var proposal = await _ai.GenerateFromPromptAsync(req);
        return Ok(proposal);
    }

    // POST /api/scoring/save-model  — F2-T4: persistir la propuesta (editada) como ScoringModel real
    [HttpPost("save-model")]
    [SwaggerOperation(Summary = "Guardar la propuesta editada como modelo de calificación")]
    [SwaggerResponse(200, "Modelo guardado")]
    public async Task<IActionResult> SaveModel([FromBody] SaveScoringModelRequestDto req)
    {
        if (!await HasAccess(req.AccountId)) return Forbid();

        var modelId = await _ai.SaveModelAsync(req);
        return Ok(new { scoringModelId = modelId });
    }
}
