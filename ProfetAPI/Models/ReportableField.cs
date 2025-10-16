using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

public class ReportableField
{
    [Key]
    public int FieldId { get; set; }

    [Required]
    [StringLength(255)]
    public string DisplayName { get; set; } = null!;

    [Required]
    [StringLength(255)]
    public string TechnicalName { get; set; } = null!;

    [Required]
    [StringLength(50)]
    public string FieldType { get; set; } = null!; // 'Metrica' o 'Dimension'

    [StringLength(50)]
    public string? AggregationType { get; set; } // 'SUM', 'COUNT', 'AVG'
}