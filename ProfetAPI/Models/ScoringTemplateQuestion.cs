using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

public class ScoringTemplateQuestion
{
    [Key]
    public int TemplateQuestionId { get; set; }

    public int TemplateId { get; set; }

    [Required]
    public string QuestionText { get; set; } = null!;

    /// <summary>SingleChoice | MultiChoice | OpenText | Numeric</summary>
    [MaxLength(20)]
    public string QuestionType { get; set; } = "SingleChoice";

    public bool IsRequired { get; set; } = false;
    public int OrderPosition { get; set; } = 0;

    public virtual ScoringTemplate Template { get; set; } = null!;
    public virtual ICollection<ScoringTemplateAnswerOption> AnswerOptions { get; set; } = new List<ScoringTemplateAnswerOption>();
}
