using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace ProfetAPI.Models;

public class PlaybookTask
{
    [Key]
    public int TaskId { get; set; }
    public int PlaybookId { get; set; }
    public string TaskName { get; set; } = null!;
    [Column("\"Order\"")]
    public int Order { get; set; }

    public virtual ActivityPlaybook Playbook { get; set; } = null!;
}