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

    /// <summary>
    /// Tipo de condición:
    ///   "answer"          → respondió una opción en una pregunta del scoring
    ///   "field_filled"    → la variable tiene algún valor capturado
    ///   "field_equals"    → la variable es exactamente igual a ConditionValue
    ///   "field_contains"  → la variable contiene el texto de ConditionValue
    ///   "response_time"   → el lead se contactó en menos de ConditionValue horas
    ///   "prospect_source" → la fuente de prospecto es ConditionValue
    /// </summary>
    [MaxLength(50)]
    public string ConditionType { get; set; } = "answer";

    // ── Para ConditionType = "answer" ──────────────────────────────
    public int? QuestionId { get; set; }
    public int? AnswerOptionId { get; set; }

    // ── Para condiciones de variable / tiempo ──────────────────────
    /// <summary>ID del CustomFieldDefinition a evaluar.</summary>
    public int? FieldId { get; set; }

    /// <summary>
    /// Señal directa del lead (sin pasar por CustomFieldDefinition):
    /// 'email' | 'company' | 'position' | 'city' | 'prospectsource' | 'adname' | 'initialmessage' | 'phone' | 'name'.
    /// La usan las reglas automáticas generadas por IA.
    /// </summary>
    [MaxLength(50)]
    public string? SignalField { get; set; }

    /// <summary>Valor a comparar según el tipo. Ej: "Referido", "5" (horas), "urgente".</summary>
    [MaxLength(500)]
    public string? ConditionValue { get; set; }

    // ── Operador lógico con la SIGUIENTE condición ─────────────────
    /// <summary>'AND' | 'OR'</summary>
    [MaxLength(5)]
    public string LogicOperator { get; set; } = "AND";

    public virtual ScoringRule Rule { get; set; } = null!;
    public virtual ScoringQuestion? Question { get; set; }
    public virtual ScoringAnswerOption? AnswerOption { get; set; }
    public virtual CustomFieldDefinition? Field { get; set; }
}
