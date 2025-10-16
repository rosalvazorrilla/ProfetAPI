using System.ComponentModel.DataAnnotations;
namespace ProfetAPI.Models;

public class Line
{
    [Key]
    public int Id { get; set; }
    public string Number { get; set; } = null!;
    public string Description { get; set; } = null!;
}