using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Models;
using ProfetAPI.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace ProfetAPI.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
[SwaggerTag("CRM — Historial de correos enviados")]
public class EmailsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IEmailService        _emailService;

    public EmailsController(ApplicationDbContext context, IEmailService emailService)
    {
        _context      = context;
        _emailService = emailService;
    }

    private string? CurrentUserId   => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    private string? CurrentUserRole => User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
    private bool IsAdminGlobal      => CurrentUserRole == "AdminGlobal";

    private async Task<int?> ResolveAccountId(int? accountId)
    {
        if (IsAdminGlobal && accountId.HasValue) return accountId;
        if (!IsAdminGlobal)
            return await _context.AccountInternalUsers
                .Where(u => u.UserId == CurrentUserId)
                .Select(u => (int?)u.AccountId)
                .FirstOrDefaultAsync();
        return accountId;
    }

    // ── GET /api/emails ───────────────────────────────────────────────────────
    // Historial filtrado por accountId, leadId, dealId o contactId

    [HttpGet]
    [SwaggerOperation(Summary = "Historial de correos", Description = "Filtra por account, lead, deal o contacto.")]
    [SwaggerResponse(200, "Lista paginada de correos")]
    public async Task<IActionResult> GetEmails(
        [FromQuery] int? accountId,
        [FromQuery] int? leadId,
        [FromQuery] int? dealId,
        [FromQuery] int? contactId,
        [FromQuery] string? search,
        [FromQuery] int page     = 1,
        [FromQuery] int pageSize = 25)
    {
        var resolvedAccountId = await ResolveAccountId(accountId);
        if (resolvedAccountId == null && !IsAdminGlobal)
            return BadRequest("No se pudo determinar la cuenta.");

        var q = _context.EmailLogs.AsQueryable();

        // Scope por cuenta
        if (resolvedAccountId.HasValue)
            q = q.Where(e => e.AccountId == resolvedAccountId);

        // Filtros adicionales
        if (leadId.HasValue)    q = q.Where(e => e.LeadId    == leadId);
        if (dealId.HasValue)    q = q.Where(e => e.DealId    == dealId);
        if (contactId.HasValue) q = q.Where(e => e.ContactId == contactId);

        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(e => e.Subject.Contains(search) || e.ToAddress.Contains(search));

        var total = await q.CountAsync();

        var data = await q
            .OrderByDescending(e => e.SentAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new
            {
                e.EmailLogId,
                e.LeadId,
                e.DealId,
                e.ContactId,
                e.ToAddress,
                e.CcAddress,
                e.Subject,
                BodyPreview = e.BodyHtml.Length > 200
                    ? e.BodyHtml.Substring(0, 200) + "..."
                    : e.BodyHtml,
                e.SentAt,
                e.IsSuccess,
                e.ErrorMessage,
                SentByName = _context.UserProfiles
                    .Where(p => p.UserId == e.SentByUserId)
                    .Select(p => p.FirstName + " " + p.LastName)
                    .FirstOrDefault(),
            })
            .ToListAsync();

        return Ok(new { total, page, pageSize, data });
    }

    // ── GET /api/emails/{id} ──────────────────────────────────────────────────

    [HttpGet("{id}")]
    [SwaggerOperation(Summary = "Detalle de un correo (incluye BodyHtml completo)")]
    [SwaggerResponse(200, "Correo completo")]
    [SwaggerResponse(404, "No encontrado")]
    public async Task<IActionResult> GetEmail(int id)
    {
        var resolvedAccountId = await ResolveAccountId(null);

        var email = await _context.EmailLogs
            .Where(e => e.EmailLogId == id &&
                        (IsAdminGlobal || e.AccountId == resolvedAccountId))
            .Select(e => new
            {
                e.EmailLogId,
                e.AccountId,
                e.LeadId,
                e.DealId,
                e.ContactId,
                e.ToAddress,
                e.CcAddress,
                e.Subject,
                e.BodyHtml,
                e.SentAt,
                e.IsSuccess,
                e.ErrorMessage,
                SentByName = _context.UserProfiles
                    .Where(p => p.UserId == e.SentByUserId)
                    .Select(p => p.FirstName + " " + p.LastName)
                    .FirstOrDefault(),
            })
            .FirstOrDefaultAsync();

        if (email == null) return NotFound();
        return Ok(email);
    }

    // ── POST /api/emails/send ─────────────────────────────────────────────────

    [HttpPost("send")]
    [SwaggerOperation(Summary = "Enviar correo y guardarlo en el historial")]
    [SwaggerResponse(200, "Correo enviado y registrado")]
    [SwaggerResponse(400, "Datos inválidos")]
    public async Task<IActionResult> Send([FromBody] SendEmailDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.To))
            return BadRequest("El destinatario es obligatorio.");
        if (string.IsNullOrWhiteSpace(dto.Subject))
            return BadRequest("El asunto es obligatorio.");
        if (string.IsNullOrWhiteSpace(dto.BodyHtml))
            return BadRequest("El cuerpo del correo es obligatorio.");

        var resolvedAccountId = await ResolveAccountId(dto.AccountId);

        // Resolver config SMTP: ¿tiene la cuenta su propio SMTP verificado?
        SmtpConfig? smtpConfig = null;
        if (resolvedAccountId.HasValue)
        {
            var account = await _context.Accounts
                .Where(a => a.AccountId == resolvedAccountId)
                .Select(a => new { a.SmtpEnabled, a.SmtpIsVerified, a.SmtpHost, a.SmtpPort, a.SmtpUser, a.SmtpPassword, a.SmtpFromAddress, a.SmtpFromName, a.SmtpEnableSsl })
                .FirstOrDefaultAsync();

            if (account?.SmtpEnabled == true && account.SmtpIsVerified == true
                && !string.IsNullOrWhiteSpace(account.SmtpHost)
                && !string.IsNullOrWhiteSpace(account.SmtpUser)
                && !string.IsNullOrWhiteSpace(account.SmtpPassword)
                && !string.IsNullOrWhiteSpace(account.SmtpFromAddress))
            {
                smtpConfig = new SmtpConfig(
                    Host:        account.SmtpHost!,
                    Port:        account.SmtpPort ?? 587,
                    User:        account.SmtpUser!,
                    Password:    account.SmtpPassword!,
                    FromAddress: account.SmtpFromAddress!,
                    FromName:    account.SmtpFromName ?? "CRM",
                    EnableSsl:   account.SmtpEnableSsl ?? true,
                    IsCustom:    true
                );
            }
        }

        // Intentar enviar (usa smtpConfig de la cuenta o el global de Profet como fallback)
        var (success, error) = await _emailService.SendAsync(
            to:       dto.To.Trim(),
            subject:  dto.Subject.Trim(),
            bodyHtml: dto.BodyHtml,
            cc:       dto.Cc?.Trim(),
            replyTo:  dto.ReplyTo?.Trim(),
            config:   smtpConfig);

        // Siempre guardar el log (éxito o fallo)
        var log = new EmailLog
        {
            AccountId     = resolvedAccountId,
            LeadId        = dto.LeadId,
            DealId        = dto.DealId,
            ContactId     = dto.ContactId,
            SentByUserId  = CurrentUserId,
            ToAddress     = dto.To.Trim(),
            CcAddress     = dto.Cc?.Trim(),
            Subject       = dto.Subject.Trim(),
            BodyHtml      = dto.BodyHtml,
            SentAt        = DateTime.UtcNow,
            IsSuccess     = success,
            ErrorMessage  = error,
        };

        _context.EmailLogs.Add(log);
        await _context.SaveChangesAsync();

        if (!success)
            return StatusCode(502, new { message = $"El correo fue registrado pero falló al enviarse: {error}", emailLogId = log.EmailLogId });

        return Ok(new { message = "Correo enviado correctamente.", emailLogId = log.EmailLogId });
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

public class SendEmailDto
{
    public int?    AccountId { get; set; }
    public int?    LeadId    { get; set; }
    public int?    DealId    { get; set; }
    public int?    ContactId { get; set; }

    public string  To        { get; set; } = "";
    public string? Cc        { get; set; }
    public string? ReplyTo   { get; set; }
    public string  Subject   { get; set; } = "";
    public string  BodyHtml  { get; set; } = "";
}
