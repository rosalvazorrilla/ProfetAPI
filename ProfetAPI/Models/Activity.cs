using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace ProfetAPI.Models;

public class Activity
{
    [Key]
    public int ActivityId { get; set; }
    public string? ActivityType { get; set; }
    public string? Subject { get; set; }
    [Column("Date")]
    public DateTime? ActivityDate { get; set; }
    public string? Notes { get; set; }
    public bool? IsCompleted { get; set; }
    public string? OwnerUserId { get; set; }
    public long? EntityId { get; set; }
    public string? EntityType { get; set; }

    public virtual ApplicationUser? OwnerUser { get; set; }
    public virtual CallDetail? CallDetail { get; set; }
}