using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Dtos.Admin;

public class CreateCustomFieldDefinitionDto
{
    [Required]
    /// <example>nse</example>
    [SwaggerSchema("Código interno del campo (snake_case). Ej: nse, buying_time.", Nullable = false)]
    public string FieldCode { get; set; } = null!;

    [Required]
    /// <example>NSE</example>
    [SwaggerSchema("Nombre visible del campo en el UI.", Nullable = false)]
    public string FieldName { get; set; } = null!;

    /// <example>Text</example>
    [SwaggerSchema("Tipo de dato: Text | Number | Date | Boolean | Select.")]
    public string FieldType { get; set; } = "Text";
}

public class UpdateCustomFieldDefinitionDto
{
    [Required]
    [SwaggerSchema("Nuevo nombre visible del campo.")]
    public string FieldName { get; set; } = null!;

    [SwaggerSchema("Nuevo tipo de dato.")]
    public string FieldType { get; set; } = "Text";
}

public class CustomFieldDefinitionResponseDto
{
    public int Id { get; set; }

    [SwaggerSchema("Código interno del campo.")]
    public string? FieldCode { get; set; }

    [SwaggerSchema("Nombre visible del campo.")]
    public string? FieldName { get; set; }

    [SwaggerSchema("Tipo de dato.")]
    public string FieldType { get; set; } = "Text";
}
