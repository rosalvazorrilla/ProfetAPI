namespace ProfetAPI.Models;

/// <summary>Junction: qué motivos de pérdida habilitó cada Account.</summary>
public class AccountLeadLostReason
{
    public int AccountId { get; set; }
    public int LostReasonId { get; set; }

    public virtual Account Account { get; set; } = null!;
    public virtual LeadLostReason LeadLostReason { get; set; } = null!;
}
