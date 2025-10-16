using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

public class AccountNotificationRecipient
{
    [Key]
    public int RecipientId { get; set; }
    public int AccountId { get; set; }

    [Required]
    [EmailAddress]
    public string Email { get; set; } = null!;
    public bool IsActive { get; set; } = true;

    // Propiedad de navegación
    public virtual Account Account { get; set; } = null!;
}