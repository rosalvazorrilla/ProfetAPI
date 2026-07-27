using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Models;
using Swashbuckle.AspNetCore.Annotations;
using System.Security.Claims;

namespace ProfetAPI.Controllers;

/// <summary>Catálogo de etiquetas (Tags) por cliente, y su asignación a prospectos.</summary>
[Route("api/tags")]
[ApiController]
[Authorize]
[SwaggerTag("Etiquetas de prospectos")]
public class TagsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    public TagsController(ApplicationDbContext context) => _context = context;

    private string? CurrentUserId   => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    private bool    IsAdminGlobal   => User.FindFirst(ClaimTypes.Role)?.Value == "AdminGlobal";

    private async Task<int?> ResolveCustomerId(int? accountId)
    {
        int? resolvedAccountId;
        if (accountId.HasValue)
        {
            if (!IsAdminGlobal)
            {
                var belongs = await _context.AccountInternalUsers
                    .AnyAsync(a => a.AccountId == accountId && a.UserId == CurrentUserId);
                if (!belongs) return null;
            }
            resolvedAccountId = accountId;
        }
        else
        {
            if (IsAdminGlobal) return null;
            resolvedAccountId = await _context.AccountInternalUsers
                .Where(a => a.UserId == CurrentUserId)
                .Select(a => (int?)a.AccountId).FirstOrDefaultAsync();
        }
        if (resolvedAccountId == null) return null;

        return await _context.Accounts.AsNoTracking()
            .Where(a => a.AccountId == resolvedAccountId)
            .Select(a => (int?)a.CustomerId)
            .FirstOrDefaultAsync();
    }

    // GET /api/tags?accountId=
    [HttpGet]
    [SwaggerOperation(Summary = "Listar etiquetas del cliente")]
    public async Task<IActionResult> GetTags([FromQuery] int? accountId)
    {
        var customerId = await ResolveCustomerId(accountId);
        if (customerId == null) return NotFound(new { message = "Sin cuenta." });

        var tags = await _context.Tags.AsNoTracking()
            .Where(t => t.CustomerId == customerId)
            .OrderBy(t => t.Name)
            .Select(t => new
            {
                t.TagId, t.Name, t.Color, t.FontColor,
                usageCount = _context.Taggings.Count(tg => tg.TagId == t.TagId),
            })
            .ToListAsync();

        return Ok(tags);
    }

    // POST /api/tags?accountId=
    [HttpPost]
    [SwaggerOperation(Summary = "Crear etiqueta")]
    public async Task<IActionResult> CreateTag([FromQuery] int? accountId, [FromBody] TagUpsertDto model)
    {
        var customerId = await ResolveCustomerId(accountId);
        if (customerId == null) return NotFound(new { message = "Sin cuenta." });
        if (string.IsNullOrWhiteSpace(model.Name)) return BadRequest(new { message = "El nombre es obligatorio." });

        var exists = await _context.Tags.AnyAsync(t => t.CustomerId == customerId && t.Name == model.Name.Trim());
        if (exists) return BadRequest(new { message = "Ya existe una etiqueta con ese nombre." });

        var tag = new Tag
        {
            CustomerId = customerId,
            Name       = model.Name.Trim(),
            Color      = model.Color,
            FontColor  = model.FontColor,
        };
        _context.Tags.Add(tag);
        await _context.SaveChangesAsync();

        return Ok(new { tag.TagId, tag.Name, tag.Color, tag.FontColor });
    }

    // PUT /api/tags/{id}?accountId=
    [HttpPut("{id:int}")]
    [SwaggerOperation(Summary = "Editar etiqueta")]
    public async Task<IActionResult> UpdateTag(int id, [FromQuery] int? accountId, [FromBody] TagUpsertDto model)
    {
        var customerId = await ResolveCustomerId(accountId);
        if (customerId == null) return NotFound(new { message = "Sin cuenta." });

        var tag = await _context.Tags.FirstOrDefaultAsync(t => t.TagId == id && t.CustomerId == customerId);
        if (tag == null) return NotFound(new { message = "Etiqueta no encontrada." });

        if (!string.IsNullOrWhiteSpace(model.Name)) tag.Name = model.Name.Trim();
        if (model.Color != null)     tag.Color = model.Color;
        if (model.FontColor != null) tag.FontColor = model.FontColor;
        await _context.SaveChangesAsync();

        return Ok(new { tag.TagId, tag.Name, tag.Color, tag.FontColor });
    }

    // DELETE /api/tags/{id}?accountId=
    [HttpDelete("{id:int}")]
    [SwaggerOperation(Summary = "Eliminar etiqueta (y sus asignaciones)")]
    public async Task<IActionResult> DeleteTag(int id, [FromQuery] int? accountId)
    {
        var customerId = await ResolveCustomerId(accountId);
        if (customerId == null) return NotFound(new { message = "Sin cuenta." });

        var tag = await _context.Tags.FirstOrDefaultAsync(t => t.TagId == id && t.CustomerId == customerId);
        if (tag == null) return NotFound(new { message = "Etiqueta no encontrada." });

        var taggings = _context.Taggings.Where(tg => tg.TagId == id);
        _context.Taggings.RemoveRange(taggings);
        _context.Tags.Remove(tag);
        await _context.SaveChangesAsync();

        return Ok(new { deleted = true });
    }

    // POST /api/tags/{id}/assign?leadId=
    [HttpPost("{id:int}/assign")]
    [SwaggerOperation(Summary = "Asignar una etiqueta a un prospecto")]
    public async Task<IActionResult> AssignToLead(int id, [FromQuery] long leadId)
    {
        var lead = await _context.Leads.FirstOrDefaultAsync(l => l.LeadId == leadId);
        if (lead == null) return NotFound(new { message = "Prospecto no encontrado." });
        if (!IsAdminGlobal)
        {
            var belongs = await _context.AccountInternalUsers
                .AnyAsync(a => a.AccountId == lead.AccountId && a.UserId == CurrentUserId);
            if (!belongs) return Forbid();
        }

        var already = await _context.Taggings.AnyAsync(t => t.TagId == id && t.LeadId == (int)leadId);
        if (!already)
        {
            _context.Taggings.Add(new Tagging { TagId = id, LeadId = (int)leadId });
            await _context.SaveChangesAsync();
        }
        return Ok(new { assigned = true });
    }

    // DELETE /api/tags/{id}/assign?leadId=
    [HttpDelete("{id:int}/assign")]
    [SwaggerOperation(Summary = "Quitar una etiqueta de un prospecto")]
    public async Task<IActionResult> UnassignFromLead(int id, [FromQuery] long leadId)
    {
        var tagging = await _context.Taggings.FirstOrDefaultAsync(t => t.TagId == id && t.LeadId == (int)leadId);
        if (tagging != null)
        {
            _context.Taggings.Remove(tagging);
            await _context.SaveChangesAsync();
        }
        return Ok(new { removed = true });
    }
}

public class TagUpsertDto
{
    public string? Name { get; set; }
    public string? Color { get; set; }
    public string? FontColor { get; set; }
}
