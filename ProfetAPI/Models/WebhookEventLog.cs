using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

public class WebhookEventLog
{
    [Key]
    public long EventLogId { get; set; }

    public int WebhookId { get; set; }

    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    /// <summary>"Success" | "Error"</summary>
    [MaxLength(20)]
    public string Status { get; set; } = "Success";

    /// <summary>Nombre del lead/contacto creado, o resumen del evento</summary>
    [MaxLength(300)]
    public string? Summary { get; set; }

    /// <summary>ID externo (leadgen_id de Meta, etc.)</summary>
    [MaxLength(100)]
    public string? ExternalId { get; set; }

    [MaxLength(500)]
    public string? ErrorMessage { get; set; }

    // Navigation
    public virtual AccountWebhook? Webhook { get; set; }
}
