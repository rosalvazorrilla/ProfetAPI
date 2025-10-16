using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

public class Widget
{
    [Key]
    public int WidgetId { get; set; }
    public int DashboardId { get; set; }

    [Required]
    [StringLength(255)]
    public string Name { get; set; } = null!;

    // La "receta" del reporte
    public int MetricFieldId { get; set; }
    public int DimensionFieldId { get; set; }
    public int ChartTypeId { get; set; }
    
    // Guardará los filtros en formato JSON
    public string? Filters { get; set; }

    // Posición y tamaño
    public int PositionX { get; set; }
    public int PositionY { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    // Propiedades de navegación
    public virtual Dashboard Dashboard { get; set; } = null!;
    public virtual ReportableField MetricField { get; set; } = null!;
    public virtual ReportableField DimensionField { get; set; } = null!;
    public virtual ChartType ChartType { get; set; } = null!;
    public virtual ICollection<SharedWidget> SharedInLinks { get; set; } = new List<SharedWidget>();
}