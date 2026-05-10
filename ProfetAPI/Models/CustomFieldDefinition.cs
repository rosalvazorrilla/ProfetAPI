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

    /// <summary>
    /// Para campos tipo "select": opciones separadas por coma. Ej: "Opción 1,Opción 2,Opción 3"
    /// </summary>
    [Column("Options")]
    public string? Options { get; set; }

    /// <summary>
    /// Campos de sistema (IDs internos, fechas auto-generadas, etc.) — no aparecen en el wizard.
    /// </summary>
    [Column("IsSystem")]
    public bool IsSystem { get; set; } = false;
}