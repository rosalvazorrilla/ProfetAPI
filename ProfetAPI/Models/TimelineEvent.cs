using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

/// <summary>
/// Evento del hilo cronológico de un lead/deal (mensajes, cambios de etapa/score, notas, llamadas…).
/// Separado de <see cref="Activity"/> (que representa TAREAS), para no mezclar ambos conceptos.
/// </summary>
public class TimelineEvent
{
    [Key]
    public long TimelineEventId { get; set; }

    public int AccountId { get; set; }

    /// <summary>'Lead' | 'Deal'.</summary>
    [Required, MaxLength(20)]
    public string EntityType { get; set; } = "Lead";

    public long EntityId { get; set; }

    /// <summary>
    /// 'lead_created' | 'message_in' | 'message_out' | 'stage_change' | 'score_change'
    /// | 'task' | 'call' | 'note' | 'email'.
    /// </summary>
    [Required, MaxLength(30)]
    public string Type { get; set; } = "note";

    [Required, MaxLength(200)]
    public string Title { get; set; } = "";

    public string? Detail { get; set; }

    /// <summary>JSON libre con datos extra del evento (opcional).</summary>
    public string? MetaJson { get; set; }

    public string? CreatedByUserId { get; set; }

    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

    public bool Deleted { get; set; } = false;
}
