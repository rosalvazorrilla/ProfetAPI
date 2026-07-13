using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

/// <summary>
/// Reporte guardado: un conjunto de gráficas (MetricQuery serializadas en LayoutJson)
/// que un usuario arma en el constructor y puede reabrir. Persistido en BD, por cuenta/usuario.
/// </summary>
public class SavedReport
{
    [Key]
    public int Id { get; set; }
    public int AccountId { get; set; }
    public string? UserId { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = "";

    /// <summary>JSON: [{ measure, dimension, chartType, title, ... }]</summary>
    public string LayoutJson { get; set; } = "[]";

    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    public bool Deleted { get; set; } = false;
}
