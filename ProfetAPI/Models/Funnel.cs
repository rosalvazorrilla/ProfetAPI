using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProfetAPI.Models;

public class Funnel
{
    [Key]
    [Column("Id")]
    public int FunnelId { get; set; }
    
    [Column("Title")]
    public string Name { get; set; } = null!;
    
    public int AccountId { get; set; }
    public int? OriginatingTemplateId { get; set; }
    
    // Propiedades de navegación
    public virtual Account Account { get; set; } = null!;
    public virtual FunnelTemplate? OriginatingTemplate { get; set; }
    public virtual ICollection<Stage> Stages { get; set; } = new List<Stage>();
}