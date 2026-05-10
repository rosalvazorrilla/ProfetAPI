using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProfetAPI.Models;

public class Contact
{
    [Key]
    public int ContactId { get; set; }
    public int? CompanyId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    [EmailAddress]
    public string? Email { get; set; }

    public string? PhoneNumber { get; set; }
    public string? Position { get; set; }

    public DateTime CreatedOn { get; set; }
    public DateTime? ModifiedOn { get; set; }

    public string? LifecycleStatus { get; set; }
    public string? PostalCode { get; set; }

    // ── WhatsApp ──────────────────────────────────────────────────────
    /// <summary>true = este contacto llegó / está en el canal de WhatsApp</summary>
    public bool IsWhatsappContact { get; set; } = false;

    // Propiedades de navegación
    public virtual Company? Company { get; set; }
    public virtual ICollection<ContactReferral> ReferralsMade { get; set; } = new List<ContactReferral>();
    public virtual ICollection<ContactReferral> ReferralsReceived { get; set; } = new List<ContactReferral>();
}