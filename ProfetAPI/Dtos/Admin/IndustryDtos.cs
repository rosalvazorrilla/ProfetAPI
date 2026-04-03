using Swashbuckle.AspNetCore.Annotations;

namespace ProfetAPI.Dtos.Admin;

/// <summary>Datos para crear o actualizar un sector de industria.</summary>
public class UpsertIndustryDto
{
    /// <example>Automotriz</example>
    [SwaggerSchema("Nombre en español del sector.", Nullable = false)]
    public string? NameES { get; set; }

    /// <example>Automotive</example>
    [SwaggerSchema("Nombre en inglés del sector.")]
    public string? NameEN { get; set; }
}

/// <summary>Respuesta con el detalle de un sector de industria.</summary>
public class IndustryResponseDto
{
    [SwaggerSchema("ID único del sector.")]
    public long Id { get; set; }

    [SwaggerSchema("Nombre en español.")]
    public string? NameES { get; set; }

    [SwaggerSchema("Nombre en inglés.")]
    public string? NameEN { get; set; }
}
