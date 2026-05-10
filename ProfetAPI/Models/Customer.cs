using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProfetAPI.Models;

public class Customer
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [Column("name")]
    public string Name { get; set; } = null!;

    [Column("phone")]
    public string? Phone { get; set; }

    [Column("initialDate")]
    public DateTime InitialDate { get; set; }

    // --- CAMPOS AGREGADOS PARA EL ONBOARDING Y ADMIN ---

    [Column("contact")]
    public string? Contact { get; set; } // Nombre de la persona a la que va dirigido el correo

    [Column("Email")]
    public string? Email { get; set; } // Vital para enviar el SetupLink

    [Column("SetupToken")]
    public string? SetupToken { get; set; } // El token de la URL mágica

    [Column("SetupStep")]
    public int SetupStep { get; set; } // Para saber en qué paso del Wizard se quedó

    [Column("Status")]
    public string Status { get; set; } = "Pendiente de Setup"; // El estatus visual para tu tabla

    [Column("Active")]
    public bool? Active { get; set; }

    [Column("Deleted")]
    public bool? Deleted { get; set; }

    // --- WHITE LABEL / BRANDING ---
    [Column("BrandName")]
    public string? BrandName { get; set; }       // Nombre de la app (reemplaza "Profet")

    [Column("BrandLogoUrl")]
    public string? BrandLogoUrl { get; set; }    // URL del logo grande (sidebar expandido)

    [Column("BrandLogoSmallUrl")]
    public string? BrandLogoSmallUrl { get; set; } // URL del logo pequeño/ícono (sidebar colapsado)

    [Column("BrandPrimaryColor")]
    public string? BrandPrimaryColor { get; set; } // Hex, ej: #1CAF9A

    [Column("BrandSecondaryColor")]
    public string? BrandSecondaryColor { get; set; } // Hex, ej: #5F6CAF

    [Column("BrandFaviconUrl")]
    public string? BrandFaviconUrl { get; set; } // URL del favicon (opcional)

    // --- INTEGRACIÓN WHATSAPP (2Chat) ---

    /// <summary>true = el canal de WhatsApp está habilitado para este tenant.</summary>
    [Column("hasWhatsApp")]
    public bool HasWhatsApp { get; set; } = false;

    /// <summary>Número de WhatsApp Business conectado a 2chat (sin '+', ej: 528181818181)</summary>
    [Column("WhatsappNumber")]
    public string? WhatsappNumber { get; set; }

    /// <summary>Identificador del canal en 2chat (ej: WPN5218181818181). Se extrae de la session_key del webhook.</summary>
    [Column("WhatsappChannel")]
    public string? WhatsappChannel { get; set; }

    /// <summary>API Key del usuario en 2chat (UAK...)</summary>
    [Column("TwoChatApiKey")]
    public string? TwoChatApiKey { get; set; }

    /// <summary>UUID del webhook de 2Chat para mensajes recibidos (para poder desuscribirse)</summary>
    public string? WebhookReceiveId { get; set; }

    /// <summary>UUID del webhook de 2Chat para mensajes enviados (para poder desuscribirse)</summary>
    public string? WebhookSentId { get; set; }

    // --- Propiedades de navegación ---
    // Un cliente tiene muchos usuarios y muchos equipos.
    public virtual ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
    public virtual ICollection<Team> Teams { get; set; } = new List<Team>();
    public virtual ICollection<SavedResponseWhatsapp> SavedResponses { get; set; } = new List<SavedResponseWhatsapp>();
}