namespace ProfetAPI.Dtos
{
    public class UserDetailDto
    {
        public string Id { get; set; }
        public string? Email { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? CustomerName { get; set; }
        public string? ManagerName { get; set; }
        public List<string> Teams { get; set; } = new();
        public List<string> Roles { get; set; } = new();
        public UserPreferences? Preferences { get; set; }
    }
}
