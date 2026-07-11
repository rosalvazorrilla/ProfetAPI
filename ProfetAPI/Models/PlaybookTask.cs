using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace ProfetAPI.Models;

public class PlaybookTask
{
    [Key]
    public int TaskId { get; set; }
    public int PlaybookId { get; set; }
    public string TaskName { get; set; } = null!;

    /// <summary>Tipo de acción del paso: Task / Call / WhatsApp / Email / Meeting / AdvanceStage.</summary>
    public string ActionType { get; set; } = "Task";

    /// <summary>Etapa destino cuando ActionType = "AdvanceStage" (recorrido lead → oportunidad).</summary>
    public int? TargetStageId { get; set; }

    /// <summary>Instrucciones para el vendedor (ej. "Llamar y confirmar presupuesto").</summary>
    public string? Description { get; set; }

    [Column("\"Order\"")]
    public int Order { get; set; }

    /// <summary>Prioridad heredada por la tarea generada: Alta / Media / Baja.</summary>
    public string Priority { get; set; } = "Media";

    /// <summary>Días de distancia para la fecha límite desde que se aplica el playbook (0 = mismo día).</summary>
    public int OffsetDays { get; set; } = 0;

    public virtual ActivityPlaybook Playbook { get; set; } = null!;
}
