using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProfetAPI.Models;

public class Company
{
    [Key]
    public int CompanyId { get; set; }

    /// <summary>Cuenta dueña de esta compañía cuando se crea a mano ("+ Nueva compañía").
    /// Null en compañías viejas que solo se conocen por sus Deals/Leads ligados — GetCompanies
    /// las encuentra igual por esa vía. Sin este campo, una compañía creada directamente no
    /// tenía NINGUNA forma de aparecer en ningún listado por cuenta.</summary>
    public int? AccountId { get; set; }
    [ForeignKey(nameof(AccountId))]
    public virtual Account? Account { get; set; }

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