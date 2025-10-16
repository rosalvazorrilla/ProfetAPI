using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

public class ReportShareLink
{
    [Key]
    public int ShareLinkId { get; set; }

    // El ID público para la URL (ej: /share/a1b2c3d4-...)
    public Guid PublicId { get; set; }
    public string? Description { get; set; }
    
    [Required]
    public string OwnerUserId { get; set; } = null!;
    public DateTime CreatedOn { get; set; }
    public DateTime? ExpiresOn { get; set; }
    public bool IsActive { get; set; }

    // Propiedades de navegación
    public virtual ApplicationUser OwnerUser { get; set; } = null!;
    public virtual ICollection<SharedWidget> SharedWidgets { get; set; } = new List<SharedWidget>();
}