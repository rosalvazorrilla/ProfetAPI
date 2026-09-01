using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

/// <summary>
/// API Key para que sistemas externos consuman la API (crear/actualizar leads,
/// contactos, consultar catálogos) sin usar el usuario/password de una persona
/// real. Vive por Account — la key solo puede tocar esa cuenta, nunca otra.
/// La key en texto plano se muestra UNA sola vez al crearla; después solo se
/// guarda su hash (para validar cada request) y una copia cifrada reversible
/// (para que Admin Global pueda volver a verla si hace falta, mismo criterio
/// que las contraseñas temporales de usuarios).
/// </summary>
public class AccountApiKey
{
    [Key]
    public int Id { get; set; }

    public int AccountId { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty; // etiqueta libre, ej. "Zapier", "Sitio web"

    [MaxLength(12)]
    public string Prefix { get; set; } = string.Empty; // primeros caracteres visibles sin descifrar, ej. "pfk_3f9a"

    [Required, MaxLength(64)]
    public string KeyHash { get; set; } = string.Empty; // SHA-256 hex de la key completa — usado para validar cada request

    public string? KeyEncrypted { get; set; } // copia reversible (SecretProtector) para poder mostrarla de nuevo

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public virtual Account? Account { get; set; }
}
