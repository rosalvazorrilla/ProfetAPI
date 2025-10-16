namespace ProfetAPI.Models;

public class AccountInternalUser
{
    public int AccountId { get; set; }
    public string UserId { get; set; } = null!;
    public string RoleInAccount { get; set; } = null!; // Ej: "SalesRep", "ProjectManager"

    // Propiedades de navegación
    public virtual Account Account { get; set; } = null!;
    public virtual ApplicationUser User { get; set; } = null!;
}