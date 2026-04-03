using System.ComponentModel.DataAnnotations;
namespace ProfetAPI.Models;

public class ScoringAnswerOption
{
    [Key]
    public int AnswerOptionId { get; set; }
    public int QuestionId { get; set; }

    [Required]
    public string AnswerText { get; set; } = null!;

    /// <summary>Puntos directos que suma esta respuesta al score del lead.</summary>
    public decimal Points { get; set; } = 0;
    public int OrderPosition { get; set; } = 0;

    public virtual ScoringQuestion Question { get; set; } = null!;
}
