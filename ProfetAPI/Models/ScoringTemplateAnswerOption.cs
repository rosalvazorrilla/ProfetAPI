using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

public class ScoringTemplateAnswerOption
{
    [Key]
    public int TemplateAnswerId { get; set; }

    public int TemplateQuestionId { get; set; }

    [Required]
    public string AnswerText { get; set; } = null!;

    /// <summary>Puntos directos que suma esta respuesta al score del lead.</summary>
    public decimal Points { get; set; } = 0;
    public int OrderPosition { get; set; } = 0;

    public virtual ScoringTemplateQuestion TemplateQuestion { get; set; } = null!;
}
