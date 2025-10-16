using System.ComponentModel.DataAnnotations;
namespace ProfetAPI.Models;

public class ActivityPlaybook
{
    [Key]
    public int PlaybookId { get; set; }
    public int AccountId { get; set; }
    public string Name { get; set; } = null!;

    public virtual Account Account { get; set; } = null!;
    public virtual ICollection<PlaybookTask> Tasks { get; set; } = new List<PlaybookTask>();
}