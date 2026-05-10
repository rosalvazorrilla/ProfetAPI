using System.ComponentModel.DataAnnotations;
namespace ProfetAPI.Models;

public class AccountProspectSource
{
    [Key]
    public int Id { get; set; }
    public int AccountId { get; set; }
    public int SourceId { get; set; }

    public virtual Account Account { get; set; } = null!;
    public virtual ProspectSource Source { get; set; } = null!;
}
