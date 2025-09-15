using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProfetAPI.Models
{
    public class Subscription
    {
        [Key]
        public int SubscriptionId { get; set; }
        public int CustomerId { get; set; }
        public int PlanId { get; set; }

        [Required]
        [StringLength(50)]
        public string Status { get; set; } // "Trialing", "Active", "Canceled"

        [Column(TypeName = "decimal(18, 2)")]
        public decimal PriceAgreed { get; set; }

        [Required]
        [StringLength(50)]
        public string BillingCycle { get; set; } // "Monthly", "Annually"

        [Column(TypeName = "decimal(18, 2)")]
        public decimal DiscountAmount { get; set; }

        public DateTime? TrialEndDate { get; set; }
        public DateTime SubscriptionStartDate { get; set; }
        public DateTime? CanceledDate { get; set; }

        // Propiedades de navegación
        public virtual Customer Customer { get; set; }
        public virtual Plan Plan { get; set; }
        public virtual ICollection<SubscriptionPeriod> Periods { get; set; } = new List<SubscriptionPeriod>();
        public virtual ICollection<CustomerPurchasedAddOn> PurchasedAddOns { get; set; } = new List<CustomerPurchasedAddOn>();
    }
}
