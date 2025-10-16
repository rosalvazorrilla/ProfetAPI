using System.ComponentModel.DataAnnotations;
namespace ProfetAPI.Models;

public class LeadTier
{
    [Key]
    public int TierId { get; set; }
    public string Name { get; set; } = null!; // Ej: "Estándar", "Premier"
}