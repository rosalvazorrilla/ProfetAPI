using System.ComponentModel.DataAnnotations;
namespace ProfetAPI.Models;

public class ContactForm
{
    [Key]
    public int Id { get; set; }
    public string? DescriptionEn { get; set; }
    public string? DescriptionES { get; set; }
    public bool? Active { get; set; }
}