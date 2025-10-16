using System.ComponentModel.DataAnnotations;
namespace ProfetAPI.Models;

public class UserLine
{
    [Key]
    public int Id { get; set; }
    public string? UserId { get; set; }
    public int? LineId { get; set; }

    public virtual ApplicationUser? User { get; set; }
    public virtual Line? Line { get; set; }
}