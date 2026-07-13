using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProfetAPI.Models;

/// <summary>
/// Tabla Leads — esquema heredado (wide flat table) más columnas nuevas de migración.
/// AccountId y OwnerUserId son nullable en la BD; se tratan como requeridos a nivel
/// de negocio pero deben ser nullable en el modelo para evitar errores de materialización.
/// </summary>
public class Lead
{
    [Key]
    public long LeadId { get; set; }

    // === COLUMNAS NUEVAS (migración) ===
    public int? AccountId { get; set; }
    public string? OwnerUserId { get; set; }
    public int? ContactId { get; set; }
    public int? ContactFormId { get; set; }
    public int? StageId { get; set; }
    public int? LeadLostReasonsId { get; set; }
    public int? ProspectSourceId { get; set; }

    [Column("OriginType")]
    public string OriginType { get; set; } = "Inbound";

    public string? LifecycleStatus { get; set; }

    // === COLUMNAS LEGACY (flat schema original) ===
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? InitialMessage { get; set; }
    public string? ProspectSource { get; set; }
    public string? AdName { get; set; }
    public string? Company { get; set; }
    public string? Position { get; set; }
    public string? City { get; set; }
    public long CampaignId { get; set; }
    public string? CampaignName { get; set; }
    public bool? Deleted { get; set; }
    public bool? Active { get; set; }
    public bool? StateLead { get; set; }

    [Required]
    public string Status { get; set; } = "Nuevo";

    public DateTime CreatedOn { get; set; }

    // === SCORING (Fase 1) ===
    /// <summary>Puntaje total persistido (respuestas + reglas automáticas).</summary>
    public decimal? Score { get; set; }
    /// <summary>Banda de calificación resuelta (Frío/Tibio/Caliente…).</summary>
    public int? TierId { get; set; }
    /// <summary>Explicación del score (sobre todo cuando lo calcula la IA).</summary>
    public string? ScoreReasoning { get; set; }
    public DateTime? ScoredAt { get; set; }
    /// <summary>'Manual' | 'AI' | 'Hybrid'.</summary>
    public string? ScoreSource { get; set; }

    // === NAVIGATION PROPERTIES ===
    public virtual Account? Account { get; set; }
    public virtual ApplicationUser? Owner { get; set; }
    public virtual Contact? Contact { get; set; }
    public virtual LeadTier? Tier { get; set; }
}
