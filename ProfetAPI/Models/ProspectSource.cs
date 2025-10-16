using System.ComponentModel.DataAnnotations;
namespace ProfetAPI.Models;

public class ProspectSource
{
    [Key]
    public int SourceId { get; set; }
    public string Name { get; set; } = null!;
}