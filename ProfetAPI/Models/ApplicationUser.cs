using Microsoft.AspNetCore.Identity;

namespace ProfetAPI.Models
{
    public class ApplicationUser : IdentityUser
    {
        // Llaves Foráneas
        public int? CustomerId { get; set; }
        public string? ParentId { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public string UserType { get; set; } = "Client"; // <--- ESTA ES LA QUE TE DA EL ERROR
        public bool? Active { get; set; }   // <--- AGREGAR ESTO
        public bool? Deleted { get; set; }  // <--- AGREGAR ESTO
        public bool? HasWhatsApp { get; set; }
        // Propiedades de Navegación
        public virtual Customer? Customer { get; set; }
        public virtual UserProfile? UserProfile { get; set; }
        public virtual ApplicationUser? Parent { get; set; }
        public virtual ICollection<ApplicationUser> Subordinates { get; set; } = new List<ApplicationUser>();
        public virtual ICollection<UserTeam> UserTeams { get; set; } = new List<UserTeam>();
    }
}