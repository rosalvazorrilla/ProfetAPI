using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Models;
using Swashbuckle.AspNetCore.Annotations;
using System.Security.Claims;

namespace ProfetAPI.Controllers;

[Route("api/leadtiers")]
[ApiController]
[Authorize]
[SwaggerTag("Scoring — Niveles de calificación (tiers) por cuenta")]
public class LeadTiersController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    public LeadTiersController(ApplicationDbContext db) => _db = db;

    private string? UserId   => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    private bool    IsAdmin  => User.FindFirst(ClaimTypes.Role)?.Value == "AdminGlobal";

    private async Task<int?> ResolveAccountId(int? accountId)
    {
        if (IsAdmin && accountId.HasValue) return accountId;
        if (!IsAdmin)
            return await _db.AccountInternalUsers.Where(u => u.UserId == UserId)
                .Select(u => (int?)u.AccountId).FirstOrDefaultAsync();
        return accountId;
    }

    /// <summary>Devuelve el ScoringModelId de la cuenta (o null si no tiene).</summary>
    private async Task<int?> ModelIdForAccount(int accountId) =>
        await _db.ScoringModels.AsNoTracking()
            .Where(m => m.AccountId == accountId)
            .Select(m => (int?)m.ScoringModelId).FirstOrDefaultAsync();

    // GET /api/leadtiers?accountId=
    [HttpGet]
    [SwaggerOperation(Summary = "Listar tiers de la cuenta")]
    public async Task<IActionResult> List([FromQuery] int? accountId)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound(new { message = "Sin cuenta asignada." });

        var modelId = await ModelIdForAccount(acId.Value);
        if (modelId == null) return Ok(Array.Empty<object>());

        var tiers = await _db.LeadTiers.AsNoTracking()
            .Where(t => t.ScoringModelId == modelId)
            .OrderBy(t => t.MinScore)
            .Select(t => new { t.TierId, t.Name, t.MinScore, t.MaxScore, t.Color })
            .ToListAsync();
        return Ok(tiers);
    }

    // POST /api/leadtiers?accountId=
    [HttpPost]
    [SwaggerOperation(Summary = "Crear tier")]
    public async Task<IActionResult> Create([FromQuery] int? accountId, [FromBody] LeadTierDto dto)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound(new { message = "Sin cuenta asignada." });
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { message = "El nombre es obligatorio." });

        var modelId = await ModelIdForAccount(acId.Value);
        if (modelId == null) return BadRequest(new { message = "La cuenta no tiene modelo de calificación." });

        var tier = new LeadTier
        {
            ScoringModelId = modelId,
            Name     = dto.Name.Trim(),
            MinScore = dto.MinScore,
            MaxScore = dto.MaxScore,
            Color    = dto.Color?.Trim(),
        };
        _db.LeadTiers.Add(tier);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(List), new { accountId }, new { tier.TierId });
    }

    // PUT /api/leadtiers/{id}?accountId=
    [HttpPut("{id:int}")]
    [SwaggerOperation(Summary = "Actualizar tier")]
    public async Task<IActionResult> Update(int id, [FromQuery] int? accountId, [FromBody] LeadTierDto dto)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound();
        var modelId = await ModelIdForAccount(acId.Value);

        var tier = await _db.LeadTiers.FirstOrDefaultAsync(t => t.TierId == id && t.ScoringModelId == modelId);
        if (tier == null) return NotFound();

        tier.Name     = dto.Name.Trim();
        tier.MinScore = dto.MinScore;
        tier.MaxScore = dto.MaxScore;
        tier.Color    = dto.Color?.Trim();
        await _db.SaveChangesAsync();
        return Ok(new { tier.TierId });
    }

    // DELETE /api/leadtiers/{id}?accountId=
    [HttpDelete("{id:int}")]
    [SwaggerOperation(Summary = "Eliminar tier (los leads que lo usaban quedan sin tier)")]
    public async Task<IActionResult> Delete(int id, [FromQuery] int? accountId)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound();
        var modelId = await ModelIdForAccount(acId.Value);

        var tier = await _db.LeadTiers.FirstOrDefaultAsync(t => t.TierId == id && t.ScoringModelId == modelId);
        if (tier == null) return NotFound();

        _db.LeadTiers.Remove(tier); // FK Lead→Tier está en SetNull
        await _db.SaveChangesAsync();
        return Ok(new { deleted = true });
    }
}

public class LeadTierDto
{
    public string   Name     { get; set; } = "";
    public decimal  MinScore { get; set; }
    public decimal? MaxScore { get; set; }
    public string?  Color    { get; set; }
}
