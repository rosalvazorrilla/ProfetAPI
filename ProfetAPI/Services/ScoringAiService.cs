using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Dtos.Scoring;
using ProfetAPI.Models;

namespace ProfetAPI.Services;

public interface IScoringAiService
{
    Task<GeneratedScoringProposalDto> GenerateFromPromptAsync(GenerateQuestionsRequestDto req, CancellationToken ct = default);
    Task<int> SaveModelAsync(SaveScoringModelRequestDto req, CancellationToken ct = default);
    Task<AiScoreResultDto> ScoreLeadAsync(long leadId, CancellationToken ct = default);
    Task<AiScorePersistResult> ScoreAndPersistAsync(long leadId, CancellationToken ct = default);
    bool IsConfigured { get; }
}

public record AiScorePersistResult(bool HasModel, decimal Score, int? TierId, string? TierName, string Reasoning, int Answered, int Pending);

/// <summary>
/// IA de scoring: (F2) genera criterios desde un prompt del dueño, y (F3) en runtime elige
/// las respuestas que se deducen de los datos del lead. NUNCA calcula el score (eso es determinista).
/// </summary>
public class ScoringAiService(
    ApplicationDbContext db, IAiClient ai, ScoringCalculator scoring, ILogger<ScoringAiService> logger) : IScoringAiService
{
    public bool IsConfigured => ai.IsConfigured;

    // ── F2: generar propuesta desde prompt ────────────────────────────────────

    public async Task<GeneratedScoringProposalDto> GenerateFromPromptAsync(GenerateQuestionsRequestDto req, CancellationToken ct = default)
    {
        const string system = """
Eres un experto en calificación de leads B2B. A partir de la descripción del negocio, propones criterios de calificación.
Por cada criterio decide su TIPO:
- Si se puede evaluar automáticamente desde datos que YA trae un lead (email, company, position, city, prospectsource, adname, initialmessage) → créalo como REGLA automática (rules), sin preguntar nada. Prioriza esto.
- Si requiere que el lead responda algo que no se puede deducir → créalo como PREGUNTA (questions) de opción múltiple, con opciones ordenadas de menor a mayor intención y puntos crecientes.
Máximo 6 preguntas y 6 reglas. Propón 3 tiers (Frío/Tibio/Caliente) con rangos coherentes con el puntaje máximo total (preguntas + reglas). Si solo aplican reglas, devuelve questions vacío.
Para reglas: SignalField ∈ {email, company, position, city, prospectsource, adname, initialmessage}; Operator ∈ {equals, contains, not_empty, is_corporate_email, prospect_source}.
Responde en español.
""";

        var schema = """
{"type":"object","additionalProperties":false,"required":["Questions","Rules","Tiers"],"properties":{
"Questions":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["QuestionText","QuestionType","IsRequired","Options"],"properties":{
"QuestionText":{"type":"string"},"QuestionType":{"type":"string"},"IsRequired":{"type":"boolean"},
"Options":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["AnswerText","SuggestedPoints","OrderPosition"],"properties":{"AnswerText":{"type":"string"},"SuggestedPoints":{"type":"number"},"OrderPosition":{"type":"integer"}}}}}}},
"Rules":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["Name","SignalField","Operator","Value","SuggestedPoints"],"properties":{"Name":{"type":"string"},"SignalField":{"type":"string"},"Operator":{"type":"string"},"Value":{"type":["string","null"]},"SuggestedPoints":{"type":"number"}}}},
"Tiers":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["Name","MinScore","MaxScore","Color"],"properties":{"Name":{"type":"string"},"MinScore":{"type":"number"},"MaxScore":{"type":["number","null"]},"Color":{"type":"string"}}}}}}
""";

        var user = $"Negocio: {req.Prompt}" + (string.IsNullOrWhiteSpace(req.Industry) ? "" : $"\nIndustria: {req.Industry}");

        var json = await ai.CompleteJsonAsync(system, user, schema, ct);
        try
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<GeneratedScoringProposalDto>(json, opts) ?? new();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo parsear la propuesta de scoring de la IA");
            return new();
        }
    }

    // ── F2-T4: guardar la propuesta como ScoringModel real ────────────────────

    public async Task<int> SaveModelAsync(SaveScoringModelRequestDto req, CancellationToken ct = default)
    {
        using var tx = await db.Database.BeginTransactionAsync(ct);

        // Un ScoringModel por cuenta: reemplazar el existente
        var model = await db.ScoringModels
            .Include(m => m.Questions).ThenInclude(q => q.AnswerOptions)
            .Include(m => m.Rules).ThenInclude(r => r.Conditions)
            .FirstOrDefaultAsync(m => m.AccountId == req.AccountId, ct);

        if (model != null)
        {
            db.ScoringRuleConditions.RemoveRange(model.Rules.SelectMany(r => r.Conditions));
            db.ScoringRules.RemoveRange(model.Rules);
            db.ScoringAnswerOptions.RemoveRange(model.Questions.SelectMany(q => q.AnswerOptions));
            db.ScoringQuestions.RemoveRange(model.Questions);
            db.LeadTiers.RemoveRange(await db.LeadTiers.Where(t => t.ScoringModelId == model.ScoringModelId).ToListAsync(ct));
            model.Name = req.Name;
        }
        else
        {
            model = new ScoringModel { AccountId = req.AccountId, Name = req.Name };
            db.ScoringModels.Add(model);
        }
        await db.SaveChangesAsync(ct);

        // Preguntas (Tipo A)
        int qOrder = 0;
        foreach (var q in req.Proposal.Questions)
        {
            var question = new ScoringQuestion
            {
                ScoringModelId = model.ScoringModelId,
                QuestionText   = q.QuestionText,
                QuestionType   = string.IsNullOrWhiteSpace(q.QuestionType) ? "SingleChoice" : q.QuestionType,
                IsRequired     = q.IsRequired,
                OrderPosition  = qOrder++,
            };
            db.ScoringQuestions.Add(question);
            await db.SaveChangesAsync(ct);

            int oOrder = 0;
            foreach (var o in q.Options.OrderBy(o => o.OrderPosition))
                db.ScoringAnswerOptions.Add(new ScoringAnswerOption
                {
                    QuestionId    = question.QuestionId,
                    AnswerText    = o.AnswerText,
                    Points        = o.SuggestedPoints,
                    OrderPosition = oOrder++,
                });
        }

        // Reglas automáticas (Tipo B)
        int rOrder = 0;
        foreach (var r in req.Proposal.Rules)
        {
            var rule = new ScoringRule
            {
                ScoringModelId = model.ScoringModelId,
                Name           = r.Name,
                BonusPoints    = r.SuggestedPoints,
                ExecutionOrder = rOrder++,
            };
            db.ScoringRules.Add(rule);
            await db.SaveChangesAsync(ct);

            db.ScoringRuleConditions.Add(new ScoringRuleCondition
            {
                RuleId         = rule.RuleId,
                ConditionType  = MapOperator(r.Operator),
                SignalField    = r.SignalField?.ToLowerInvariant(),
                ConditionValue = r.Value,
                LogicOperator  = "AND",
            });
        }

        // Tiers
        foreach (var t in req.Proposal.Tiers)
            db.LeadTiers.Add(new LeadTier
            {
                ScoringModelId = model.ScoringModelId,
                Name = t.Name, MinScore = t.MinScore, MaxScore = t.MaxScore, Color = t.Color,
            });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return model.ScoringModelId;
    }

    private static string MapOperator(string op) => op switch
    {
        "equals"             => "field_equals",
        "contains"           => "field_contains",
        "not_empty"          => "field_filled",
        "is_corporate_email" => "is_corporate_email",
        "prospect_source"    => "prospect_source",
        _                    => "field_equals",
    };

    // ── F3: scoring en runtime (Claude elige respuestas) ──────────────────────

    public async Task<AiScoreResultDto> ScoreLeadAsync(long leadId, CancellationToken ct = default)
    {
        var lead = await db.Leads.AsNoTracking().FirstOrDefaultAsync(l => l.LeadId == leadId, ct);
        if (lead?.AccountId == null) return new();

        var model = await db.ScoringModels.AsNoTracking()
            .FirstOrDefaultAsync(m => m.AccountId == lead.AccountId.Value, ct);
        if (model == null) return new();

        var questions = await db.ScoringQuestions.AsNoTracking()
            .Where(q => q.ScoringModelId == model.ScoringModelId)
            .OrderBy(q => q.OrderPosition)
            .Select(q => new
            {
                q.QuestionId, q.QuestionText,
                Options = db.ScoringAnswerOptions.Where(o => o.QuestionId == q.QuestionId)
                    .OrderBy(o => o.OrderPosition)
                    .Select(o => new { o.AnswerOptionId, o.AnswerText }).ToList(),
            })
            .ToListAsync(ct);

        if (questions.Count == 0) return new();  // solo reglas automáticas → no hay nada que preguntar a la IA

        const string system = """
Eres un calificador de leads. Para cada pregunta, elige SOLO entre las opciones dadas (por su AnswerOptionId) la que mejor corresponda según los datos del lead.
Si no hay datos suficientes para una pregunta, marca Answerable=false y no inventes (AnswerOptionId=null). Devuelve Confidence 0..1 y una breve razón.
Responde en español.
""";

        var sb = new StringBuilder();
        sb.AppendLine("Datos del lead:");
        sb.AppendLine($"- Nombre: {lead.Name}");
        sb.AppendLine($"- Email: {lead.Email}");
        sb.AppendLine($"- Teléfono: {lead.Phone}");
        sb.AppendLine($"- Empresa: {lead.Company}");
        sb.AppendLine($"- Puesto: {lead.Position}");
        sb.AppendLine($"- Ciudad: {lead.City}");
        sb.AppendLine($"- Fuente: {lead.ProspectSource}");
        sb.AppendLine($"- Anuncio: {lead.AdName}");
        sb.AppendLine($"- Mensaje inicial: {lead.InitialMessage}");
        sb.AppendLine("\nPreguntas y opciones (AnswerOptionId → texto):");
        foreach (var q in questions)
        {
            sb.AppendLine($"[Pregunta {q.QuestionId}] {q.QuestionText}");
            foreach (var o in q.Options) sb.AppendLine($"   {o.AnswerOptionId} → {o.AnswerText}");
        }

        var schema = """
{"type":"object","additionalProperties":false,"required":["Answers","OverallSummary"],"properties":{
"Answers":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["QuestionId","AnswerOptionId","Confidence","Reasoning","Answerable"],"properties":{
"QuestionId":{"type":"integer"},"AnswerOptionId":{"type":["integer","null"]},"Confidence":{"type":"number"},"Reasoning":{"type":["string","null"]},"Answerable":{"type":"boolean"}}}},
"OverallSummary":{"type":"string"}}}
""";

        var json = await ai.CompleteJsonAsync(system, sb.ToString(), schema, ct);
        AiScoreResultDto result;
        try
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            result = JsonSerializer.Deserialize<AiScoreResultDto>(json, opts) ?? new();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo parsear el scoring IA del lead {LeadId}", leadId);
            return new();
        }

        // Validación anti-alucinación: descartar AnswerOptionId que no pertenezcan al modelo
        var validOptionIds = questions.SelectMany(q => q.Options.Select(o => o.AnswerOptionId)).ToHashSet();
        foreach (var a in result.Answers)
            if (a.AnswerOptionId.HasValue && !validOptionIds.Contains(a.AnswerOptionId.Value))
            {
                a.AnswerOptionId = null;
                a.Answerable = false;
            }

        return result;
    }

    /// <summary>
    /// Corre el scoring IA, guarda las respuestas deducibles (Source="AI", sin pisar las Manuales),
    /// recalcula con el motor determinista y persiste score/tier/reasoning. Orquesta F3 completo.
    /// </summary>
    public async Task<AiScorePersistResult> ScoreAndPersistAsync(long leadId, CancellationToken ct = default)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.LeadId == leadId, ct);
        if (lead?.AccountId == null) return new(false, 0, null, null, "", 0, 0);

        var model = await db.ScoringModels.AsNoTracking()
            .FirstOrDefaultAsync(m => m.AccountId == lead.AccountId.Value, ct);
        if (model == null) return new(false, 0, null, null, "", 0, 0);

        var aiResult = await ScoreLeadAsync(leadId, ct);

        var existing = await db.LeadScoringAnswers.Where(a => a.LeadId == leadId).ToListAsync(ct);
        var manualQuestions = existing.Where(a => a.Source == "Manual").Select(a => a.QuestionId).ToHashSet();

        var optionIds = aiResult.Answers.Where(a => a.Answerable && a.AnswerOptionId.HasValue)
            .Select(a => a.AnswerOptionId!.Value).ToList();
        var optionPoints = optionIds.Count > 0
            ? await db.ScoringAnswerOptions.Where(o => optionIds.Contains(o.AnswerOptionId))
                .ToDictionaryAsync(o => o.AnswerOptionId, o => o.Points, ct)
            : new Dictionary<int, decimal>();

        int answered = 0;
        foreach (var a in aiResult.Answers.Where(a => a.Answerable && a.AnswerOptionId.HasValue))
        {
            if (manualQuestions.Contains(a.QuestionId)) continue;  // la corrección humana gana
            db.LeadScoringAnswers.RemoveRange(existing.Where(e => e.QuestionId == a.QuestionId));
            db.LeadScoringAnswers.Add(new LeadScoringAnswer
            {
                LeadId = leadId, QuestionId = a.QuestionId, AnswerOptionId = a.AnswerOptionId,
                PointsAwarded = optionPoints.TryGetValue(a.AnswerOptionId!.Value, out var p) ? p : 0m,
                Source = "AI", Confidence = a.Confidence,
            });
            answered++;
        }
        lead.ScoreReasoning = aiResult.OverallSummary;
        await db.SaveChangesAsync(ct);

        var calc = await scoring.RecomputeAndPersistAsync(leadId);

        var answeredQ = await db.LeadScoringAnswers
            .Where(a => a.LeadId == leadId && a.AnswerOptionId != null)
            .Select(a => a.QuestionId).Distinct().CountAsync(ct);
        var totalQ = await db.ScoringQuestions.CountAsync(q => q.ScoringModelId == model.ScoringModelId, ct);

        return new(true, calc.Total, calc.TierId, calc.TierName, aiResult.OverallSummary, answered, Math.Max(0, totalQ - answeredQ));
    }
}
