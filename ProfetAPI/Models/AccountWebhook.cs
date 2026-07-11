using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

public class AccountWebhook
{
    [Key]
    public int WebhookId { get; set; }

    public int AccountId { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>"Incoming" | "Outgoing"</summary>
    [Required, MaxLength(20)]
    public string Direction { get; set; } = "Incoming";

    // ── ENTRANTE: plataforma de origen ────────────────────────────────────────
    /// <summary>"MetaLeadAds" | "TikTokLeadGen" | "GoogleLeads" | "CustomHttp"</summary>
    [MaxLength(50)]
    public string? Platform { get; set; }

    /// <summary>"CreateLead" | "CreateContact" | "CreateCompany" | "LogOnly"</summary>
    [MaxLength(50)]
    public string? ActionType { get; set; } = "CreateLead";

    /// <summary>Token único en la URL: /api/receive/{platform}/{WebhookKey}</summary>
    [MaxLength(64)]
    public string? WebhookKey { get; set; }

    // Meta Lead Ads
    public string? MetaAppId           { get; set; }
    public string? MetaAppSecret       { get; set; }
    public string? MetaVerifyToken     { get; set; }
    public string? MetaPageAccessToken { get; set; }
    public string? MetaPageId          { get; set; }
    public string? MetaPageName        { get; set; }
    public string? MetaFormId          { get; set; }
    public string? MetaFormName        { get; set; }
    /// <summary>JSON: {"name":"full_name","email":"email","phone":"phone_number",...} — CRM field → Meta question key</summary>
    public string? FieldMappingJson    { get; set; }
    /// <summary>JSON array de reglas FormatterRule[] — transformaciones aplicadas al payload antes de crear el lead</summary>
    public string? FormatterJson       { get; set; }

    /// <summary>ID de la cuenta de anuncios de Meta Business (sin prefijo "act_"), para cruzar métricas de inversión</summary>
    [MaxLength(50)]
    public string? MetaAdAccountId     { get; set; }

    // Destino en Profet para leads/contactos entrantes
    public int?    DestFunnelId   { get; set; }
    public string? DestLeadStatus { get; set; } = "Nuevo";

    // ── SALIENTE: evento y destino ────────────────────────────────────────────
    /// <summary>"LeadCreated" | "LeadUpdated" | "LeadStatusChanged" | "ContactCreated" | "DealCreated" | "DealWon" | "DealLost" | "TaskCreated"</summary>
    [MaxLength(100)]
    public string? TriggerEvent { get; set; }

    [MaxLength(500)]
    public string? TargetUrl { get; set; }

    [MaxLength(300)]
    public string? OutgoingSecret { get; set; }  // para firmar el payload saliente (X-Profet-Signature)

    // ── Estado y métricas ─────────────────────────────────────────────────────
    public bool      IsActive        { get; set; } = true;
    public DateTime  CreatedAt       { get; set; } = DateTime.UtcNow;
    public DateTime? LastTriggeredAt { get; set; }
    public int       TriggerCount    { get; set; } = 0;
    public string?   LastError       { get; set; }

    // Navigation
    public virtual Account? Account { get; set; }
}
