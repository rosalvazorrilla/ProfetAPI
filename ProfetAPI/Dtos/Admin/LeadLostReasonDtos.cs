using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Dtos.Admin;

public class CreateLeadLostReasonDto
{
    [Required]
    /// <example>Fuera de presupuesto</example>
    [SwaggerSchema("Descripción del motivo de pérdida.", Nullable = false)]
    public string Description { get; set; } = null!;

    /// <example>true</example>
    [SwaggerSchema("Indica si este motivo impacta en métricas de tasa de conversión.")]
    public bool ConversionRate { get; set; } = false;
}

public class UpdateLeadLostReasonDto
{
    [Required]
    [SwaggerSchema("Nueva descripción del motivo.")]
    public string Description { get; set; } = null!;

    [SwaggerSchema("Nuevo valor de impacto en conversión.")]
    public bool ConversionRate { get; set; }

    [SwaggerSchema("Activar o desactivar el motivo en el catálogo global.")]
    public bool IsActive { get; set; } = true;
}

public class LeadLostReasonResponseDto
{
    public int Id { get; set; }
    public string Description { get; set; } = null!;
    public bool ConversionRate { get; set; }
    public bool IsActive { get; set; }
}
