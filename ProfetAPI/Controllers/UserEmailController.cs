using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Models;
using ProfetAPI.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace ProfetAPI.Controllers;

/// <summary>
/// Correo propio del usuario para enviar seguimiento a prospectos — distinto del correo
/// compartido de la cuenta (AccountEmailController), que es para notificaciones del sistema.
/// Al enviar un correo de seguimiento (EmailsController.Send) se prioriza este sobre el de la cuenta.
/// </summary>
[Route("api/user/email-config")]
[ApiController]
[Authorize]
[SwaggerTag("CRM — Correo propio del usuario para seguimiento a prospectos")]
public class UserEmailController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IEmailService        _emailService;

    public UserEmailController(ApplicationDbContext context, IEmailService emailService)
    {
        _context      = context;
        _emailService = emailService;
    }

    private string CurrentUserId => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";

    // ── GET /api/user/email-config ────────────────────────────────────────────

    [HttpGet]
    [SwaggerOperation(Summary = "Obtener configuración de correo propio del usuario autenticado")]
    [SwaggerResponse(200, "Configuración actual")]
    public async Task<IActionResult> GetConfig()
    {
        var config = await _context.UserEmailConfigs
            .AsNoTracking()
            .Where(c => c.UserId == CurrentUserId)
            .Select(c => new UserEmailConfigDto
            {
                SmtpEnabled     = c.SmtpEnabled ?? false,
                SmtpHost        = c.SmtpHost,
                SmtpPort        = c.SmtpPort ?? 587,
                SmtpUser        = c.SmtpUser,
                HasPassword     = !string.IsNullOrEmpty(c.SmtpPassword),
                SmtpFromAddress = c.SmtpFromAddress,
                SmtpFromName    = c.SmtpFromName,
                SmtpEnableSsl   = c.SmtpEnableSsl ?? true,
                SmtpIsVerified  = c.SmtpIsVerified ?? false,
                SmtpVerifiedAt  = c.SmtpVerifiedAt,
                SmtpLastError   = c.SmtpLastError,
            })
            .FirstOrDefaultAsync();

        // Si nunca ha configurado nada, regresar valores vacíos (no 404 — es un estado válido)
        return Ok(config ?? new UserEmailConfigDto { SmtpPort = 587, SmtpEnableSsl = true });
    }

    // ── PUT /api/user/email-config ────────────────────────────────────────────

    [HttpPut]
    [SwaggerOperation(Summary = "Guardar el correo propio (no activa hasta verificar con una prueba)")]
    [SwaggerResponse(200, "Configuración guardada")]
    public async Task<IActionResult> SaveConfig([FromBody] SaveUserEmailConfigDto dto)
    {
        var config = await _context.UserEmailConfigs.FindAsync(CurrentUserId);
        if (config == null)
        {
            config = new UserEmailConfig { UserId = CurrentUserId };
            _context.UserEmailConfigs.Add(config);
        }

        config.SmtpHost        = dto.SmtpHost?.Trim();
        config.SmtpPort        = dto.SmtpPort;
        config.SmtpUser        = dto.SmtpUser?.Trim();
        config.SmtpFromAddress = dto.SmtpFromAddress?.Trim();
        config.SmtpFromName    = dto.SmtpFromName?.Trim();
        config.SmtpEnableSsl   = dto.SmtpEnableSsl;

        if (!string.IsNullOrWhiteSpace(dto.SmtpPassword))
            config.SmtpPassword = dto.SmtpPassword.Trim();

        // Al cambiar la config, pierde la verificación hasta nuevo test
        config.SmtpIsVerified = false;
        config.SmtpVerifiedAt = null;
        config.SmtpLastError  = null;

        await _context.SaveChangesAsync();
        return Ok(new { message = "Configuración guardada. Envía un correo de prueba para verificarla." });
    }

    // ── POST /api/user/email-config/test ──────────────────────────────────────

    [HttpPost("test")]
    [SwaggerOperation(Summary = "Enviar correo de prueba y verificar la configuración")]
    [SwaggerResponse(200, "Prueba exitosa — configuración verificada")]
    [SwaggerResponse(502, "Error SMTP — ver detalles")]
    public async Task<IActionResult> TestConfig([FromBody] TestUserEmailDto dto)
    {
        var config = await _context.UserEmailConfigs.FindAsync(CurrentUserId);
        if (config == null) return BadRequest("Guarda tu configuración de correo antes de probar.");

        if (string.IsNullOrWhiteSpace(config.SmtpHost) ||
            string.IsNullOrWhiteSpace(config.SmtpUser) ||
            string.IsNullOrWhiteSpace(config.SmtpPassword) ||
            string.IsNullOrWhiteSpace(config.SmtpFromAddress))
            return BadRequest("Guarda la configuración SMTP completa antes de probar.");

        var smtpConfig = new SmtpConfig(
            Host:        config.SmtpHost!,
            Port:        config.SmtpPort ?? 587,
            User:        config.SmtpUser!,
            Password:    config.SmtpPassword!,
            FromAddress: config.SmtpFromAddress!,
            FromName:    config.SmtpFromName ?? "CRM",
            EnableSsl:   config.SmtpEnableSsl ?? true,
            IsCustom:    true
        );

        var testTo = string.IsNullOrWhiteSpace(dto.TestTo) ? config.SmtpFromAddress! : dto.TestTo.Trim();

        var (success, error) = await _emailService.SendAsync(
            to:       testTo,
            subject:  "✅ Prueba de configuración — Profet CRM",
            bodyHtml: $"<p>¡Funciona! Tu correo personal está conectado correctamente para enviar seguimiento a tus prospectos.</p><p>Este correo fue enviado desde <strong>{config.SmtpFromAddress}</strong>.</p>",
            config:   smtpConfig
        );

        config.SmtpIsVerified = success;
        config.SmtpVerifiedAt = success ? DateTime.UtcNow : null;
        config.SmtpLastError  = success ? null : error;
        config.SmtpEnabled    = success;
        await _context.SaveChangesAsync();

        if (!success)
            return StatusCode(502, new { message = $"Error de conexión SMTP: {error}" });

        return Ok(new { message = $"¡Correo de prueba enviado a {testTo}! Tu correo de seguimiento quedó activado." });
    }

    // ── DELETE /api/user/email-config ─────────────────────────────────────────

    [HttpDelete]
    [SwaggerOperation(Summary = "Desactivar el correo propio (los seguimientos vuelven a salir desde la cuenta/Profet)")]
    [SwaggerResponse(200, "Desactivado")]
    public async Task<IActionResult> Disable()
    {
        var config = await _context.UserEmailConfigs.FindAsync(CurrentUserId);
        if (config == null) return Ok(new { message = "No tenías correo propio configurado." });

        config.SmtpEnabled    = false;
        config.SmtpIsVerified = false;
        await _context.SaveChangesAsync();
        return Ok(new { message = "Correo propio desactivado." });
    }

    // ── DTOs ─────────────────────────────────────────────────────────────────

    public class UserEmailConfigDto
    {
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
    }

    public class SaveUserEmailConfigDto
    {
        public string? SmtpHost        { get; set; }
        public int     SmtpPort        { get; set; } = 587;
        public string? SmtpUser        { get; set; }
        public string? SmtpPassword    { get; set; }
        public string? SmtpFromAddress { get; set; }
        public string? SmtpFromName    { get; set; }
        public bool    SmtpEnableSsl   { get; set; } = true;
    }

    public class TestUserEmailDto
    {
        public string? TestTo { get; set; }
    }
}
