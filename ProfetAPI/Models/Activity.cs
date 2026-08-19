using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace ProfetAPI.Models;

public class Activity
{
    [Key]
    [Column("Id")]
    public int ActivityId { get; set; }
    public string? ActivityType { get; set; }
    public string? Subject { get; set; }
    [Column("Date")]
    public DateTime? ActivityDate { get; set; }
    public string? Notes { get; set; }
    public bool? IsCompleted { get; set; }
    public string? OwnerUserId { get; set; }
    public long? EntityId { get; set; }
    public string? EntityType { get; set; }

    // ── Campos para el módulo de Tareas ──────────────────────
    public int? AccountId { get; set; }
    public string? Priority { get; set; }           // Alta / Media / Baja
    public string? TaskStatus { get; set; }         // Pendiente / En progreso / Completada / Cancelada
    public string? AssignedToUserId { get; set; }   // usuario responsable de ejecutar la tarea
    public DateTime? DueDate { get; set; }          // fecha límite
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

    // ── Campos para el gating de secuencias (checklist Lead / etapas Deal) ────
    /// <summary>De qué paso de plantilla (PlaybookTask) nació esta tarea, si aplica.</summary>
    public int? SourcePlaybookTaskId { get; set; }
    /// <summary>Etapa del Deal a la que pertenece esta tarea (null = fase Lead o tarea suelta).</summary>
    public int? StageId { get; set; }
    /// <summary>Motivo cuando TaskStatus = "Omitida" (se resolvió distinto a como se definió, pero cuenta como cerrada).</summary>
    public string? ResolutionNote { get; set; }

    public virtual ApplicationUser? OwnerUser { get; set; }
    public virtual ApplicationUser? AssignedToUser { get; set; }
    public virtual CallDetail? CallDetail { get; set; }
}