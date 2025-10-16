using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

public class ScoringTemplate
{
    [Key]
    public int TemplateId { get; set; }

    public int? CategoryId { get; set; }

    [Required]
    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public virtual TemplateCategory? Category { get; set; }
    public virtual ICollection<ScoringTemplateQuestion> Questions { get; set; } = new List<ScoringTemplateQuestion>();
}