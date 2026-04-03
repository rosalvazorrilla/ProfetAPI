using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

/// <summary>Catálogo global de motivos de pérdida de leads (Admin lo pre-carga).</summary>
public class LeadLostReason
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Description { get; set; } = null!;

    /// <summary>Indica si este motivo cuenta para métricas de tasa de conversión.</summary>
    public bool ConversionRate { get; set; } = false;

    public bool IsActive { get; set; } = true;

    public virtual ICollection<AccountLeadLostReason> AccountLeadLostReasons { get; set; } = new List<AccountLeadLostReason>();
}
