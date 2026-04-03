using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Dtos.Admin;

public class UpsertProspectSourceDto
{
    [Required]
    /// <example>Facebook Ads</example>
    [SwaggerSchema("Nombre de la fuente de prospectos.", Nullable = false)]
    public string Name { get; set; } = null!;
}

public class ProspectSourceResponseDto
{
    public int SourceId { get; set; }
    public string Name { get; set; } = null!;
}
