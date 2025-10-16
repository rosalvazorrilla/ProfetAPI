using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

public class ChartType
{
    [Key]
    public int ChartTypeId { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = null!;

    [Required]
    [StringLength(50)]
    public string Code { get; set; } = null!;
}