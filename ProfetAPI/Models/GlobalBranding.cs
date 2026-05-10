using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProfetAPI.Models;

/// <summary>
/// Configuración de marca global de la plataforma (logo grande, logo pequeño, colores por defecto).
/// Tabla de una sola fila — siempre Id = 1.
/// Los tenants que no configuran su propia marca heredan estos valores.
/// </summary>
[Table("GlobalBranding")]
public class GlobalBranding
{
    [Key]
    [Column("Id")]
    public int Id { get; set; } = 1;

    [Column("AppName")]
    public string? AppName { get; set; }           // Nombre de la plataforma (ej: "Profet")

    [Column("LogoLargeUrl")]
    public string? LogoLargeUrl { get; set; }      // Logo completo (sidebar expandido, emails, etc.)

    [Column("LogoSmallUrl")]
    public string? LogoSmallUrl { get; set; }      // Ícono cuadrado (sidebar colapsado, favicon móvil)

    [Column("PrimaryColor")]
    public string? PrimaryColor { get; set; }      // Hex, ej: #1CAF9A

    [Column("SecondaryColor")]
    public string? SecondaryColor { get; set; }    // Hex, ej: #5F6CAF

    [Column("FaviconUrl")]
    public string? FaviconUrl { get; set; }        // .ico o PNG 32x32
}
