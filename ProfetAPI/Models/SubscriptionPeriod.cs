using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models
{
    public class SubscriptionPeriod
    {
        [Key]
        public int PeriodId { get; set; }
        public int SubscriptionId { get; set; }
        public DateTime PeriodStartDate { get; set; }
        public DateTime PeriodEndDate { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal AmountBilled { get; set; }

        [Required]
        [StringLength(50)]
        public string Status { get; set; } // "Paid", "Unpaid"

        public DateTime? PaymentDate { get; set; }

        [StringLength(500)]
        public string? InvoiceUrl { get; set; }

        // Propiedad de navegación
        public virtual Subscription Subscription { get; set; }
    }
}
