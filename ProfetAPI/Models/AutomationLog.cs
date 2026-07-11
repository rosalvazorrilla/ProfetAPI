using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Models;

public class AutomationLog
{
    [Key]
    public long LogId { get; set; }

    public int RuleId { get; set; }

    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;

    public bool Success { get; set; }

    /// <summary>JSON con resultado por paso: [{ "step": "HttpPost", "ok": true, "status": 200 }, ...]</summary>
    public string? StepsResultJson { get; set; }

    [MaxLength(1000)]
    public string? ErrorMessage { get; set; }

    /// <summary>Primeros 500 chars del payload entrante (para debug)</summary>
    [MaxLength(500)]
    public string? PayloadPreview { get; set; }

    public virtual AutomationRule? Rule { get; set; }
}
