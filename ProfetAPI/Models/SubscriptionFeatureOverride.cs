using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

/// <summary>
/// Límite de feature negociado para un cliente específico.
/// Tiene prioridad sobre el límite base de PlanFeature.
/// Ejemplo: el plan tiene MAX_LEADS=500, pero este cliente tiene 1000.
/// </summary>
public class SubscriptionFeatureOverride
{
    public int SubscriptionId { get; set; }
    public int FeatureId { get; set; }

    [Required]
    [MaxLength(50)]
    public string CustomLimit { get; set; } = null!;

    public virtual Subscription Subscription { get; set; } = null!;
    public virtual Feature Feature { get; set; } = null!;
}
