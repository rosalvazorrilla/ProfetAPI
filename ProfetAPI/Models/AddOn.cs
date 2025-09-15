using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models
{
    public class AddOn
    {
        [Key]
        public int AddOnId { get; set; }

        public int FeatureId { get; set; }

        [Required]
        [StringLength(255)]
        public string Name { get; set; }

        [StringLength(1000)]
        public string? Description { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal Price { get; set; }

        [Required]
        [StringLength(50)]
        public string BillingCycle { get; set; } // Ej: "OneTime", "Monthly"

        // Propiedades de navegación
        public virtual Feature Feature { get; set; }
        public virtual ICollection<CustomerPurchasedAddOn> PurchasedInstances { get; set; } = new List<CustomerPurchasedAddOn>();

    }
}
