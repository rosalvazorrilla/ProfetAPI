using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models
{
    public class Feature
    {
        [Key]
        public int FeatureId { get; set; }

        // Código único usado en el código para verificar permisos (ej: "API_ACCESS")
        [Required]
        [StringLength(100)]
        public string FeatureCode { get; set; }

        [Required]
        [StringLength(255)]
        public string Name { get; set; }

        [StringLength(1000)]
        public string? Description { get; set; }

        // Propiedades de navegación
        public virtual ICollection<PlanFeature> PlanFeatures { get; set; } = new List<PlanFeature>();
        public virtual ICollection<AddOn> AddOns { get; set; } = new List<AddOn>();
    }
}
