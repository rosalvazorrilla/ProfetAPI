using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProfetAPI.Models;

public class Deal
{
    [Key]
    public int DealId { get; set; }
    public string? PublicId { get; set; }
    public string? ExternalId { get; set; }
    public string DealName { get; set; } = null!;
    
    [Column(TypeName = "decimal(18, 2)")]
    public decimal? QuotedAmount { get; set; }

    [Column(TypeName = "decimal(18, 2)")]
    public decimal? FinalAmount { get; set; }

    // Relaciones Clave
    public int AccountId { get; set; }
    public int? CompanyId { get; set; }
    public int? PrimaryContactId { get; set; }
    public int? StageId { get; set; }
    public int? LeadLostReasonId { get; set; }
    public int? LeadTierId { get; set; }

    [Required]
    public string Status { get; set; } = "Abierto";
    public DateTime? CloseDate { get; set; }

    [Required]
    public string DealType { get; set; } = "NewBusiness";

    public long? OriginatingLeadId { get; set; }
    public DateTime CreatedOn { get; set; }

    // Datos de Origen (copiados del lead)
    public string? ProspectSource { get; set; }
    public string? AdName { get; set; }
    public string? OriginType { get; set; }

    // Propiedades de navegación
    public virtual Account Account { get; set; } = null!;
    public virtual Company? Company { get; set; }
    public virtual Contact? PrimaryContact { get; set; }
    public virtual Stage? Stage { get; set; }
    public virtual LeadTier? LeadTier { get; set; }
    public virtual ICollection<DealUser> DealUsers { get; set; } = new List<DealUser>();
    public virtual ICollection<DealPayment> Payments { get; set; } = new List<DealPayment>();
}