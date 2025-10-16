namespace ProfetAPI.Models;

public class DealUser
{
    public int DealId { get; set; }
    public string UserId { get; set; } = null!;
    public string RoleInDeal { get; set; } = null!;

    public virtual Deal Deal { get; set; } = null!;
    public virtual ApplicationUser User { get; set; } = null!;
}