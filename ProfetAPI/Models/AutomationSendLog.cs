using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

/// <summary>
/// Auditoría de cada intento de envío automático de secuencia (éxito o falla), tanto del
/// despachador diario como del seguimiento comercial masivo — para que quede visible en la
/// cuenta en vez de solo en el log de servidor, que nadie del cliente puede ver.
/// </summary>
public class AutomationSendLog
{
    [Key]
    public int LogId { get; set; }
    public int AccountId { get; set; }
    public string EntityType { get; set; } = null!;   // "Lead" | "Deal"
    public long EntityId { get; set; }
    public string Channel { get; set; } = null!;       // "Email" | "WhatsApp"
    public int? TemplateId { get; set; }
    public string? TemplateName { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
}
