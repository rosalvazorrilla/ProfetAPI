namespace ProfetAPI.Dtos
{
    public class LoginResponseDto
    {
        public string Token { get; set; }
        public DateTime Expiration { get; set; }
        public string UserId { get; set; }
        public string Email { get; set; }
        public string Role { get; set; }
    }
}