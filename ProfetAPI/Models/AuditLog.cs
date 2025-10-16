using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

public class AuditLog
{
    [Key]
    public int LogId { get; set; }
    public long EntityId { get; set; }
    public string EntityType { get; set; } = null!;
    public string AuthorUserId { get; set; } = null!;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    [Required]
    public string EventType { get; set; } = null!; // 'FieldUpdate', 'StageChange', 'NoteAdded'
    
    [Required]
    public string Description { get; set; } = null!;
    
    // Guardará un JSON con detalles como OldValue y NewValue
    public string? ChangeData { get; set; } 

    // Propiedad de navegación
    public virtual ApplicationUser AuthorUser { get; set; } = null!;
}