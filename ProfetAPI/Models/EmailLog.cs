using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProfetAPI.Models;

public class EmailLog
{
    [Key]
    public int EmailLogId { get; set; }

    // ── Tenant / contexto ─────────────────────────────────────────────────────
    public int? AccountId { get; set; }

    // ── Entidad relacionada (polimórfico — solo uno a la vez) ─────────────────
    public int? LeadId    { get; set; }
    public int? DealId    { get; set; }
    public int? ContactId { get; set; }

    // ── Quién envió ───────────────────────────────────────────────────────────
    public string? SentByUserId { get; set; }

    // ── Destinatarios ─────────────────────────────────────────────────────────
    [Required, MaxLength(320)]
    public string ToAddress { get; set; } = null!;

    [MaxLength(320)]
    public string? CcAddress { get; set; }

    // ── Contenido ─────────────────────────────────────────────────────────────
    [Required, MaxLength(500)]
    public string Subject { get; set; } = null!;

    [Required]
    public string BodyHtml { get; set; } = null!;

    // ── Resultado ─────────────────────────────────────────────────────────────
    public DateTime SentAt     { get; set; } = DateTime.UtcNow;
    public bool     IsSuccess  { get; set; } = true;
    public string?  ErrorMessage { get; set; }

    // ── Navegación ───────────────────────────────────────────────────────────
    public virtual ApplicationUser? SentByUser { get; set; }
}
