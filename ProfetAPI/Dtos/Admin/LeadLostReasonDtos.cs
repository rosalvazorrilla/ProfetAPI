using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Dtos.Admin;

public class CreateLeadLostReasonDto
{
    [Required]
    [SwaggerSchema("Descripción del motivo de pérdida.", Nullable = false)]
    public string Description { get; set; } = null!;

    [SwaggerSchema("Indica si este motivo cuenta para gráficas de conversión.")]
    public bool CountsForCharts { get; set; } = true;
}

public class UpdateLeadLostReasonDto
{
    [Required]
    [SwaggerSchema("Nueva descripción del motivo.")]
    public string Description { get; set; } = null!;

    [SwaggerSchema("Nuevo valor de impacto en conversión.")]
    public bool CountsForCharts { get; set; }

    [SwaggerSchema("Activar o desactivar el motivo.")]
    public bool IsActive { get; set; } = true;
}

public class LeadLostReasonResponseDto
{
    public int TemplateId { get; set; }
    public string Description { get; set; } = null!;
    public bool CountsForCharts { get; set; }
    public bool IsActive { get; set; }
}
