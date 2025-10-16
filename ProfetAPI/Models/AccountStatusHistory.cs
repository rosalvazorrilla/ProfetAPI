using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProfetAPI.Models;

public class AccountStatusHistory
{
    [Key]
    [Column("id")]
    public long Id { get; set; }
    public int AccountId { get; set; }

    [Column("initial_date")]
    public DateTime InitialDate { get; set; }

    [Column("end_date")]
    public DateTime? EndDate { get; set; }

    [Column("active_days")]
    public int? ActiveDays { get; set; }
    
    [Required]
    [StringLength(50)]
    public string Status { get; set; } = "Por Iniciar";

    // Propiedad de navegación
    public virtual Account Account { get; set; } = null!;
}