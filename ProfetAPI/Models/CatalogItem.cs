using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProfetAPI.Models;

public class CatalogItem
{
    [Key]
    public int ItemId { get; set; }
    public int CatalogId { get; set; }
    
    [Required]
    public string Name { get; set; } = null!;

    [Column(TypeName = "decimal(18, 2)")]
    public decimal? Price { get; set; }
    public string? Code { get; set; }

    // Propiedad de navegación
    public virtual ItemCatalog Catalog { get; set; } = null!;
}