using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProfetAPI.Models;

public class Stage
{
    [Key]
    [Column("Id")]
    public int StageId { get; set; }
    public int FunnelId { get; set; }
    
    [Column("Title")]
    public string Name { get; set; } = null!;
    
    [Column("Position")]
    public int Order { get; set; }
    
    [Column("color")]
    public string? Color { get; set; }

    // Propiedad de navegación
    public virtual Funnel Funnel { get; set; } = null!;
}