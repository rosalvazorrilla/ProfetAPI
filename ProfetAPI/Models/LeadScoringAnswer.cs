using System.ComponentModel.DataAnnotations;
namespace ProfetAPI.Models;

public class LeadScoringAnswer
{
    [Key]
    public int ScoringAnswerId { get; set; }
    public long LeadId { get; set; }
    public int QuestionId { get; set; }
    public int AnswerOptionId { get; set; }

    public virtual Lead Lead { get; set; } = null!;
    public virtual ScoringQuestion Question { get; set; } = null!;
    public virtual ScoringAnswerOption AnswerOption { get; set; } = null!;
}