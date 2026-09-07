using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Models;
using Swashbuckle.AspNetCore.Annotations;

namespace ProfetAPI.Controllers;

// ── CRUD de plantillas de mensaje (Email/WhatsApp) por cuenta ────────────────

[Route("api/message-templates")]
[ApiController]
[Authorize]
[SwaggerTag("CRM — Plantillas de mensaje (envío automático y seguimiento masivo)")]
public class MessageTemplatesController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    private string? UserId   => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    private string? UserRole => User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
    private bool IsAdmin     => UserRole == "AdminGlobal";

    public MessageTemplatesController(ApplicationDbContext db) => _db = db;

    private async Task<int?> ResolveAccountId(int? accountId)
    {
        if (IsAdmin && accountId.HasValue) return accountId;
        if (!IsAdmin)
            return await _db.AccountInternalUsers
                .Where(u => u.UserId == UserId)
                .Select(u => (int?)u.AccountId)
                .FirstOrDefaultAsync();
        return accountId;
    }

    // GET /api/message-templates?channel=Email
    [HttpGet]
    [SwaggerOperation(Summary = "Listar plantillas de la cuenta")]
    public async Task<IActionResult> List([FromQuery] int? accountId, [FromQuery] string? channel)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound(new { message = "Sin cuenta asignada." });

        var q = _db.MessageTemplates.Where(t => t.AccountId == acId);
        if (!string.IsNullOrWhiteSpace(channel)) q = q.Where(t => t.Channel == channel);

        var templates = await q.OrderBy(t => t.Name)
            .Select(t => new { t.TemplateId, t.Name, t.Channel, t.Subject, t.Body, t.IsActive, t.CreatedOn })
            .ToListAsync();
        return Ok(templates);
    }

    // GET /api/message-templates/fields — variables disponibles para {{...}}
    [HttpGet("fields")]
    [SwaggerOperation(Summary = "Variables disponibles para interpolar en una plantilla")]
    public IActionResult Fields()
    {
        return Ok(new[]
        {
            new { field = "nombre",   label = "Nombre del prospecto" },
            new { field = "empresa",  label = "Empresa" },
            new { field = "email",    label = "Correo" },
            new { field = "telefono", label = "Teléfono" },
            new { field = "ciudad",   label = "Ciudad" },
            new { field = "cargo",    label = "Puesto" },
        });
    }

    // POST /api/message-templates
    [HttpPost]
    [SwaggerOperation(Summary = "Crear plantilla")]
    public async Task<IActionResult> Create([FromQuery] int? accountId, [FromBody] SaveTemplateRequest req)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound(new { message = "Sin cuenta asignada." });
        var error = ValidateRequest(req);
        if (error != null) return BadRequest(new { message = error });

        var template = new MessageTemplate
        {
            AccountId = acId.Value,
            Name      = req.Name!.Trim(),
            Channel   = req.Channel!,
            Subject   = req.Channel == "Email" ? req.Subject?.Trim() : null,
            Body      = req.Body!.Trim(),
            IsActive  = req.IsActive,
        };
        _db.MessageTemplates.Add(template);
        await _db.SaveChangesAsync();
        return Ok(new { template.TemplateId });
    }

    // PUT /api/message-templates/{id}
    [HttpPut("{id:int}")]
    [SwaggerOperation(Summary = "Actualizar plantilla")]
    public async Task<IActionResult> Update(int id, [FromQuery] int? accountId, [FromBody] SaveTemplateRequest req)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound();
        var error = ValidateRequest(req);
        if (error != null) return BadRequest(new { message = error });

        var template = await _db.MessageTemplates.FirstOrDefaultAsync(t => t.TemplateId == id && t.AccountId == acId);
        if (template == null) return NotFound();

        template.Name     = req.Name!.Trim();
        template.Channel  = req.Channel!;
        template.Subject  = req.Channel == "Email" ? req.Subject?.Trim() : null;
        template.Body     = req.Body!.Trim();
        template.IsActive = req.IsActive;
        await _db.SaveChangesAsync();
        return Ok(new { template.TemplateId });
    }

    // DELETE /api/message-templates/{id}
    [HttpDelete("{id:int}")]
    [SwaggerOperation(Summary = "Eliminar plantilla")]
    public async Task<IActionResult> Delete(int id, [FromQuery] int? accountId)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound();

        var template = await _db.MessageTemplates.FirstOrDefaultAsync(t => t.TemplateId == id && t.AccountId == acId);
        if (template == null) return NotFound();

        var inUse = await _db.PlaybookTasks.AnyAsync(pt => pt.TemplateId == id);
        if (inUse) return BadRequest(new { message = "Esta plantilla está en uso por un paso de una secuencia — desactívala en su lugar o quítala del paso primero." });

        _db.MessageTemplates.Remove(template);
        await _db.SaveChangesAsync();
        return Ok(new { deleted = true });
    }

    private static string? ValidateRequest(SaveTemplateRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return "El nombre es obligatorio.";
        if (req.Channel != "Email" && req.Channel != "WhatsApp") return "Canal inválido — debe ser Email o WhatsApp.";
        if (string.IsNullOrWhiteSpace(req.Body)) return "El contenido del mensaje es obligatorio.";
        if (req.Channel == "Email" && string.IsNullOrWhiteSpace(req.Subject)) return "El asunto es obligatorio para plantillas de Email.";
        return null;
    }
}

public class SaveTemplateRequest
{
    public string? Name    { get; set; }
    public string? Channel { get; set; }   // "Email" | "WhatsApp"
    public string? Subject { get; set; }
    public string? Body    { get; set; }
    public bool    IsActive { get; set; } = true;
}
