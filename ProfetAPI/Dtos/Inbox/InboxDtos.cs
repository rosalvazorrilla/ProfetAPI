namespace ProfetAPI.Dtos.Inbox;

public class InboxConversationDto
{
    public int      WhatsappContactId { get; set; }
    public long?     LeadId           { get; set; }
    public string    Name             { get; set; } = "";
    public string?   Phone            { get; set; }
    public string?   Email            { get; set; }
    public string?   AvatarUrl        { get; set; }
    public string    LastChannel      { get; set; } = "whatsapp"; // whatsapp | email
    public string?   LastMessagePreview { get; set; }
    public DateTime? LastAt           { get; set; }
    public int       UnreadCount      { get; set; }
    public string?   TierName         { get; set; }
    public string?   TierColor        { get; set; }
}

public class InboxMessageDto
{
    public string    Channel   { get; set; } = "whatsapp"; // whatsapp | email
    public string    Direction { get; set; } = "in";       // in | out
    public string?   Subject   { get; set; }
    public string?   Body      { get; set; }
    public string?   MediaUrl  { get; set; }
    public DateTime  At        { get; set; }
}

public class InboxReplyRequestDto
{
    /// <summary>"whatsapp" | "email"</summary>
    public string  Channel  { get; set; } = "whatsapp";
    public string? Text     { get; set; }     // whatsapp
    public string? Subject  { get; set; }     // email
    public string? BodyHtml { get; set; }     // email
}

public class InboxSummaryDto
{
    public bool   Available { get; set; }
    public string Summary   { get; set; } = "";
}

public class InboxSuggestedReplyDto
{
    public bool   Available { get; set; }
    public string Reply     { get; set; } = "";
}
