using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProfetAPI.Models
{
    public class PlanFeature
    {
        public int PlanId { get; set; }
        public int FeatureId { get; set; }

        public string? Limit { get; set; } // Ej: "500" para MAX_LEADS

        // Propiedades de navegación
        public virtual Plan Plan { get; set; }
        public virtual Feature Feature { get; set; }
    }
}
