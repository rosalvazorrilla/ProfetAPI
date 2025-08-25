using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models
{
    public class Team
    {
        [Key]
        public int Id { get; set; }

        public string Name { get; set; }

        public int? CustomerId { get; set; }

        // Propiedades de navegación
        public virtual Customer Customer { get; set; }
        public virtual ICollection<UserTeam> UserTeams { get; set; } = new List<UserTeam>();
    }
}
