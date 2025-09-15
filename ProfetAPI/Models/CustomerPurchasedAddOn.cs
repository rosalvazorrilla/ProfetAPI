using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models
{
    public class CustomerPurchasedAddOn
    {
        [Key]
        public int PurchasedAddOnId { get; set; }
        public int SubscriptionId { get; set; }
        public int AddOnId { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal PricePaid { get; set; }

        public DateTime PurchaseDate { get; set; }
        public DateTime? ExpiryDate { get; set; }

        // Propiedades de navegación
        public virtual Subscription Subscription { get; set; }
        public virtual AddOn AddOn { get; set; }
    }
}
