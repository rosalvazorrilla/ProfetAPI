// En el archivo Controllers/UsersController.cs
using profetApi.Models;
using profetApi.Dtos;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace profetApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UsersController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public UsersController(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        // GET: api/Users
        [HttpGet]
        public async Task<ActionResult<IEnumerable<UserDto>>> GetUsers()
        {
            // Usamos el UserManager para acceder a los usuarios
            var users = await _userManager.Users
                .Select(u => new UserDto // Mapeamos la Entidad (ApplicationUser) al DTO (UserDto)
                {
                    Id = u.Id,
                    UserName = u.UserName,
                    Email = u.Email,
                    IsActive = u.Active // Usamos el campo 'Active' que definimos
                })
                .ToListAsync();

            return Ok(users);
        }
    }
}