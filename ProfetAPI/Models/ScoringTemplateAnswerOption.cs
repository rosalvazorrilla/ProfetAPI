using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

public class ScoringTemplateAnswerOption
{
    [Key]
    public int TemplateAnswerId { get; set; }

    public int TemplateQuestionId { get; set; }

    [Required]
    public string AnswerText { get; set; } = null!;

    public virtual ScoringTemplateQuestion TemplateQuestion { get; set; } = null!;
}