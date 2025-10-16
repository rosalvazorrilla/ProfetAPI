using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

public class Tag
{
    [Key]
    public int TagId { get; set; }
    public int? CustomerId { get; set; } // Para tags personalizadas por cliente
    
    [Required]
    public string Name { get; set; } = null!;
    public string? Color { get; set; }
    public string? FontColor { get; set; }

    // Propiedad de navegación
    public virtual Customer? Customer { get; set; }
}