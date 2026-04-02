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
    public string Name { get; set; } = null!;

    [Column("phone")]
    public string? Phone { get; set; }

    [Column("initialDate")]
    public DateTime InitialDate { get; set; }

    // --- CAMPOS AGREGADOS PARA EL ONBOARDING Y ADMIN ---

    [Column("contact")]
    public string? Contact { get; set; } // Nombre de la persona a la que va dirigido el correo

    [Column("Email")]
    public string? Email { get; set; } // Vital para enviar el SetupLink

    [Column("SetupToken")]
    public string? SetupToken { get; set; } // El token de la URL mágica

    [Column("SetupStep")]
    public int SetupStep { get; set; } // Para saber en qué paso del Wizard se quedó

    [Column("Status")]
    public string Status { get; set; } = "Pendiente de Setup"; // El estatus visual para tu tabla

    [Column("Active")]
    public bool? Active { get; set; }

    [Column("Deleted")]
    public bool? Deleted { get; set; }

    // --- Propiedades de navegación ---
    // Un cliente tiene muchos usuarios y muchos equipos.
    public virtual ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
    public virtual ICollection<Team> Teams { get; set; } = new List<Team>();
}