using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

public class ScoringTemplateQuestion
{
    [Key]
    public int TemplateQuestionId { get; set; }

    public int TemplateId { get; set; }

    [Required]
    public string QuestionText { get; set; } = null!;

    public virtual ScoringTemplate Template { get; set; } = null!;
    public virtual ICollection<ScoringTemplateAnswerOption> AnswerOptions { get; set; } = new List<ScoringTemplateAnswerOption>();
}