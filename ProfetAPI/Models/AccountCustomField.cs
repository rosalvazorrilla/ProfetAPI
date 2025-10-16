namespace ProfetAPI.Models;

public class AccountCustomField
{
    public int AccountId { get; set; }
    public int FieldId { get; set; }
    public bool IsVisibleOnCard { get; set; }

    public virtual Account Account { get; set; } = null!;
    public virtual CustomFieldDefinition CustomFieldDefinition { get; set; } = null!;
}