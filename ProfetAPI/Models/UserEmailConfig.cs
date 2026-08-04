using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

/// <summary>
/// Correo propio de un usuario para enviar seguimiento a prospectos (no confundir con
/// Account.Smtp* que es el correo compartido/de notificaciones de toda la cuenta).
/// Se prioriza este sobre el de la cuenta al enviar un correo de seguimiento.
/// </summary>
public class UserEmailConfig
{
    [Key]
    public string UserId { get; set; } = null!;

    public bool?   SmtpEnabled     { get; set; } = false;
    public string? SmtpHost        { get; set; }
    public int?    SmtpPort        { get; set; }
    public string? SmtpUser        { get; set; }
    public string? SmtpPassword    { get; set; } // TODO: cifrar en producción (igual que Account.SmtpPassword)
    public string? SmtpFromAddress { get; set; }
    public string? SmtpFromName    { get; set; }
    public bool?   SmtpEnableSsl   { get; set; } = true;
    public bool?   SmtpIsVerified  { get; set; } = false;
    public DateTime? SmtpVerifiedAt { get; set; }
    public string? SmtpLastError   { get; set; }

    public virtual ApplicationUser User { get; set; } = null!;
}
