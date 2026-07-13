namespace ProfetAPI.Dtos.Metrics;

/// <summary>Consulta de métrica: qué medir, contra qué agrupar, con qué filtros y en qué gráfica.</summary>
public class MetricQueryDto
{
    public string  Source     { get; set; } = "crm";           // crm | meta | google
    public string  Measure    { get; set; } = "leads_count";   // ver MetricsCatalog
    public string? Dimension  { get; set; }                    // null = KPI (un número)
    public string  ChartType  { get; set; } = "bar";           // kpi | bar | line | donut | table
    public DateTime? From     { get; set; }
    public DateTime? To       { get; set; }

    // Filtros opcionales
    public int?    FilterTierId { get; set; }
    public string? FilterSource { get; set; }
    public string? FilterOwner  { get; set; }
}

public class MetricSeriesDto
{
    public string        ChartType { get; set; } = "bar";
    public string        Measure   { get; set; } = "";
    public string?       Dimension { get; set; }
    public List<string>  Labels    { get; set; } = new();
    public List<decimal> Values    { get; set; } = new();
    public decimal       Total     { get; set; }
}

// ── Catálogo ──────────────────────────────────────────────────────────────────

public class MetricsCatalogDto
{
    public List<CatalogMeasureDto>   Measures   { get; set; } = new();
    public List<CatalogItemDto>      Dimensions { get; set; } = new();
    public List<CatalogItemDto>      ChartTypes { get; set; } = new();
    public List<CatalogSourceDto>    Sources    { get; set; } = new();
}

public class CatalogItemDto
{
    public string Key   { get; set; } = "";
    public string Label { get; set; } = "";
}

public class CatalogMeasureDto : CatalogItemDto
{
    public string       Source              { get; set; } = "crm";
    public List<string> SupportedDimensions { get; set; } = new();
    public string       Format              { get; set; } = "number"; // number | money | percent
}

public class CatalogSourceDto : CatalogItemDto
{
    public bool    Available { get; set; } = true;
    public string? Reason    { get; set; }
}
