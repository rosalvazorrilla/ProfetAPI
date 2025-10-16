using System.ComponentModel.DataAnnotations;
namespace ProfetAPI.Models;

public class ScoringRule
{
    [Key]
    public int RuleId { get; set; }
    public int ScoringModelId { get; set; }
    public int? ConditionQuestionId { get; set; }
    public int? ConditionAnswerOptionId { get; set; }
    public string ActionType { get; set; } = null!; // Ej: 'ADD_POINTS'
    public string ActionValue { get; set; } = null!; // Ej: '50'
    public int ExecutionOrder { get; set; }

    public virtual ScoringModel ScoringModel { get; set; } = null!;
    public virtual ScoringQuestion? ConditionQuestion { get; set; }
    public virtual ScoringAnswerOption? ConditionAnswerOption { get; set; }
}