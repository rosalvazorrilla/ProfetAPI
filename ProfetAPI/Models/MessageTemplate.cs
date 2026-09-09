using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

/// <summary>
/// Plantilla de mensaje reutilizable por cuenta, usada tanto por el envío automático
/// de una secuencia (PlaybookTask.TemplateId) como por el seguimiento comercial masivo
/// manual. El cuerpo admite variables tipo {{nombre}} que se interpolan al enviar.
/// </summary>
public class MessageTemplate
{
    [Key]
    public int TemplateId { get; set; }

    /// <summary>Legacy: cuenta donde se creó originalmente. Ya no se usa para resolver
    /// pertenencia — ahora es por cliente (CustomerId), para que sirva en cualquiera de
    /// las cuentas asignadas a una secuencia compartida.</summary>
    public int AccountId { get; set; }

    public int CustomerId { get; set; }

    [Required, StringLength(150)]
    public string Name { get; set; } = null!;

    /// <summary>"Email" | "WhatsApp".</summary>
    [Required, StringLength(20)]
    public string Channel { get; set; } = "Email";

    /// <summary>Solo aplica cuando Channel = "Email".</summary>
    [StringLength(300)]
    public string? Subject { get; set; }

    [Required]
    public string Body { get; set; } = null!;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

    public virtual Account? Account { get; set; }
}
