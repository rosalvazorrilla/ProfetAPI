using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

public class TemplateCategory
{
    [Key]
    public int CategoryId { get; set; }

    [Required]
    public string Name { get; set; } = null!;

   // public virtual ICollection<ScoringTemplate> ScoringTemplates { get; set; } = new List<ScopingTemplate>();
}