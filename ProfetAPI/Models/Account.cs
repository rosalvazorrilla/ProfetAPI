using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace profetApi.Models;

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
    public int? LeadLostReasonsPackagesId { get; set; }
    public int? LeadDealsTypesPackagesId { get; set; }
    public int? ActivitiesTemplateId { get; set; }

    // Estado y Fechas
    [Required]
    [StringLength(50)]
    public string Status { get; set; } = "Por Iniciar";
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

    // Propiedades de navegación
    public virtual Customer Customer { get; set; } = null!;
    public virtual ICollection<AccountInternalUser> InternalUsers { get; set; } = new List<AccountInternalUser>();
    public virtual ICollection<AccountNotificationRecipient> NotificationRecipients { get; set; } = new List<AccountNotificationRecipient>();
    public virtual ICollection<AccountStatusHistory> StatusHistory { get; set; } = new List<AccountStatusHistory>();
    public virtual ICollection<Funnel> Funnels { get; set; } = new List<Funnel>();
    // ... y otras navegaciones que añadiremos
}