namespace ProfetAPI.Models
{
    public class UserTeam
    {
        public string UserId { get; set; }
        public int TeamId { get; set; }

        // Propiedades de navegación
        public virtual ApplicationUser User { get; set; }
        public virtual Team Team { get; set; }
    }
}
