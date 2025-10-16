namespace ProfetAPI.Models;

// Esta es una tabla de unión para una relación muchos-a-muchos
public class SharedWidget
{
    public int ShareLinkId { get; set; }
    public int WidgetId { get; set; }

    // Propiedades de navegación
    public virtual ReportShareLink ShareLink { get; set; } = null!;
    public virtual Widget Widget { get; set; } = null!;
}