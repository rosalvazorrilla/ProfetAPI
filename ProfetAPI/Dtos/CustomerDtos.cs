namespace ProfetAPI.Dtos
{
    public record CreateCustomerDto(
     string Name,
     string Contact,
     string Email,
     string? Phone
 );

    public record CustomerResponseDto(
        int Id,
        string Name,
        string? Contact,
        string? Email,
        string Status,
        string SetupUrl
    );

    public record UpdateCustomerDto(
        string Name,
        string Contact,
        string? Phone
    );
}
