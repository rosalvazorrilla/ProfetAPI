using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProfetAPI.Models;

[Table("FunnelTemplateStages")]
public class FunnelTemplateStage
{
    [Key]
    public int TemplateStageId { get; set; }

    [ForeignKey("FunnelTemplate")]
    public int TemplateId { get; set; }

    public string StageName { get; set; } = null!;

    [Column("Order")]
    public int Order { get; set; }

    // Propiedad de navegación
    public virtual FunnelTemplate FunnelTemplate { get; set; } = null!;
}