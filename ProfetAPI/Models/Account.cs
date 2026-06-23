using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProfetAPI.Models;

public class Account
{
    [Key]
    public int AccountId { get; set; }

    [Required]
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public int CustomerId { get; set; }

    // Configuraciones
    [StringLength(200)]
    public string? LandingUrl { get; set; }
    [StringLength(100)]
    public string? AssignmentType { get; set; }
    [StringLength(128)]
    public string? AssignmentUserId { get; set; }

    // Paquetes de Configuración (FKs)
    public int? LeadDealsTypesPackagesId { get; set; }
    public int? ActivitiesTemplateId { get; set; }

    // Estado y Fechas
    [Required]
    [StringLength(50)]
    public string Status { get; set; } = "Por Iniciar";
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

    // ── Add-on: Correo saliente propio ───────────────────────────────────────
    /// <summary>
    /// true = el add-on "Email Propio" está activo para esta cuenta.
    /// Cuando es true, los correos salen desde SmtpFromAddress en lugar de la cuenta global de Profet.
    /// </summary>
    public bool? SmtpEnabled     { get; set; } = false;
    public string? SmtpHost      { get; set; }   // ej: smtp.sendgrid.net / smtp.gmail.com
    public int?    SmtpPort      { get; set; }   // 587 / 465 / 25
    public string? SmtpUser      { get; set; }
    public string? SmtpPassword  { get; set; }   // TODO: cifrar en producción
    public string? SmtpFromAddress { get; set; } // ej: ventas@miempresa.com
    public string? SmtpFromName    { get; set; } // ej: Equipo de Ventas
    public bool?   SmtpEnableSsl   { get; set; } = true;
    public bool?   SmtpIsVerified  { get; set; } = false;  // true tras enviar correo de prueba exitoso
    public DateTime? SmtpVerifiedAt { get; set; }
    public string? SmtpLastError   { get; set; }

    // Propiedades de navegación
    public virtual Customer Customer { get; set; } = null!;
    public virtual ICollection<AccountInternalUser> InternalUsers { get; set; } = new List<AccountInternalUser>();
    public virtual ICollection<AccountNotificationRecipient> NotificationRecipients { get; set; } = new List<AccountNotificationRecipient>();
    public virtual ICollection<AccountStatusHistory> StatusHistory { get; set; } = new List<AccountStatusHistory>();
    public virtual ICollection<Funnel> Funnels { get; set; } = new List<Funnel>();
    // ... y otras navegaciones que añadiremos
}