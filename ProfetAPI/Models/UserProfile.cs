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
        public DateTime? LastLoginDate { get; set; }
        public string? Preferences { get; set; } // El JSON como texto

        // Y todos los demás campos de perfil que definimos...
        public string? PhoneExt { get; set; }
        public string? Mobile { get; set; }
        public string? CallPickerExtensionName { get; set; }
        // ...etc.

        [NotMapped]
        public UserPreferences PreferencesAsObject
        {
            get => string.IsNullOrEmpty(Preferences) ? new UserPreferences() : JsonSerializer.Deserialize<UserPreferences>(Preferences) ?? new UserPreferences();
            set => Preferences = JsonSerializer.Serialize(value);
        }

        public virtual ApplicationUser User { get; set; }
    }
}
