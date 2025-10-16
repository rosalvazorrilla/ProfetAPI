using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

public class ItemCatalog
{
    [Key]
    public int CatalogId { get; set; }
    public int AccountId { get; set; }
    
    [Required]
    public string Name { get; set; } = null!;

    // Propiedades de navegación
    public virtual Account Account { get; set; } = null!;
    public virtual ICollection<CatalogItem> Items { get; set; } = new List<CatalogItem>();
}