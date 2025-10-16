using System.ComponentModel.DataAnnotations.Schema;

namespace ProfetAPI.Models;

public class DealItem
{
    public int DealId { get; set; }
    public int ItemId { get; set; }
    public int Quantity { get; set; }

    [Column(TypeName = "decimal(18, 2)")]
    public decimal Price { get; set; }

    // Propiedades de navegación
    public virtual Deal Deal { get; set; } = null!;
    public virtual CatalogItem Item { get; set; } = null!;
}