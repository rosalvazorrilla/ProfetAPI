using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProfetAPI.Models;

public class CustomFieldDefinition
{
    [Key]
    [Column("FieldId")]
    public int FieldId { get; set; }

    [Column("FieldCode")]
    public string? FieldCode { get; set; }

    [Column("FieldName")]
    public string? FieldName { get; set; }

    [Required]
    public string FieldType { get; set; } = "Text";
}