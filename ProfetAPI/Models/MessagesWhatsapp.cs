using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace ProfetAPI.Models;

[Table("MessagesWhatsapps")]
public class MessagesWhatsapp
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    /// <summary>FK → Contacts.ContactId (contacto de WhatsApp)</summary>
    public int ContactId { get; set; }

    /// <summary>UUID único del mensaje que proviene de 2chat (evita duplicados en webhooks)</summary>
    [Column("MessageId")]
    [StringLength(200)]
    public string? MessageId { get; set; }

    /// <summary>Texto plano del mensaje (null si es solo media)</summary>
    [Column("MessageText")]
    public string? MessageText { get; set; }

    /// <summary>URL del archivo multimedia (imagen, PDF, audio, etc.)</summary>
    [Column("MediaUrl")]
    public string? MediaUrl { get; set; }

    /// <summary>Tipo de media: image, document, audio, video</summary>
    [Column("MediaType")]
    [StringLength(50)]
    public string? MediaType { get; set; }

    /// <summary>MIME type del archivo adjunto</summary>
    [Column("MimeType")]
    [StringLength(100)]
    public string? MimeType { get; set; }

    /// <summary>"incoming" = recibido del cliente, "outgoing" = enviado por el agente</summary>
    [Column("Direction")]
    [StringLength(20)]
    public string Direction { get; set; } = "incoming";

    /// <summary>Session key de 2chat (WW-WPN{channel}-{phone}@c.us)</summary>
    [Column("SessionKey")]
    [StringLength(200)]
    public string? SessionKey { get; set; }

    /// <summary>true si el agente ya leyó el mensaje entrante</summary>
    [Column("IsRead")]
    public bool IsRead { get; set; } = false;

    [Column("CreatedAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navegación
    public virtual ContactWhatsapp Contact { get; set; } = null!;
}
