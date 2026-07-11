using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

/// <summary>
/// Un paso dentro de una AutomationRule, ejecutado en orden ascendente por StepOrder.
///
/// StepType y su ConfigJson:
///
///   "Formatter"    — mapeo de campos del payload entrante a nombres de Profet
///     { "mappings": [{ "from": "full_name", "to": "name" }, { "from": "phone_number", "to": "phone" }] }
///
///   "InsertLead"   — crea un Lead en Profet con los campos del contexto
///     { "insertAs": "Lead", "defaultStageId": 1, "defaultOwnerId": null }
///
///   "HttpPost"     — POST a URL externa con campos seleccionados
///     { "url": "https://...", "headers": {"X-Key":"val"}, "fields": ["name","email","phone"] }
///
///   "Email"        — envía correo usando el SMTP configurado en la cuenta
///     { "toType": "field|fixed", "to": "{{email}}", "subject": "Nuevo lead: {{name}}", "body": "..." }
///
///   "Notification" — crea una notificación interna en Profet
///     { "message": "Nuevo lead: {{name}} desde {{prospectSource}}", "recipientType": "owner|all" }
/// </summary>
public class AutomationStep
{
    [Key]
    public int StepId { get; set; }

    public int RuleId { get; set; }

    public int StepOrder { get; set; }

    [Required, MaxLength(30)]
    public string StepType { get; set; } = "";

    public string? ConfigJson { get; set; }

    public bool IsActive { get; set; } = true;

    // ── Navigation ────────────────────────────────────────────────────────────
    public virtual AutomationRule? Rule { get; set; }
}
