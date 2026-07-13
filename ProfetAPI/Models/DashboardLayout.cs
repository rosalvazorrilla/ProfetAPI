using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

/// <summary>
/// Layout del dashboard por usuario y cuenta (los widgets que armó). Reemplaza la persistencia
/// en localStorage para que el dashboard se vea igual en cualquier dispositivo.
/// </summary>
public class DashboardLayout
{
    [Key]
    public int Id { get; set; }
    public int AccountId { get; set; }
    public string? UserId { get; set; }

    /// <summary>JSON con la lista de widgets (WidgetDef[]).</summary>
    public string LayoutJson { get; set; } = "[]";

    public DateTime ModifiedOn { get; set; } = DateTime.UtcNow;
}
