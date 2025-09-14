using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using ProfetAPI.Dtos;

namespace ProfetAPI.Models
{
    public class UserProfile
    {
       
        [Key]
        public string UserId { get; set; }

        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Phone { get; set; }
        public string? PhoneExt { get; set; }
        public string? Mobile { get; set; }
        public string? IndustrySector { get; set; }
        public string? CallPickerExtensionName { get; set; }
        public string? CallPickerExtension { get; set; }
        public string? CallPickerKey { get; set; }
        public bool? ProfilePicture { get; set; }
        public string? Pass64 { get; set; }
        public bool? IsAdmin { get; set; }
        public DateTime? LastLoginDate { get; set; }
        
        // Esta es la columna que guarda el JSON como texto plano en la DB
        public string? Preferences { get; set; }


        // --- PROPIEDAD "AYUDANTE" (NO SE GUARDA EN LA DB) ---
        
        [NotMapped] // <-- Esto le dice a Entity Framework que ignore esta propiedad
        public UserPreferences PreferencesAsObject
        {
            // Cuando pides los datos, convierte el texto (JSON) a un objeto C#
            get => string.IsNullOrEmpty(Preferences) 
                ? new UserPreferences() 
                : JsonSerializer.Deserialize<UserPreferences>(Preferences) ?? new UserPreferences();
            
            // Cuando guardas los datos, convierte el objeto C# a texto (JSON)
            set => Preferences = JsonSerializer.Serialize(value);
        }


        // --- PROPIEDAD DE NAVEGACIÓN ---
        
        public virtual ApplicationUser User { get; set; }
    
    }
}
