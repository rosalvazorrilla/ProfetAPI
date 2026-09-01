using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

/// <summary>
/// Asigna un usuario interno con rol "PM" a un cliente — controla qué clientes
/// puede ver/gestionar ese PM en el panel de Admin Global. Muchos-a-muchos:
/// un PM puede tener varios clientes, un cliente puede tener más de un PM.
/// </summary>
public class PmCustomerAssignment
{
    [Key]
    public int Id { get; set; }

    [MaxLength(128)] // dbo.Users.Id es NVARCHAR(128) en este esquema (no el default 450 de Identity)
    public string PmUserId { get; set; } = null!;
    public int CustomerId { get; set; }

    public DateTime AssignedOn { get; set; } = DateTime.UtcNow;

    public virtual ApplicationUser PmUser { get; set; } = null!;
    public virtual Customer Customer { get; set; } = null!;
}
