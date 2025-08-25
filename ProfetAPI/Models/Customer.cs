using ProfetAPI.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProfetAPI.Models;

public class Customer
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [Column("name")]
    public string Name { get; set; }

    [Column("phone")]
    public string? Phone { get; set; }

    [Column("initialDate")]
    public DateTime InitialDate { get; set; }

    // Propiedades de navegación: Un cliente tiene muchos usuarios y muchos equipos.
    public virtual ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
    public virtual ICollection<Team> Teams { get; set; } = new List<Team>();
}