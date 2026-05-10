using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace ProfetAPI.Models;

[Table("ContactsWhatsapps")]
public class ContactWhatsapp
{
    [Key]
    public int Id { get; set; }

    [Required]
    [StringLength(20)]
    public string PhoneNumber { get; set; } = null!;

    [StringLength(100)]
    public string? FirstName { get; set; }

    [StringLength(100)]
    public string? LastName { get; set; }

    [StringLength(255)]
    public string? AvatarUrl { get; set; }

    public int CustomerId { get; set; }

    public DateTime? CreatedAt { get; set; }

    public long? LeadId { get; set; }

    [StringLength(128)]
    public string? UserId { get; set; }

    [StringLength(200)]
    public string? Email { get; set; }

    public bool? IsArchived { get; set; }

    // Nuevas columnas (agregar via SQL)
    public int? AccountId { get; set; }

    public int? LinkedContactId { get; set; }

    // Computed helper (no columna)
    [NotMapped]
    public string FullName => $"{FirstName} {LastName}".Trim();

    // Navegación
    public virtual Customer Customer { get; set; } = null!;
    public virtual Contact? LinkedContact { get; set; }
    public virtual ICollection<MessagesWhatsapp> Messages { get; set; } = new List<MessagesWhatsapp>();
}
