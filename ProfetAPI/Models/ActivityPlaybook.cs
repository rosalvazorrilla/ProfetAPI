using System.ComponentModel.DataAnnotations;
namespace ProfetAPI.Models;

public class ActivityPlaybook
{
    [Key]
    public int PlaybookId { get; set; }
    public int AccountId { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    /// <summary>Si está activo se puede aplicar; si no, queda archivado.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>El playbook predeterminado de la cuenta se aplica automáticamente al crear un lead. Solo uno por cuenta.</summary>
    public bool IsDefault { get; set; } = false;

    public bool Deleted { get; set; } = false;

    public virtual Account Account { get; set; } = null!;
    public virtual ICollection<PlaybookTask> Tasks { get; set; } = new List<PlaybookTask>();
}
