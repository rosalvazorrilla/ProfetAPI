using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProfetAPI.Models;

public class FunnelTemplateStage
{
    [Key]
    public int TemplateStageId { get; set; }
    public int TemplateId { get; set; }
    public string StageName { get; set; } = null!;
    
    [Column("\"Order\"")] // El uso de "Order" como nombre de columna puede requerir comillas
    public int Order { get; set; }

    // Propiedad de navegación
    public virtual FunnelTemplate FunnelTemplate { get; set; } = null!;
}