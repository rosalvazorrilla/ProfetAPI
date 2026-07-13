namespace ProfetAPI.Dtos.Scoring;

// ── F2: Generador de criterios desde un prompt ────────────────────────────────

public class GenerateQuestionsRequestDto
{
    public string  Prompt    { get; set; } = "";
    public int     AccountId { get; set; }
    public string? Industry  { get; set; }
}

/// <summary>Propuesta editable: preguntas (Tipo A) + reglas automáticas (Tipo B) + tiers.</summary>
public class GeneratedScoringProposalDto
{
    public List<GeneratedQuestionDto> Questions { get; set; } = new();
    public List<GeneratedRuleDto>     Rules     { get; set; } = new();
    public List<GeneratedTierDto>     Tiers     { get; set; } = new();
}

public class GeneratedQuestionDto
{
    public string QuestionText { get; set; } = "";
    public string QuestionType { get; set; } = "SingleChoice";
    public bool   IsRequired   { get; set; }
    public List<GeneratedOptionDto> Options { get; set; } = new();
}

public class GeneratedOptionDto
{
    public string  AnswerText      { get; set; } = "";
    public decimal SuggestedPoints { get; set; }
    public int     OrderPosition   { get; set; }
}

public class GeneratedRuleDto
{
    public string  Name            { get; set; } = "";
    /// <summary>Señal del lead: email | company | position | city | prospectsource | adname | initialmessage.</summary>
    public string  SignalField     { get; set; } = "";
    /// <summary>equals | contains | not_empty | is_corporate_email | prospect_source.</summary>
    public string  Operator        { get; set; } = "";
    public string? Value           { get; set; }
    public decimal SuggestedPoints { get; set; }
}

public class GeneratedTierDto
{
    public string   Name     { get; set; } = "";
    public decimal  MinScore { get; set; }
    public decimal? MaxScore { get; set; }
    public string?  Color    { get; set; }
}

/// <summary>F2-T4: guardar la propuesta (posiblemente editada) como ScoringModel real.</summary>
public class SaveScoringModelRequestDto
{
    public int    AccountId { get; set; }
    public string Name      { get; set; } = "Modelo de calificación";
    public GeneratedScoringProposalDto Proposal { get; set; } = new();
}

// ── F3: Scoring en runtime (Claude elige respuestas) ──────────────────────────

public class AiScoreResultDto
{
    public List<AiAnswerDto> Answers        { get; set; } = new();
    public string            OverallSummary { get; set; } = "";
}

public class AiAnswerDto
{
    public int     QuestionId     { get; set; }
    public int?    AnswerOptionId { get; set; }
    public decimal Confidence     { get; set; }
    public string? Reasoning      { get; set; }
    public bool    Answerable     { get; set; }
}
