using System.ComponentModel.DataAnnotations;
using Swashbuckle.AspNetCore.Annotations;

namespace ProfetAPI.Dtos.Admin;

// ── Accounts ──────────────────────────────────────────────────────────────────

public class AdminAccountResponseDto
{
    public int AccountId { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Status { get; set; } = null!;
    public string AssignmentType { get; set; } = null!;
}

public class CreateAdminAccountDto
{
    [Required]
    [SwaggerSchema("Nombre de la cuenta.", Nullable = false)]
    public string Name { get; set; } = null!;

    [SwaggerSchema("Descripción opcional.")]
    public string? Description { get; set; }

    [SwaggerSchema("Tipo de asignación de leads: 'Carrusel' | 'Manual'.")]
    public string AssignmentType { get; set; } = "Carrusel";
}

public class UpdateAdminAccountDto
{
    [Required]
    [SwaggerSchema("Nuevo nombre de la cuenta.")]
    public string Name { get; set; } = null!;

    [SwaggerSchema("Nueva descripción.")]
    public string? Description { get; set; }

    [SwaggerSchema("Tipo de asignación.")]
    public string AssignmentType { get; set; } = "Carrusel";
}

// ── Funnel ────────────────────────────────────────────────────────────────────

public class AdminFunnelResponseDto
{
    public int FunnelId { get; set; }
    public string Name { get; set; } = null!;
    public int? OriginatingTemplateId { get; set; }
    public List<AdminStageResponseDto> Stages { get; set; } = new();
}

public class AdminStageResponseDto
{
    public int StageId { get; set; }
    public string Name { get; set; } = null!;
    public int Order { get; set; }
    public string? Color { get; set; }
}

public class SetAdminFunnelDto
{
    [SwaggerSchema("ID de la plantilla a clonar. Si es null se usan las etapas del array Stages.")]
    public int? TemplateId { get; set; }

    [SwaggerSchema("Etapas personalizadas (requerido si TemplateId es null).")]
    public List<AdminStageInputDto> Stages { get; set; } = new();
}

public class AdminStageInputDto
{
    [SwaggerSchema("ID de la etapa a actualizar. Null = nueva etapa.")]
    public int? StageId { get; set; }

    [Required]
    public string Name { get; set; } = null!;

    public int Order { get; set; }
    public string? Color { get; set; }
}

// ── Scoring ───────────────────────────────────────────────────────────────────

public class AdminScoringResponseDto
{
    public int ScoringModelId { get; set; }
    public string ModelName { get; set; } = null!;
    public int? OriginatingTemplateId { get; set; }
    public List<AdminScoringQuestionResponseDto> Questions { get; set; } = new();
    public List<AdminTierResponseDto> Tiers { get; set; } = new();
}

public class AdminScoringQuestionResponseDto
{
    public int QuestionId { get; set; }
    public string QuestionText { get; set; } = null!;
    public string QuestionType { get; set; } = null!;
    public bool IsRequired { get; set; }
    public int OrderPosition { get; set; }
    public List<AdminAnswerOptionResponseDto> AnswerOptions { get; set; } = new();
}

public class AdminAnswerOptionResponseDto
{
    public int AnswerOptionId { get; set; }
    public string AnswerText { get; set; } = null!;
    public decimal Points { get; set; }
    public int OrderPosition { get; set; }
}

public class AdminTierResponseDto
{
    public int TierId { get; set; }
    public string Name { get; set; } = null!;
    public decimal MinScore { get; set; }
    public decimal? MaxScore { get; set; }
    public string? Color { get; set; }
}

public class SetAdminScoringDto
{
    [SwaggerSchema("ID del ScoringTemplate a clonar. Si es null se usan las preguntas manuales.")]
    public int? TemplateId { get; set; }

    public string ModelName { get; set; } = "Modelo de Calificación";
}

// ── Industries ────────────────────────────────────────────────────────────────

public class AdminAccountIndustriesDto
{
    [Required]
    public List<long> IndustryIds { get; set; } = new();
}

// ── Catalogs ──────────────────────────────────────────────────────────────────

public class AdminCatalogsResponseDto
{
    public List<AdminLostReasonItemDto> LostReasons { get; set; } = new();
}

public class AdminLostReasonItemDto
{
    public int Id { get; set; }
    public string Description { get; set; } = null!;
}

public class SetAdminCatalogsDto
{
    [SwaggerSchema("IDs de los templates de motivos a asignar a la cuenta.")]
    public List<int> TemplateIds { get; set; } = new();
}

// ── Users ─────────────────────────────────────────────────────────────────────

public class AdminAccountUserResponseDto
{
    public string UserId { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public string Role { get; set; } = null!;
    public bool Active { get; set; }
    public string? RoleInAccount { get; set; }
}

public class CreateAdminUserDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = null!;

    [Required]
    public string Password { get; set; } = null!;

    [Required]
    public string FirstName { get; set; } = null!;

    public string? LastName { get; set; }
    public string? Phone { get; set; }

    [SwaggerSchema("'AccountAdmin' | 'SalesRep' | 'Viewer'")]
    public string Role { get; set; } = "SalesRep";
}

public class AssignAdminUserDto
{
    [Required]
    public string UserId { get; set; } = null!;

    [SwaggerSchema("'Admin' | 'SalesRep' | 'Viewer'")]
    public string RoleInAccount { get; set; } = "SalesRep";
}
