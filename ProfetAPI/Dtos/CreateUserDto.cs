using System.ComponentModel.DataAnnotations;
namespace ProfetAPI.Dtos
{
    public class CreateUserDto
    {
        [Required]
        [EmailAddress]
        public string? Email { get; set; }
        [Required]
        public string? Password { get; set; }
        [Required]
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        [Required]
        public string? Phone { get; set; }
        public string? Role { get; set; }
        public int? CustomerId { get; set; }
    }
}