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
    public List<CreateScoringQuestionDto> Questions { get; set; } = new();
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

// Respuesta ligera para listado (sin preguntas)
public class ScoringTemplateSummaryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public long? IndustryId { get; set; }
    public string? IndustryName { get; set; }
    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public int QuestionCount { get; set; }
}

// Respuesta completa para detalle (con preguntas y respuestas)
public class ScoringTemplateResponseDto
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public long? IndustryId { get; set; }
    public string? IndustryName { get; set; }
    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public List<ScoringQuestionResponseDto> Questions { get; set; } = new();
}

// ── ScoringTemplateQuestion ─────────────────────────────────────

public class CreateScoringQuestionDto
{
    [Required]
    /// <example>¿Cuenta con presupuesto definido?</example>
    [SwaggerSchema("Texto de la pregunta.", Nullable = false)]
    public string Text { get; set; } = null!;

    [SwaggerSchema("Posición en el formulario.")]
    public int Order { get; set; } = 0;

    [SwaggerSchema("Tipo: SingleChoice | MultiChoice | OpenText | Numeric.")]
    public string QuestionType { get; set; } = "SingleChoice";

    [SwaggerSchema("¿Es obligatoria esta pregunta?")]
    public bool IsRequired { get; set; } = false;

    [SwaggerSchema("Opciones de respuesta (se pueden agregar después).")]
    public List<CreateScoringAnswerDto> Answers { get; set; } = new();
}

public class UpdateScoringQuestionDto
{
    [Required]
    [SwaggerSchema("Nuevo texto de la pregunta.")]
    public string Text { get; set; } = null!;

    [SwaggerSchema("Nueva posición.")]
    public int Order { get; set; } = 0;

    [SwaggerSchema("Nuevo tipo.")]
    public string QuestionType { get; set; } = "SingleChoice";

    [SwaggerSchema("¿Es obligatoria?")]
    public bool IsRequired { get; set; } = false;
}

public class ScoringQuestionResponseDto
{
    public int Id { get; set; }
    public string Text { get; set; } = null!;
    public int Order { get; set; }
    public string QuestionType { get; set; } = null!;
    public bool IsRequired { get; set; }
    public List<ScoringAnswerResponseDto> Answers { get; set; } = new();
}

// ── ScoringTemplateAnswerOption ──────────────────────────────────

public class CreateScoringAnswerDto
{
    [Required]
    /// <example>Sí, tiene presupuesto definido</example>
    [SwaggerSchema("Texto de la respuesta.", Nullable = false)]
    public string Text { get; set; } = null!;

    /// <example>30</example>
    [SwaggerSchema("Puntos que otorga esta respuesta al score del lead.")]
    public decimal Score { get; set; } = 0;

    [SwaggerSchema("Posición visual de la respuesta.")]
    public int Order { get; set; } = 0;
}

public class UpdateScoringAnswerDto
{
    [Required]
    [SwaggerSchema("Nuevo texto de la respuesta.")]
    public string Text { get; set; } = null!;

    [SwaggerSchema("Nuevos puntos.")]
    public decimal Score { get; set; } = 0;

    [SwaggerSchema("Nueva posición.")]
    public int Order { get; set; } = 0;
}

public class ScoringAnswerResponseDto
{
    public int Id { get; set; }
    public string Text { get; set; } = null!;
    public decimal Score { get; set; }
    public int Order { get; set; }
}
