using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

public class AccountIndustry
{
    [Key]
    public long Id { get; set; }
    public int AccountId { get; set; }
    public long IndustryId { get; set; }

    public virtual Account Account { get; set; } = null!;
    public virtual Industry Industry { get; set; } = null!;
}
