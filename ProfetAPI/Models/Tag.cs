using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProfetAPI.Models;

[Table("TagsLeads")]
public class Tag
{
    [Key]
    [Column("Id")]
    public int TagId { get; set; }

    [Column("CustomerId")]
    public int? CustomerId { get; set; }

    [Required]
    [Column("Name")]
    public string Name { get; set; } = null!;

    [Column("Color")]
    public string? Color { get; set; }

    [Column("FontColor")]
    public string? FontColor { get; set; }

    // Propiedad de navegación
    public virtual Customer? Customer { get; set; }
}