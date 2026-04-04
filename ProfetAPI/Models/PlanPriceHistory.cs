using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models
{
    [Table("PlanPriceHistory")]
    public class PlanPriceHistory
    {
        [Key]
        public int PriceHistoryId { get; set; }
        public int PlanId { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal MonthlyPrice { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal AnnualPrice { get; set; }

        public DateTime EffectiveDate { get; set; }
        public DateTime? EndDate { get; set; }

        // Propiedad de navegación
        public virtual Plan Plan { get; set; } = null!;
    }
}
