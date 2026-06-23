using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace ProfetAPI.Controllers;

[Route("api/account/email-config")]
[ApiController]
[Authorize]
[SwaggerTag("CRM — Configuración de correo saliente por cuenta")]
public class AccountEmailController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IEmailService        _emailService;

    public AccountEmailController(ApplicationDbContext context, IEmailService emailService)
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

    // ── GET /api/account/email-config ─────────────────────────────────────────

    [HttpGet]
    [SwaggerOperation(Summary = "Obtener configuración SMTP de la cuenta")]
    [SwaggerResponse(200, "Configuración actual")]
    public async Task<IActionResult> GetConfig([FromQuery] int? accountId)
    {
        var resolved = await ResolveAccountId(accountId);
        if (resolved == null) return BadRequest("No se pudo determinar la cuenta.");

        var account = await _context.Accounts
            .Where(a => a.AccountId == resolved)
            .Select(a => new EmailConfigDto
            {
                AccountId      = a.AccountId,
                SmtpEnabled    = a.SmtpEnabled ?? false,
                SmtpHost       = a.SmtpHost,
                SmtpPort       = a.SmtpPort ?? 587,
                SmtpUser       = a.SmtpUser,
                // No devolvemos la contraseña — solo indicamos si está configurada
                HasPassword    = !string.IsNullOrEmpty(a.SmtpPassword),
                SmtpFromAddress= a.SmtpFromAddress,
                SmtpFromName   = a.SmtpFromName,
                SmtpEnableSsl  = a.SmtpEnableSsl ?? true,
                SmtpIsVerified = a.SmtpIsVerified ?? false,
                SmtpVerifiedAt = a.SmtpVerifiedAt,
                SmtpLastError  = a.SmtpLastError,
                // Info: si no hay config propia, está usando la global de Profet
                UsingGlobal    = !(a.SmtpEnabled == true && a.SmtpIsVerified == true && !string.IsNullOrEmpty(a.SmtpHost)),
            })
            .FirstOrDefaultAsync();

        if (account == null) return NotFound();
        return Ok(account);
    }

    // ── PUT /api/account/email-config ─────────────────────────────────────────

    [HttpPut]
    [SwaggerOperation(Summary = "Guardar configuración SMTP (no activa hasta verificar)")]
    [SwaggerResponse(200, "Configuración guardada")]
    public async Task<IActionResult> SaveConfig([FromQuery] int? accountId, [FromBody] SaveEmailConfigDto dto)
    {
        var resolved = await ResolveAccountId(accountId);
        if (resolved == null) return BadRequest("No se pudo determinar la cuenta.");

        var account = await _context.Accounts.FindAsync(resolved);
        if (account == null) return NotFound();

        account.SmtpHost        = dto.SmtpHost?.Trim();
        account.SmtpPort        = dto.SmtpPort;
        account.SmtpUser        = dto.SmtpUser?.Trim();
        account.SmtpFromAddress = dto.SmtpFromAddress?.Trim();
        account.SmtpFromName    = dto.SmtpFromName?.Trim();
        account.SmtpEnableSsl   = dto.SmtpEnableSsl;

        // Solo actualizar la contraseña si se envió una nueva (no vacía)
        if (!string.IsNullOrWhiteSpace(dto.SmtpPassword))
            account.SmtpPassword = dto.SmtpPassword.Trim();

        // Al cambiar la config, pierde la verificación hasta nuevo test
        account.SmtpIsVerified = false;
        account.SmtpVerifiedAt = null;
        account.SmtpLastError  = null;

        await _context.SaveChangesAsync();
        return Ok(new { message = "Configuración guardada. Envía un correo de prueba para verificarla." });
    }

    // ── POST /api/account/email-config/test ───────────────────────────────────

    [HttpPost("test")]
    [SwaggerOperation(Summary = "Enviar correo de prueba y verificar la configuración")]
    [SwaggerResponse(200, "Prueba exitosa — configuración verificada")]
    [SwaggerResponse(502, "Error SMTP — ver detalles")]
    public async Task<IActionResult> TestConfig([FromQuery] int? accountId, [FromBody] TestEmailDto dto)
    {
        var resolved = await ResolveAccountId(accountId);
        if (resolved == null) return BadRequest("No se pudo determinar la cuenta.");

        var account = await _context.Accounts.FindAsync(resolved);
        if (account == null) return NotFound();

        if (string.IsNullOrWhiteSpace(account.SmtpHost) ||
            string.IsNullOrWhiteSpace(account.SmtpUser) ||
            string.IsNullOrWhiteSpace(account.SmtpPassword) ||
            string.IsNullOrWhiteSpace(account.SmtpFromAddress))
            return BadRequest("Guarda la configuración SMTP completa antes de probar.");

        var config = new SmtpConfig(
            Host:        account.SmtpHost!,
            Port:        account.SmtpPort ?? 587,
            User:        account.SmtpUser!,
            Password:    account.SmtpPassword!,
            FromAddress: account.SmtpFromAddress!,
            FromName:    account.SmtpFromName ?? "CRM",
            EnableSsl:   account.SmtpEnableSsl ?? true,
            IsCustom:    true
        );

        var testTo = string.IsNullOrWhiteSpace(dto.TestTo) ? account.SmtpFromAddress! : dto.TestTo.Trim();

        var (success, error) = await _emailService.SendAsync(
            to:       testTo,
            subject:  "✅ Prueba de configuración — Profet CRM",
            bodyHtml: $"<p>¡Funciona! Tu configuración SMTP está conectada correctamente.</p><p>Este correo fue enviado desde <strong>{account.SmtpFromAddress}</strong> a través de tu servidor SMTP configurado en Profet CRM.</p>",
            config:   config
        );

        account.SmtpIsVerified = success;
        account.SmtpVerifiedAt = success ? DateTime.UtcNow : null;
        account.SmtpLastError  = success ? null : error;
        account.SmtpEnabled    = success; // activar automáticamente si el test pasa
        await _context.SaveChangesAsync();

        if (!success)
            return StatusCode(502, new { message = $"Error de conexión SMTP: {error}" });

        return Ok(new { message = $"¡Correo de prueba enviado a {testTo}! Configuración verificada y activada." });
    }

    // ── DELETE (desactivar sin borrar config) ─────────────────────────────────

    [HttpDelete]
    [SwaggerOperation(Summary = "Desactivar correo propio (vuelve a usar Profet global)")]
    [SwaggerResponse(200, "Desactivado")]
    public async Task<IActionResult> Disable([FromQuery] int? accountId)
    {
        var resolved = await ResolveAccountId(accountId);
        if (resolved == null) return BadRequest("No se pudo determinar la cuenta.");

        var account = await _context.Accounts.FindAsync(resolved);
        if (account == null) return NotFound();

        account.SmtpEnabled    = false;
        account.SmtpIsVerified = false;
        await _context.SaveChangesAsync();
        return Ok(new { message = "Correo propio desactivado. Los correos salen ahora desde Profet." });
    }

    // ── DTOs ─────────────────────────────────────────────────────────────────

    public class EmailConfigDto
    {
        public int     AccountId       { get; set; }
        public bool    SmtpEnabled     { get; set; }
        public string? SmtpHost        { get; set; }
        public int     SmtpPort        { get; set; }
        public string? SmtpUser        { get; set; }
        public bool    HasPassword     { get; set; }
        public string? SmtpFromAddress { get; set; }
        public string? SmtpFromName    { get; set; }
        public bool    SmtpEnableSsl   { get; set; }
        public bool    SmtpIsVerified  { get; set; }
        public DateTime? SmtpVerifiedAt { get; set; }
        public string? SmtpLastError   { get; set; }
        public bool    UsingGlobal     { get; set; }
    }

    public class SaveEmailConfigDto
    {
        public string? SmtpHost        { get; set; }
        public int     SmtpPort        { get; set; } = 587;
        public string? SmtpUser        { get; set; }
        public string? SmtpPassword    { get; set; }  // vacío = no cambiar
        public string? SmtpFromAddress { get; set; }
        public string? SmtpFromName    { get; set; }
        public bool    SmtpEnableSsl   { get; set; } = true;
    }

    public class TestEmailDto
    {
        public string? TestTo { get; set; }   // si vacío, envía al FromAddress
    }
}
