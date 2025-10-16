using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace ProfetAPI.Models;

[Table("MessagesWhatsapp")]
public class MessagesWhatsapp
{
    [Key]
    [Column("id")]
    public int Id { get; set; }
    public int ContactId { get; set; }
    public string? Message { get; set; }
    public DateTime Date { get; set; }
    public string? UserId { get; set; }
    public string? MessageSid { get; set; }
    public string? Status { get; set; }
    
    public virtual Contact Contact { get; set; } = null!;
}