using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore; // Añade esto
using ProfetAPI.Data;
using ProfetAPI.Models;
using ProfetAPI.Dtos;

namespace ProfetAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UsersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<ApplicationRole> _roleManager; // Añadido

        public UsersController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            RoleManager<ApplicationRole> roleManager) // Añadido
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager; // Añadido
        }

        // POST: api/users/create-global-admin
        [HttpPost("create-global-admin")]
        public async Task<IActionResult> CreateGlobalAdmin([FromBody] CreateUserDto userDto)
        {
            // Regla de seguridad: Solo permite crear un Admin Global si no existe ninguno.
            var adminRoleExists = await _roleManager.RoleExistsAsync("AdminGlobal");
            if (!adminRoleExists)
            {
                await _roleManager.CreateAsync(new ApplicationRole { Name = "AdminGlobal" });
            }

            var admins = await _userManager.GetUsersInRoleAsync("AdminGlobal");
            if (admins.Any())
            {
                return BadRequest(new { message = "Ya existe un Administrador Global." });
            }

            return await CreateUserInternal(userDto, "Internal");
        }

        // Método privado para crear cualquier usuario
        private async Task<IActionResult> CreateUserInternal(CreateUserDto userDto, string userType)
        {
            var user = new ApplicationUser
            {
                UserName = userDto.Email,
                Email = userDto.Email,
                CustomerId = userDto.CustomerId,
                CreatedOn = DateTime.UtcNow,
            };

            var result = await _userManager.CreateAsync(user, userDto.Password);
            if (!result.Succeeded) return BadRequest(result.Errors);

            // Asegurarse de que el rol existe antes de asignarlo
            if (!await _roleManager.RoleExistsAsync(userDto.Role))
            {
                await _roleManager.CreateAsync(new ApplicationRole { Name = userDto.Role });
            }
            await _userManager.AddToRoleAsync(user, userDto.Role);

            var userProfile = new UserProfile
            {
                UserId = user.Id,
                FirstName = userDto.FirstName,
                LastName = userDto.LastName,
                Phone = userDto.Phone, // Asumiendo que quieres añadir esto al DTO
            };
            _context.UserProfiles.Add(userProfile);
            await _context.SaveChangesAsync();

            return Ok(new { UserId = user.Id, Email = user.Email, Role = userDto.Role });
        }
    }
}