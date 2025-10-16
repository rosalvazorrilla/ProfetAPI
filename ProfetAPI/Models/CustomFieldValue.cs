using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

public class CustomFieldValue
{
    [Key]
    public int ValueId { get; set; }
    public long EntityId { get; set; }
    public string EntityType { get; set; } = null!; // "Deal", "Contact", etc.
    public int FieldId { get; set; }
    public string? Value { get; set; }

    public virtual CustomFieldDefinition CustomFieldDefinition { get; set; } = null!;
}