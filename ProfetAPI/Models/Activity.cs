using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace ProfetAPI.Models;

public class Activity
{
    [Key]
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

    public virtual ApplicationUser? OwnerUser { get; set; }
    public virtual ApplicationUser? AssignedToUser { get; set; }
    public virtual CallDetail? CallDetail { get; set; }
}