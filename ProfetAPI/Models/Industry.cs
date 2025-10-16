using System.ComponentModel.DataAnnotations;
namespace ProfetAPI.Models;

public class Industry
{
    [Key]
    public long Id { get; set; }
    public string? NameES { get; set; }
    public string? NameEN { get; set; }
}