using System.ComponentModel.DataAnnotations;
namespace ProfetAPI.Models;

public class ScoringRule
{
    [Key]
    public int RuleId { get; set; }
    public int ScoringModelId { get; set; }

    /// <summary>Nombre descriptivo. Ej: "Bonus director en empresa grande".</summary>
    [MaxLength(200)]
    public string? Name { get; set; }

    /// <summary>Puntos de bono que se suman si se cumplen todas las condiciones.</summary>
    public decimal BonusPoints { get; set; } = 0;

    public int ExecutionOrder { get; set; } = 0;

    // Legacy — mantener por compatibilidad
    public int? ConditionQuestionId { get; set; }
    public int? ConditionAnswerOptionId { get; set; }
    public string? ActionType { get; set; }
    public string? ActionValue { get; set; }

    public virtual ScoringModel ScoringModel { get; set; } = null!;
    public virtual ICollection<ScoringRuleCondition> Conditions { get; set; } = new List<ScoringRuleCondition>();
}
