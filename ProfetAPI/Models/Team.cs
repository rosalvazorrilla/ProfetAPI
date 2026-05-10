using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProfetAPI.Models
{
    public class Team
    {
        [Key]
        public int Id { get; set; }

        public string Name { get; set; }

        public int? CustomerId { get; set; }

        /// <summary>
        /// ID del usuario que lidera este equipo (Manager).
        /// Define quién puede ver los leads de todos los miembros del equipo.
        /// </summary>
        [Column("LeaderId")]
        public string? LeaderId { get; set; }

        // Propiedades de navegación
        public virtual Customer Customer { get; set; }
        public virtual ApplicationUser? Leader { get; set; }
        public virtual ICollection<UserTeam> UserTeams { get; set; } = new List<UserTeam>();
    }
}
