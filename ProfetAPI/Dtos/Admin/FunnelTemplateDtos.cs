using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Dtos.Admin;

// ── FunnelTemplate ──────────────────────────────────────────────

public class CreateFunnelTemplateDto
{
    [Required]
    /// <example>Pipeline B2B Estándar</example>
    [SwaggerSchema("Nombre de la plantilla de embudo.", Nullable = false)]
    public string Name { get; set; } = null!;

    /// <example>Embudo genérico para ventas B2B con 4 etapas.</example>
    [SwaggerSchema("Descripción opcional.")]
    public string? Description { get; set; }

    [SwaggerSchema("Lista de etapas iniciales (opcional, se pueden agregar después).")]
    public List<UpsertFunnelTemplateStageDto> Stages { get; set; } = new();
}

public class UpdateFunnelTemplateDto
{
    [Required]
    [SwaggerSchema("Nuevo nombre de la plantilla.")]
    public string Name { get; set; } = null!;

    [SwaggerSchema("Nueva descripción.")]
    public string? Description { get; set; }
}

public class FunnelTemplateResponseDto
{
    [SwaggerSchema("ID de la plantilla.")]
    public int TemplateId { get; set; }

    [SwaggerSchema("Nombre de la plantilla.")]
    public string Name { get; set; } = null!;

    [SwaggerSchema("Descripción.")]
    public string? Description { get; set; }

    [SwaggerSchema("Etapas de la plantilla ordenadas por posición.")]
    public List<FunnelTemplateStageResponseDto> Stages { get; set; } = new();
}

// ── FunnelTemplateStage ─────────────────────────────────────────

public class UpsertFunnelTemplateStageDto
{
    [Required]
    /// <example>Prospecto</example>
    [SwaggerSchema("Nombre de la etapa.", Nullable = false)]
    public string StageName { get; set; } = null!;

    /// <example>1</example>
    [SwaggerSchema("Posición/orden de la etapa dentro del embudo.")]
    public int Order { get; set; }
}

public class FunnelTemplateStageResponseDto
{
    public int TemplateStageId { get; set; }
    public string StageName { get; set; } = null!;
    public int Order { get; set; }
}
