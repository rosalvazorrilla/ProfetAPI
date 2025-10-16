using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProfetAPI.Models;

public class CustomFieldDefinition
{
    [Key]
    [Column("FieldId")]
    public int Id { get; set; }

    [Column("FieldCode")]
    public string? Value { get; set; }

    [Column("FieldName")]
    public string? Description { get; set; }

    [Required]
    public string FieldType { get; set; } = "Text";
}