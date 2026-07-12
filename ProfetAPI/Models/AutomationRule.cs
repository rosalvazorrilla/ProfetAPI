using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

/// <summary>
/// Regla de automatización — un trigger + N pasos configurables en secuencia.
/// TriggerType: "WebhookIncoming" | "LeadCreated" | "LeadUpdated" | "DealWon" | "DealLost" | "StageChanged"
/// TriggerPlatform (solo para WebhookIncoming): "Meta" | "Google" | "TypeForm" | "Generic"
/// </summary>
public class AutomationRule
{
    [Key]
    public int RuleId { get; set; }

    public int AccountId { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = "";

    public bool IsActive { get; set; } = true;
    public bool Deleted  { get; set; } = false;

    // ── Trigger ──────────────────────────────────────────────────────────────
    [Required, MaxLength(50)]
    public string TriggerType { get; set; } = "WebhookIncoming";

    [MaxLength(50)]
    public string? TriggerPlatform { get; set; }

    /// <summary>Clave única que forma la URL pública /api/receive/auto/{WebhookKey}</summary>
    [MaxLength(60)]
    public string? WebhookKey { get; set; }

    /// <summary>
    /// Token de verificación para plataformas que hacen handshake GET (ej. Meta Lead Ads:
    /// GET ?hub.mode=subscribe&amp;hub.verify_token=...&amp;hub.challenge=...). Se pega en la config del webhook de la plataforma.
    /// </summary>
    [MaxLength(80)]
    public string? VerifyToken { get; set; }

    /// <summary>
    /// Page Access Token de Meta. Necesario para que, al recibir un leadgen_id,
    /// el receptor consulte la Graph API y traiga los campos reales del lead (name, email, phone…).
    /// </summary>
    [MaxLength(1024)]
    public string? MetaPageToken { get; set; }

    /// <summary>
    /// JSON: [{ "field": "prospectSource", "op": "eq", "value": "Meta Lead Ads" }, ...]
    /// Ops soportados: eq | neq | contains | gt | lt
    /// </summary>
    public string? ConditionsJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ── Navigation ────────────────────────────────────────────────────────────
    public virtual Account? Account { get; set; }
    public virtual ICollection<AutomationStep> Steps { get; set; } = new List<AutomationStep>();
    public virtual ICollection<AutomationLog>  Logs  { get; set; } = new List<AutomationLog>();
}
