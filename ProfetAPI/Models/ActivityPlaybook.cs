using System.ComponentModel.DataAnnotations;
namespace ProfetAPI.Models;

public class ActivityPlaybook
{
    [Key]
    public int PlaybookId { get; set; }

    /// <summary>Legacy: cuenta donde se creó originalmente. Ya NO se usa para resolver
    /// pertenencia — eso ahora es CustomerId + PlaybookAccountAssignments (una secuencia
    /// puede compartirse entre varias cuentas del mismo cliente).</summary>
    public int AccountId { get; set; }

    /// <summary>Dueño real de la secuencia — el cliente, no una cuenta puntual.</summary>
    public int CustomerId { get; set; }

    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    /// <summary>Si está activo se puede aplicar; si no, queda archivado.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Legacy — "predeterminada" ahora vive por cuenta en
    /// PlaybookAccountAssignment.IsDefault, ya que la misma secuencia puede ser
    /// predeterminada en una cuenta del cliente y no en otra.</summary>
    public bool IsDefault { get; set; } = false;

    public bool Deleted { get; set; } = false;

    /// <summary>"Block" = no deja convertir/avanzar de etapa si hay tareas pendientes.
    /// "Warn" = deja pasar, pero el frontend advierte antes de confirmar.</summary>
    public string GatingMode { get; set; } = "Warn";

    public virtual Account Account { get; set; } = null!;
    public virtual ICollection<PlaybookTask> Tasks { get; set; } = new List<PlaybookTask>();
    public virtual ICollection<PlaybookAccountAssignment> AccountAssignments { get; set; } = new List<PlaybookAccountAssignment>();
}
