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

    // Meta: leads_count por campaña/tiempo sale del CRM (ProspectSource); spend/clicks
    // vienen en vivo del Graph API (MetaAdsService), por eso solo admiten "campaign"
    // (esa llamada no trae desglose diario todavía).
    private static readonly string[] MetaLeadDims  = { "campaign", "time" };
    private static readonly string[] MetaSpendDims = { "campaign" };
    private static readonly string[] GoogleLeadDims = { "time" };
    // Google Ads sí trae desglose por campaña Y por día (GoogleAdsService.GetCampaignDailyInsightsAsync).
    private static readonly string[] GoogleAdsDims = { "campaign", "time" };

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
        new() { Key = "meta_leads",      Label = "# Leads de Meta Ads",    Source = "meta", Format = "number", SupportedDimensions = MetaLeadDims.ToList() },
        new() { Key = "meta_spend",      Label = "Inversión Meta Ads",     Source = "meta", Format = "money",  SupportedDimensions = MetaSpendDims.ToList() },
        new() { Key = "meta_clicks",     Label = "Clics Meta Ads",         Source = "meta", Format = "number", SupportedDimensions = MetaSpendDims.ToList() },
        new() { Key = "google_leads",    Label = "# Leads de Google Ads",  Source = "google", Format = "number", SupportedDimensions = GoogleLeadDims.ToList() },
        new() { Key = "google_cost",         Label = "Inversión Google Ads",    Source = "google", Format = "money",  SupportedDimensions = GoogleAdsDims.ToList() },
        new() { Key = "google_clicks",       Label = "Clics Google Ads",        Source = "google", Format = "number", SupportedDimensions = GoogleAdsDims.ToList() },
        new() { Key = "google_conversions",  Label = "Conversiones Google Ads", Source = "google", Format = "number", SupportedDimensions = GoogleAdsDims.ToList() },
    };

    public static readonly List<CatalogItemDto> Dimensions = new()
    {
        new() { Key = "source",   Label = "Fuente" },
        new() { Key = "tier",     Label = "Nivel (tier)" },
        new() { Key = "stage",    Label = "Etapa" },
        new() { Key = "status",   Label = "Estatus" },
        new() { Key = "owner",    Label = "Vendedor" },
        new() { Key = "time",     Label = "Tiempo (mes)" },
        new() { Key = "campaign", Label = "Campaña" },
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
        var account = await db.Accounts.AsNoTracking()
            .Where(a => a.AccountId == accountId)
            .Select(a => new { a.MetaAdAccountId, a.GoogleAdsCustomerId, a.GoogleAdsRefreshTokenEncrypted })
            .FirstOrDefaultAsync();

        var hasMeta   = !string.IsNullOrWhiteSpace(account?.MetaAdAccountId);
        var hasGoogle = !string.IsNullOrWhiteSpace(account?.GoogleAdsCustomerId) && account?.GoogleAdsRefreshTokenEncrypted != null;

        // meta_leads/google_leads cuentan de los Leads ya en el CRM aunque la cuenta
        // publicitaria no esté conectada (el lead ya llegó etiquetado con esa fuente) —
        // solo las medidas que llaman a la API en vivo exigen la conexión real.
        var liveMetaKeys   = new[] { "meta_spend", "meta_clicks" };
        var liveGoogleKeys = new[] { "google_cost", "google_clicks", "google_conversions" };
        var measures = Measures
            .Where(m => !liveMetaKeys.Contains(m.Key) || hasMeta)
            .Where(m => !liveGoogleKeys.Contains(m.Key) || hasGoogle)
            .ToList();

        return new MetricsCatalogDto
        {
            Measures   = measures,
            Dimensions = Dimensions,
            ChartTypes = ChartTypes,
            Sources = new()
            {
                new() { Key = "crm",    Label = "Profet (CRM)", Available = true },
                new() { Key = "meta",   Label = "Meta Ads",     Available = hasMeta,
                        Reason = hasMeta ? null : "Conecta tu cuenta de Meta para ver inversión y clics (los leads ya cuentan)" },
                new() { Key = "google", Label = "Google Ads",   Available = hasGoogle,
                        Reason = hasGoogle ? null : "Conecta tu cuenta de Google Ads" },
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
