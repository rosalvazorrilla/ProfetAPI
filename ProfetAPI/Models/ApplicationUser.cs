using Microsoft.AspNetCore.Identity;

namespace ProfetAPI.Models
{
    public class ApplicationUser : IdentityUser
    {
        // Propiedades que existen en tu tabla Users
        public int? CustomerId { get; set; }
        public string? ParentId { get; set; }
        public bool Active { get; set; } = true;
    }
}