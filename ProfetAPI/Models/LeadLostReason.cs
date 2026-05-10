using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

/// <summary>Motivo de pérdida de una cuenta.</summary>
public class LeadLostReason
{
    [Key]
    public int LostReasonId { get; set; }

    public int AccountId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Description { get; set; } = null!;

    public bool CountsForCharts { get; set; } = true;

    public bool IsActive { get; set; } = true;

    public virtual Account Account { get; set; } = null!;
}
