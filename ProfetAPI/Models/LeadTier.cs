using System.ComponentModel.DataAnnotations;
namespace ProfetAPI.Models;

/// <summary>
/// Umbral de clasificación del lead dentro de un ScoringModel.
/// Ej: Frío (0-40pts), Tibio (41-70pts), Caliente (71+pts).
/// </summary>
public class LeadTier
{
    [Key]
    public int TierId { get; set; }

    public int? ScoringModelId { get; set; }

    [Required]
    public string Name { get; set; } = null!;

    public decimal MinScore { get; set; } = 0;

    /// <summary>NULL = sin límite superior (el tier más alto).</summary>
    public decimal? MaxScore { get; set; }

    /// <summary>Color hex para el UI. Ej: #FF0000</summary>
    [MaxLength(50)]
    public string? Color { get; set; }

    public virtual ScoringModel? ScoringModel { get; set; }
}
