using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

public class FunnelTemplate
{
    [Key]
    public int TemplateId { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    // Propiedad de navegación
    public virtual ICollection<FunnelTemplateStage> Stages { get; set; } = new List<FunnelTemplateStage>();
}