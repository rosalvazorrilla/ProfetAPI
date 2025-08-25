using Microsoft.AspNetCore.Identity;

namespace ProfetAPI.Models
{
    public class ApplicationUser : IdentityUser
    {
        // Llaves Foráneas
        public int? CustomerId { get; set; }
        public string? ParentId { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

        // Propiedades de Navegación
        public virtual Customer? Customer { get; set; }
        public virtual UserProfile? UserProfile { get; set; }
        public virtual ApplicationUser? Parent { get; set; }
        public virtual ICollection<ApplicationUser> Subordinates { get; set; } = new List<ApplicationUser>();
        public virtual ICollection<UserTeam> UserTeams { get; set; } = new List<UserTeam>();
    }
}