namespace ProfetAPI.Models;

public class Tagging
{
    public int TagId { get; set; }
    public long EntityId { get; set; } // Puede ser un LeadId, DealId, ContactId, etc.
    public string EntityType { get; set; } = null!; // "Lead", "Deal", "Contact"

    // Propiedad de navegación
    public virtual Tag Tag { get; set; } = null!;
}