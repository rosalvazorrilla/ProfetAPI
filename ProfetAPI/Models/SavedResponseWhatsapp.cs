using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace ProfetAPI.Models;

/// <summary>Respuestas rápidas guardadas por tenant para el chat de WhatsApp.</summary>
[Table("SavedResponseWhatsapps")]
public class SavedResponseWhatsapp
{
    [Key]
    public int Id { get; set; }

    /// <summary>Tenant al que pertenece la respuesta</summary>
    public int CustomerId { get; set; }

    /// <summary>Nombre corto del template (ej: "Saludo inicial")</summary>
    [Required]
    [StringLength(200)]
    public string Identifier { get; set; } = null!;

    /// <summary>Contenido del mensaje (puede tener placeholders: {nombre}, {empresa})</summary>
    [Required]
    public string MessageTemplate { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navegación
    public virtual Customer Customer { get; set; } = null!;
}
