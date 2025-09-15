using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models
{
    public class Plan
    {
        [Key]
        public int PlanId { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; }

        [StringLength(1000)]
        public string? Description { get; set; }

        public bool IsPublic { get; set; }
        public bool IsActive { get; set; }

        // Propiedades de navegación
        public virtual ICollection<PlanPriceHistory> PriceHistory { get; set; } = new List<PlanPriceHistory>();
        public virtual ICollection<PlanFeature> PlanFeatures { get; set; } = new List<PlanFeature>();
        public virtual ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
    }
}