using System.ComponentModel.DataAnnotations;
namespace ProfetAPI.Models;

public class CallDetail
{
    [Key]
    public int CallDetailId { get; set; }
    public int ActivityId { get; set; }
    public string? RecordingUrl { get; set; }
    public string? Duration { get; set; }
    public string? CallSid { get; set; }

    public virtual Activity Activity { get; set; } = null!;
}