using System.ComponentModel.DataAnnotations;
namespace ProfetAPI.Models;

public class Notification
{
    [Key]
    public int Id { get; set; }
    public string? UserId { get; set; }
    public int? NotificationType { get; set; }
    public string? Message { get; set; }
    public string? URL { get; set; }
    public bool? Status { get; set; }
    public DateTime? Date { get; set; }
    
    // Columnas polimórficas añadidas
    public long? EntityId { get; set; }
    public string? EntityType { get; set; }
    
    public virtual NotificationType? Type { get; set; }
}