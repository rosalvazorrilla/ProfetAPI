using System.ComponentModel.DataAnnotations;
namespace ProfetAPI.Models;

public class ContactReferral
{
    [Key]
    public int ReferralId { get; set; }
    public int ReferrerContactId { get; set; }
    public int ReferredContactId { get; set; }
    public string? Description { get; set; }
    public DateTime? ReferralDate { get; set; }

    public virtual Contact ReferrerContact { get; set; } = null!;
    public virtual Contact ReferredContact { get; set; } = null!;
}