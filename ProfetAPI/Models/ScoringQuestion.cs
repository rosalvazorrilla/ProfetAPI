using System.ComponentModel.DataAnnotations;
namespace ProfetAPI.Models;

public class ScoringQuestion
{
    [Key]
    public int QuestionId { get; set; }
    public int ScoringModelId { get; set; }
    public string QuestionText { get; set; } = null!;

    public virtual ScoringModel ScoringModel { get; set; } = null!;
    public virtual ICollection<ScoringAnswerOption> AnswerOptions { get; set; } = new List<ScoringAnswerOption>();
}