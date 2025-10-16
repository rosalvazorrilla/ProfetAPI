using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

public class Company
{
    [Key]
    public int CompanyId { get; set; }

    [Required]
    public string Name { get; set; } = null!;

    public string? Website { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }

    [Required]
    public string LifecycleStatus { get; set; } = "Prospecto";

    public DateTime CreatedOn { get; set; }
    public DateTime ModifiedOn { get; set; }

    // Propiedades de navegación
    public virtual ICollection<Contact> Contacts { get; set; } = new List<Contact>();
    public virtual ICollection<Deal> Deals { get; set; } = new List<Deal>();
}