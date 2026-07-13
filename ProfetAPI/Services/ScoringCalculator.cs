using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;

namespace ProfetAPI.Services;

/// <summary>
/// Motor determinista de scoring: suma puntos de respuestas + reglas automáticas,
/// resuelve el tier y persiste el resultado en el Lead. La IA NO calcula aquí; solo
/// elige respuestas (que ya vienen con sus puntos). Fuente de verdad del cálculo.
/// </summary>
public class ScoringCalculator(ApplicationDbContext db)
{
    public record ScoreResult(bool HasModel, decimal Total, int? TierId, string? TierName, string Source);

    /// <summary>Busca la banda (LeadTier) donde cae el puntaje total para un ScoringModel.</summary>
    public async Task<int?> ResolveTierIdAsync(int scoringModelId, decimal totalPoints)
    {
        var tiers = await db.LeadTiers.AsNoTracking()
            .Where(t => t.ScoringModelId == scoringModelId)
            .OrderByDescending(t => t.MinScore)
            .ToListAsync();

        var match = tiers.FirstOrDefault(t =>
            totalPoints >= t.MinScore && (t.MaxScore == null || totalPoints <= t.MaxScore.Value));
        return match?.TierId;
    }

    /// <summary>
    /// Evalúa las reglas automáticas (Tipo B) contra los datos que ya trae el lead,
    /// SIN preguntarle nada. Devuelve la suma de BonusPoints de las reglas que se cumplen.
    /// </summary>
    public async Task<decimal> EvaluateAutomaticRulesAsync(long leadId, int scoringModelId)
    {
        var lead = await db.Leads.AsNoTracking().FirstOrDefaultAsync(l => l.LeadId == leadId);
        if (lead == null) return 0m;

        var rules = await db.ScoringRules.AsNoTracking()
            .Where(r => r.ScoringModelId == scoringModelId)
            .Include(r => r.Conditions)
            .ToListAsync();
        if (rules.Count == 0) return 0m;

        // Señales del lead accesibles por FieldCode (sin acentos, minúsculas)
        var signals = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = lead.Name, ["email"] = lead.Email, ["phone"] = lead.Phone,
            ["company"] = lead.Company, ["position"] = lead.Position, ["city"] = lead.City,
            ["prospectsource"] = lead.ProspectSource, ["adname"] = lead.AdName,
            ["initialmessage"] = lead.InitialMessage, ["comments"] = lead.InitialMessage,
        };

        // Mapa FieldId → FieldCode para las condiciones basadas en variable
        var fieldIds = rules.SelectMany(r => r.Conditions)
            .Where(c => c.FieldId.HasValue).Select(c => c.FieldId!.Value).Distinct().ToList();
        var fieldCodes = fieldIds.Count == 0
            ? new Dictionary<int, string?>()
            : await db.CustomFieldDefinitions.AsNoTracking()
                .Where(f => fieldIds.Contains(f.FieldId))
                .ToDictionaryAsync(f => f.FieldId, f => f.FieldCode);

        // Respuestas ya dadas (para condiciones tipo "answer")
        var answers = await db.LeadScoringAnswers.AsNoTracking()
            .Where(a => a.LeadId == leadId)
            .Select(a => new { a.QuestionId, a.AnswerOptionId }).ToListAsync();

        decimal total = 0m;
        foreach (var rule in rules)
        {
            var conds = rule.Conditions.OrderBy(c => c.ConditionId).ToList();
            if (conds.Count == 0) continue;

            bool acc = EvalCondition(conds[0], signals, fieldCodes, answers);
            for (int i = 1; i < conds.Count; i++)
            {
                var next = EvalCondition(conds[i], signals, fieldCodes, answers);
                acc = conds[i - 1].LogicOperator == "OR" ? (acc || next) : (acc && next);
            }
            if (acc) total += rule.BonusPoints;
        }
        return total;
    }

    private static bool EvalCondition(
        Models.ScoringRuleCondition c,
        Dictionary<string, string?> signals,
        Dictionary<int, string?> fieldCodes,
        IEnumerable<dynamic> answers)
    {
        string? FieldValue()
        {
            // Preferir la señal directa (reglas IA); si no, resolver por CustomFieldDefinition
            if (!string.IsNullOrWhiteSpace(c.SignalField) && signals.TryGetValue(c.SignalField!, out var sv)) return sv;
            if (c.FieldId.HasValue && fieldCodes.TryGetValue(c.FieldId.Value, out var code) && code != null
                && signals.TryGetValue(code, out var v)) return v;
            return null;
        }

        switch (c.ConditionType)
        {
            case "answer":
                return c.QuestionId.HasValue && c.AnswerOptionId.HasValue &&
                       answers.Any(a => a.QuestionId == c.QuestionId.Value && a.AnswerOptionId == c.AnswerOptionId.Value);

            case "prospect_source":
                return !string.IsNullOrWhiteSpace(signals["prospectsource"]) &&
                       string.Equals(signals["prospectsource"], c.ConditionValue, StringComparison.OrdinalIgnoreCase);

            case "field_filled":
            case "not_empty":
                return !string.IsNullOrWhiteSpace(FieldValue());

            case "field_equals":
            case "equals":
                return string.Equals(FieldValue(), c.ConditionValue, StringComparison.OrdinalIgnoreCase);

            case "field_contains":
            case "contains":
                var val = FieldValue();
                return !string.IsNullOrWhiteSpace(val) && !string.IsNullOrWhiteSpace(c.ConditionValue) &&
                       val!.Contains(c.ConditionValue!, StringComparison.OrdinalIgnoreCase);

            case "is_corporate_email":
                return IsCorporateEmail(FieldValue() ?? signals["email"]);

            default:
                return false; // "response_time" u otros no soportados aún → no suma
        }
    }

    private static readonly HashSet<string> FreeEmailDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "gmail.com", "hotmail.com", "outlook.com", "yahoo.com", "yahoo.com.mx", "live.com",
        "icloud.com", "aol.com", "me.com", "protonmail.com", "gmx.com", "msn.com", "hotmail.es",
    };

    /// <summary>true si el correo tiene dominio corporativo (no un proveedor gratuito).</summary>
    private static bool IsCorporateEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        var at = email.LastIndexOf('@');
        if (at < 0 || at == email.Length - 1) return false;
        var domain = email[(at + 1)..].Trim();
        return !FreeEmailDomains.Contains(domain);
    }

    /// <summary>
    /// Recalcula el score total del lead (respuestas + reglas), resuelve el tier y persiste
    /// en el Lead (Score, TierId, ScoredAt, ScoreSource). El source se deriva del origen de las respuestas.
    /// </summary>
    public async Task<ScoreResult> RecomputeAndPersistAsync(long leadId)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.LeadId == leadId);
        if (lead?.AccountId == null) return new ScoreResult(false, 0m, null, null, "Manual");

        var modelId = await db.ScoringModels.AsNoTracking()
            .Where(m => m.AccountId == lead.AccountId.Value)
            .Select(m => (int?)m.ScoringModelId).FirstOrDefaultAsync();
        if (modelId == null) return new ScoreResult(false, 0m, null, null, "Manual");

        var answerPoints = await db.LeadScoringAnswers
            .Where(a => a.LeadId == leadId).SumAsync(a => (decimal?)a.PointsAwarded) ?? 0m;
        var rulePoints = await EvaluateAutomaticRulesAsync(leadId, modelId.Value);
        var total = answerPoints + rulePoints;

        var tierId = await ResolveTierIdAsync(modelId.Value, total);
        var tierName = tierId.HasValue
            ? await db.LeadTiers.AsNoTracking().Where(t => t.TierId == tierId.Value).Select(t => t.Name).FirstOrDefaultAsync()
            : null;

        // Derivar el origen: Hybrid si hay mezcla de Manual + AI
        var sources = await db.LeadScoringAnswers.AsNoTracking()
            .Where(a => a.LeadId == leadId).Select(a => a.Source).ToListAsync();
        var hasAI = sources.Any(s => s == "AI");
        var hasManual = sources.Any(s => s == "Manual");
        var source = hasAI && hasManual ? "Hybrid" : hasAI ? "AI" : "Manual";

        lead.Score       = total;
        lead.TierId      = tierId;
        lead.ScoredAt    = DateTime.UtcNow;
        lead.ScoreSource = source;
        await db.SaveChangesAsync();

        return new ScoreResult(true, total, tierId, tierName, source);
    }
}
