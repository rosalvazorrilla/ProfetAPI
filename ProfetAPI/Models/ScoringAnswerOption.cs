using System.ComponentModel.DataAnnotations;
namespace ProfetAPI.Models;

public class ScoringAnswerOption
{
    [Key]
    public int AnswerOptionId { get; set; }
    public int QuestionId { get; set; }
    public string AnswerText { get; set; } = null!;

    public virtual ScoringQuestion Question { get; set; } = null!;
}