namespace ProfetAPI.Dtos.Calls;

public class ClickToCallRequestDto
{
    public long LeadId { get; set; }
    public string? Phone { get; set; }
}

public class CallConfigDto
{
    public bool Configured { get; set; }
    public string? Extension { get; set; }
}

public class CallItemDto
{
    public int ActivityId { get; set; }
    public string Direction { get; set; } = "out"; // in | out
    public string? Status { get; set; }
    public DateTime? At { get; set; }
    public string? Phone { get; set; }
    public string? OwnerName { get; set; }
    public string? Duration { get; set; }
    public string? RecordingUrl { get; set; }
}
