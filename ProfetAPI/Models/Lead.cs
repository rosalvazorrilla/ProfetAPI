using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

public class Lead
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
    public long? OriginatingLeadId { get; set; }

    public DateTime CreatedOn { get; set; }
    public DateTime ModifiedOn { get; set; }

    // Propiedades de navegación
    public virtual Company? Company { get; set; }
    public virtual ICollection<ContactReferral> ReferralsMade { get; set; } = new List<ContactReferral>();
    public virtual ICollection<ContactReferral> ReferralsReceived { get; set; } = new List<ContactReferral>();
}