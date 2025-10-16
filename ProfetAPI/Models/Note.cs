using System.ComponentModel.DataAnnotations;
namespace ProfetAPI.Models;

public class Note
{
    [Key]
    public int NoteId { get; set; }
    public string? Content { get; set; }
    public string? AuthorUserId { get; set; }
    public DateTime? CreatedOn { get; set; }
    public long? EntityId { get; set; }
    public string? EntityType { get; set; }

    public virtual ApplicationUser? AuthorUser { get; set; }
}