using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
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

    // Sin [ForeignKey] esto le hacía creer a EF Core que necesitaba una columna
    // sombra nueva "TypeId" (no existe en la tabla real) en vez de reusar la
    // columna "NotificationType" ya existente — cualquier INSERT/UPDATE de
    // Notification tronaba con "Invalid column name 'TypeId'".
    [ForeignKey(nameof(NotificationType))]
    public virtual NotificationType? Type { get; set; }
}