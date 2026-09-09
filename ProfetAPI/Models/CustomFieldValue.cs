using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProfetAPI.Models;

public class CustomFieldValue
{
    [Key]
    public int ValueId { get; set; }
    public long EntityId { get; set; }
    public string EntityType { get; set; } = null!; // "Deal", "Contact", etc.
    public int FieldId { get; set; }
    public string? Value { get; set; }

    // Sin este [ForeignKey] explícito, EF no reconoce FieldId como la FK real
    // hacia CustomFieldDefinition (su PK también se llama "FieldId", así que
    // por convención EF crea una FK sombra "CustomFieldDefinitionFieldId" que
    // no existe en la tabla — rompía GetVariables con "Invalid column name".
    [ForeignKey(nameof(FieldId))]
    public virtual CustomFieldDefinition CustomFieldDefinition { get; set; } = null!;
}