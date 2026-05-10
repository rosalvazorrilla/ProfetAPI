using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

/// <summary>Sugerencias globales de motivos de pérdida que el Admin pre-carga para mostrar en el wizard.</summary>
public class LeadLostReasonTemplate
{
    [Key]
    public int TemplateId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Description { get; set; } = null!;

    /// <summary>Indica si este motivo debe contar para gráficas de conversión.</summary>
    public bool CountsForCharts { get; set; } = true;

    public bool IsActive { get; set; } = true;
}
