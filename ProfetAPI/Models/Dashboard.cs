using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

public class Dashboard
{
    [Key]
    public int DashboardId { get; set; }

    [Required]
    [StringLength(255)]
    public string Name { get; set; } = null!;

    [Required]
    public string OwnerUserId { get; set; } = null!;

    public DateTime CreatedOn { get; set; }
    public DateTime ModifiedOn { get; set; }

    // Propiedades de navegación
    public virtual ApplicationUser OwnerUser { get; set; } = null!;
    public virtual ICollection<Widget> Widgets { get; set; } = new List<Widget>();
}