using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProfetAPI.Models;

/// <summary>
/// Una corrida de "AI QUANT — Lead Score": la investigación de IA (con búsqueda web
/// pública) sobre la EMPRESA de un prospecto. Se guarda una fila por corrida — al
/// re-ejecutar se crea otra; la más reciente en estado "Done" es la que se muestra.
/// Incluye el ledger de costo (tokens + búsquedas + USD) para el tope mensual por cliente.
/// </summary>
public class AiQuantRun
{
    [Key]
    public int RunId { get; set; }

    public long LeadId { get; set; }
    public int AccountId { get; set; }
    /// <summary>Denormalizado para sumar el gasto del mes sin joins.</summary>
    public int CustomerId { get; set; }
    public string? RunByUserId { get; set; }

    /// <summary>Pending | Running | Done | Failed</summary>
    public string Status { get; set; } = "Pending";

    /// <summary>Sitio web que el vendedor confirmó/aportó al lanzar la corrida.</summary>
    public string? InputWebsite { get; set; }
    /// <summary>Contexto libre que el vendedor escribe tras el First Contact Call.</summary>
    public string? InputCallNotes { get; set; }

    /// <summary>La ficha estructurada completa (JSON) devuelta por la IA.</summary>
    public string? ResultJson { get; set; }
    public int? Score { get; set; }           // 0-100 — lo calcula la IA (juicio cualitativo)
    public string? Tier { get; set; }         // Frío | Tibio | Caliente | Prioritario

    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? WebSearchCount { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal? CostUsd { get; set; }

    public string? Error { get; set; }
    public int? DurationMs { get; set; }

    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedOn { get; set; }
}
