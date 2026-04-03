using System.ComponentModel.DataAnnotations;
namespace ProfetAPI.Models;

public class ScoringQuestion
{
    [Key]
    public int QuestionId { get; set; }
    public int ScoringModelId { get; set; }

    [Required]
    public string QuestionText { get; set; } = null!;

    /// <summary>SingleChoice | MultiChoice | OpenText | Numeric</summary>
    [MaxLength(20)]
    public string QuestionType { get; set; } = "SingleChoice";

    public bool IsRequired { get; set; } = false;
    public int OrderPosition { get; set; } = 0;

    public virtual ScoringModel ScoringModel { get; set; } = null!;
    public virtual ICollection<ScoringAnswerOption> AnswerOptions { get; set; } = new List<ScoringAnswerOption>();
}
