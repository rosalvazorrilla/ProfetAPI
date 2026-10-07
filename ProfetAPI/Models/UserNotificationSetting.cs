using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProfetAPI.Models;

/// <summary>Preferencias de alertas de un usuario: por tipo de alerta, dentro del sistema y/o por correo.
/// Sin fila = valores por defecto (alertas del sistema activas, correo apagado).</summary>
[Table("UserNotificationSettings")]
public class UserNotificationSetting
{
    [Key, StringLength(450)]
    public string UserId { get; set; } = null!;

    public bool DealWonSystem { get; set; } = true;
    public bool DealWonEmail  { get; set; } = false;
    public bool TaskDueSystem { get; set; } = true;
    public bool TaskDueEmail  { get; set; } = false;

    public DateTime UpdatedOn { get; set; } = DateTime.UtcNow;
}
