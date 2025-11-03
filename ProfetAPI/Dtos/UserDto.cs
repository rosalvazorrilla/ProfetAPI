// En el archivo Dtos/UserDto.cs
namespace ProfetAPI.Dtos
{
    public class UserDto
    {
        public string Id { get; set; }
        public string? UserName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }

        public bool IsActive { get; set; }
    }
}