using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProfetAPI.Models;

/// <summary>Configuración individual del usuario: zona horaria, tema y foto de perfil.</summary>
[Table("UserSettings")]
public class UserSetting
{
    [Key]
    [StringLength(450)]
    public string UserId { get; set; } = null!;

    [StringLength(64)]
    public string? TimeZoneId { get; set; }

    /// <summary>light | dark | system.</summary>
    [StringLength(10)]
    public string Theme { get; set; } = "light";

    [StringLength(500)]
    public string? AvatarUrl { get; set; }

    public DateTime UpdatedOn { get; set; } = DateTime.UtcNow;
}
