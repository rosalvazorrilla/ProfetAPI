using System.ComponentModel.DataAnnotations;
namespace ProfetAPI.Models;

public class LeadScoringAnswer
{
    [Key]
    public int ScoringAnswerId { get; set; }
    public long LeadId { get; set; }
    public int QuestionId { get; set; }

    /// <summary>Respuesta seleccionada (para preguntas SingleChoice/MultiChoice).</summary>
    public int? AnswerOptionId { get; set; }

    /// <summary>Respuesta libre (para QuestionType = OpenText).</summary>
    public string? TextValue { get; set; }

    /// <summary>Valor numérico (para QuestionType = Numeric).</summary>
    public decimal? NumericValue { get; set; }

    /// <summary>Puntos calculados y guardados en el momento de responder.</summary>
    public decimal PointsAwarded { get; set; } = 0;

    public virtual Lead Lead { get; set; } = null!;
    public virtual ScoringQuestion Question { get; set; } = null!;
    public virtual ScoringAnswerOption? AnswerOption { get; set; }
}
