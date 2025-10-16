using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace ProfetAPI.Models;

public class DealPayment
{
    [Key]
    public int PaymentId { get; set; }
    public int DealId { get; set; }
    [Column(TypeName = "decimal(18, 2)")]
    public decimal? Amount { get; set; }
    public DateTime? PaymentDate { get; set; }
    public string? Description { get; set; }

    public virtual Deal Deal { get; set; } = null!;
}