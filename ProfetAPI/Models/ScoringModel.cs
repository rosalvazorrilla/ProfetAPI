using System.ComponentModel.DataAnnotations;
namespace ProfetAPI.Models;

public class ScoringModel
{
    [Key]
    public int ScoringModelId { get; set; }
    public int AccountId { get; set; }
    public string Name { get; set; } = null!;

    public virtual Account Account { get; set; } = null!;
    public virtual ICollection<ScoringQuestion> Questions { get; set; } = new List<ScoringQuestion>();
    public virtual ICollection<ScoringRule> Rules { get; set; } = new List<ScoringRule>();
}