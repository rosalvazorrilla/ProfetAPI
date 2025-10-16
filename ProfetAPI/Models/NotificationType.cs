using System.ComponentModel.DataAnnotations;
namespace ProfetAPI.Models;

public class NotificationType
{
    [Key]
    public int Id { get; set; }
    public string? Code { get; set; }
    public string? Title { get; set; }
    public string? URL { get; set; }
    public string? Icon { get; set; }
}