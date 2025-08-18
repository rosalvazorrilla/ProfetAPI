// En el archivo Models/ApplicationRole.cs
using Microsoft.AspNetCore.Identity;

namespace ProfetAPI.Models
{
    public class ApplicationRole : IdentityRole
    {
        // Tu tabla Roles tiene una columna "description", la mapeamos aquí
        public string? description { get; set; }
    }
}