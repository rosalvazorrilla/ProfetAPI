using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

/// <summary>
/// Condición individual de una ScoringRule.
/// Una regla puede tener múltiples condiciones unidas por AND/OR.
/// </summary>
public class ScoringRuleCondition
{
    [Key]
    public int ConditionId { get; set; }

    public int RuleId { get; set; }
    public int QuestionId { get; set; }
    public int AnswerOptionId { get; set; }

    /// <summary>Operador lógico con la siguiente condición: 'AND' | 'OR'</summary>
    [MaxLength(5)]
    public string LogicOperator { get; set; } = "AND";

    public virtual ScoringRule Rule { get; set; } = null!;
    public virtual ScoringQuestion Question { get; set; } = null!;
    public virtual ScoringAnswerOption AnswerOption { get; set; } = null!;
}
