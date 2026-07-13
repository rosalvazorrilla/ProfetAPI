using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Dtos.Metrics;

namespace ProfetAPI.Services.Metrics;

/// <summary>
/// Capa semántica: define QUÉ se puede medir y cómo cruzarlo. La IA solo puede combinar
/// elementos de este catálogo (whitelist); el motor ejecuta consultas parametrizadas.
/// </summary>
public class MetricsCatalog(ApplicationDbContext db)
{
    // Dimensiones soportadas por medidas de LEADS y de DEALS
    private static readonly string[] LeadDims = { "source", "tier", "status", "owner", "time" };
    private static readonly string[] DealDims = { "stage", "status", "time" };

    public static readonly List<CatalogMeasureDto> Measures = new()
    {
        new() { Key = "leads_count",     Label = "# Prospectos",           Source = "crm", Format = "number",  SupportedDimensions = LeadDims.ToList() },
        new() { Key = "leads_qualified", Label = "# Prospectos calificados", Source = "crm", Format = "number", SupportedDimensions = LeadDims.ToList() },
        new() { Key = "avg_score",       Label = "Score promedio",         Source = "crm", Format = "number",  SupportedDimensions = LeadDims.ToList() },
        new() { Key = "deals_open",      Label = "# Oportunidades abiertas", Source = "crm", Format = "number", SupportedDimensions = DealDims.ToList() },
        new() { Key = "deals_won",       Label = "# Ganadas",              Source = "crm", Format = "number",  SupportedDimensions = DealDims.ToList() },
        new() { Key = "deals_lost",      Label = "# Perdidas",             Source = "crm", Format = "number",  SupportedDimensions = DealDims.ToList() },
        new() { Key = "deals_amount",    Label = "Monto de oportunidades", Source = "crm", Format = "money",   SupportedDimensions = DealDims.ToList() },
        new() { Key = "win_rate",        Label = "Tasa de cierre",         Source = "crm", Format = "percent", SupportedDimensions = new() { "time" } },
    };

    public static readonly List<CatalogItemDto> Dimensions = new()
    {
        new() { Key = "source", Label = "Fuente" },
        new() { Key = "tier",   Label = "Nivel (tier)" },
        new() { Key = "stage",  Label = "Etapa" },
        new() { Key = "status", Label = "Estatus" },
        new() { Key = "owner",  Label = "Vendedor" },
        new() { Key = "time",   Label = "Tiempo (mes)" },
    };

    public static readonly List<CatalogItemDto> ChartTypes = new()
    {
        new() { Key = "kpi",   Label = "Número (KPI)" },
        new() { Key = "bar",   Label = "Barras" },
        new() { Key = "line",  Label = "Línea" },
        new() { Key = "donut", Label = "Dona" },
        new() { Key = "table", Label = "Tabla" },
    };

    /// <summary>Catálogo filtrado por lo que el tenant tiene disponible (gating de fuentes externas).</summary>
    public async Task<MetricsCatalogDto> GetForAccountAsync(int accountId)
    {
        var hasMeta = await db.Accounts.AsNoTracking()
            .Where(a => a.AccountId == accountId)
            .Select(a => a.MetaAdAccountId)
            .FirstOrDefaultAsync();

        return new MetricsCatalogDto
        {
            Measures   = Measures,
            Dimensions = Dimensions,
            ChartTypes = ChartTypes,
            Sources = new()
            {
                new() { Key = "crm",    Label = "Profet (CRM)", Available = true },
                new() { Key = "meta",   Label = "Meta Ads",     Available = !string.IsNullOrWhiteSpace(hasMeta),
                        Reason = string.IsNullOrWhiteSpace(hasMeta) ? "Conecta tu cuenta de Meta" : null },
                new() { Key = "google", Label = "Google Ads",   Available = false, Reason = "Próximamente" },
            },
        };
    }

    public static bool IsValid(MetricQueryDto q, out string? error)
    {
        var measure = Measures.FirstOrDefault(m => m.Key == q.Measure);
        if (measure == null) { error = $"Medida desconocida: {q.Measure}"; return false; }
        if (q.Dimension != null && q.ChartType != "kpi" &&
            !measure.SupportedDimensions.Contains(q.Dimension))
        { error = $"La medida '{q.Measure}' no admite la dimensión '{q.Dimension}'."; return false; }
        if (!ChartTypes.Any(c => c.Key == q.ChartType)) { error = $"Tipo de gráfica desconocido: {q.ChartType}"; return false; }
        error = null; return true;
    }
}
