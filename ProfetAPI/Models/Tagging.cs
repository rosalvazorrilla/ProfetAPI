using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProfetAPI.Models;

[Table("LeadsTags")]
public class Tagging
{
    [Key]
    [Column("Id")]
    public int Id { get; set; }

    [Column("LeadId")]
    public int LeadId { get; set; }

    [Column("TagsLeadId")]
    public int TagId { get; set; }

    // Propiedad de navegación
    public virtual Tag Tag { get; set; } = null!;
}