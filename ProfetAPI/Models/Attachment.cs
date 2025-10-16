using System.ComponentModel.DataAnnotations;
namespace ProfetAPI.Models;

public class Attachment
{
    [Key]
    public int AttachmentId { get; set; }
    public string? FileName { get; set; }
    public string? FilePath { get; set; }
    public string? UploaderUserId { get; set; }
    public DateTime? CreatedOn { get; set; }
    public long? EntityId { get; set; }
    public string? EntityType { get; set; }

    public virtual ApplicationUser? UploaderUser { get; set; }
}