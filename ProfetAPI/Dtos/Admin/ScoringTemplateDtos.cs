using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Dtos.Admin;

// ── ScoringTemplate ─────────────────────────────────────────────

public class CreateScoringTemplateDto
{
    [Required]
    /// <example>Calificación Automotriz B2B</example>
    [SwaggerSchema("Nombre del template de calificación.", Nullable = false)]
    public string Name { get; set; } = null!;

    [SwaggerSchema("Descripción del template.")]
    public string? Description { get; set; }

    /// <example>3</example>
    [SwaggerSchema("ID de la industria a la que aplica. NULL = genérico para cualquier industria.")]
    public long? IndustryId { get; set; }

    /// <example>1</example>
    [SwaggerSchema("ID de la categoría del template (opcional).")]
    public int? CategoryId { get; set; }

    [SwaggerSchema("Preguntas del template (se pueden agregar después).")]
    public List<CreateScoringTemplateQuestionDto> Questions { get; set; } = new();
}

public class UpdateScoringTemplateDto
{
    [Required]
    [SwaggerSchema("Nuevo nombre del template.")]
    public string Name { get; set; } = null!;

    [SwaggerSchema("Nueva descripción.")]
    public string? Description { get; set; }

    [SwaggerSchema("Nueva industria asociada.")]
    public long? IndustryId { get; set; }

    [SwaggerSchema("Nueva categoría.")]
    public int? CategoryId { get; set; }
}

public class ScoringTemplateResponseDto
{
    public int TemplateId { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public long? IndustryId { get; set; }
    public string? IndustryName { get; set; }
    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public List<ScoringTemplateQuestionResponseDto> Questions { get; set; } = new();
}

// ── ScoringTemplateQuestion ─────────────────────────────────────

public class CreateScoringTemplateQuestionDto
{
    [Required]
    /// <example>¿Cuenta con presupuesto definido?</example>
    [SwaggerSchema("Texto de la pregunta.", Nullable = false)]
    public string QuestionText { get; set; } = null!;

    /// <example>SingleChoice</example>
    [SwaggerSchema("Tipo: SingleChoice | MultiChoice | OpenText | Numeric.")]
    public string QuestionType { get; set; } = "SingleChoice";

    [SwaggerSchema("¿Es obligatoria esta pregunta?")]
    public bool IsRequired { get; set; } = false;

    [SwaggerSchema("Posición en el formulario.")]
    public int OrderPosition { get; set; } = 0;

    [SwaggerSchema("Opciones de respuesta (requerido para SingleChoice y MultiChoice).")]
    public List<CreateScoringTemplateAnswerOptionDto> AnswerOptions { get; set; } = new();
}

public class UpdateScoringTemplateQuestionDto
{
    [Required]
    [SwaggerSchema("Nuevo texto de la pregunta.")]
    public string QuestionText { get; set; } = null!;

    [SwaggerSchema("Nuevo tipo.")]
    public string QuestionType { get; set; } = "SingleChoice";

    [SwaggerSchema("¿Es obligatoria?")]
    public bool IsRequired { get; set; } = false;

    [SwaggerSchema("Nueva posición.")]
    public int OrderPosition { get; set; } = 0;
}

public class ScoringTemplateQuestionResponseDto
{
    public int TemplateQuestionId { get; set; }
    public string QuestionText { get; set; } = null!;
    public string QuestionType { get; set; } = null!;
    public bool IsRequired { get; set; }
    public int OrderPosition { get; set; }
    public List<ScoringTemplateAnswerOptionResponseDto> AnswerOptions { get; set; } = new();
}

// ── ScoringTemplateAnswerOption ──────────────────────────────────

public class CreateScoringTemplateAnswerOptionDto
{
    [Required]
    /// <example>Sí, tiene presupuesto definido</example>
    [SwaggerSchema("Texto de la respuesta.", Nullable = false)]
    public string AnswerText { get; set; } = null!;

    /// <example>30</example>
    [SwaggerSchema("Puntos que otorga esta respuesta al score del lead.")]
    public decimal Points { get; set; } = 0;

    [SwaggerSchema("Posición visual de la respuesta.")]
    public int OrderPosition { get; set; } = 0;
}

public class UpdateScoringTemplateAnswerOptionDto
{
    [Required]
    [SwaggerSchema("Nuevo texto de la respuesta.")]
    public string AnswerText { get; set; } = null!;

    [SwaggerSchema("Nuevos puntos.")]
    public decimal Points { get; set; } = 0;

    [SwaggerSchema("Nueva posición.")]
    public int OrderPosition { get; set; } = 0;
}

public class ScoringTemplateAnswerOptionResponseDto
{
    public int TemplateAnswerId { get; set; }
    public string AnswerText { get; set; } = null!;
    public decimal Points { get; set; }
    public int OrderPosition { get; set; }
}
