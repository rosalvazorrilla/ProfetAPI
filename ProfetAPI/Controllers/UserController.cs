using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ProfetAPI.Data;
using ProfetAPI.Dtos;
using ProfetAPI.Models;
using Swashbuckle.AspNetCore.Annotations; // <--- 1. AGREGADO

namespace ProfetAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [SwaggerTag("Gestión de Usuarios (Admin)")] // <--- 2. TÍTULO DE SECCIÓN
    public class UsersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<ApplicationRole> _roleManager;

        public UsersController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            RoleManager<ApplicationRole> roleManager)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
        }

        // POST: api/users/create-global-admin
        [HttpPost("create-global-admin")]
        // --- 3. DOCUMENTACIÓN DEL MÉTODO ---
        [SwaggerOperation(
            Summary = "Crear Admin Global",
            Description = "Crea un superusuario interno, asigna el rol 'AdminGlobal' y crea su UserProfile."
        )]
        [SwaggerResponse(200, "Usuario creado correctamente")]
        [SwaggerResponse(400, "Datos inválidos o el usuario ya existe")]
        [SwaggerResponse(500, "Error interno al guardar en BD")]
        public async Task<IActionResult> CreateGlobalAdmin([FromBody] CreateUserDto model)
        {
            // 1. Validar el modelo
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            // 2. Verificar si el correo ya existe
            var userExists = await _userManager.FindByEmailAsync(model.Email);
            if (userExists != null)
                return BadRequest(new { message = "El usuario con ese correo ya existe." });

            // 3. Preparar el objeto ApplicationUser (TU LÓGICA ORIGINAL)
            ApplicationUser user = new ApplicationUser()
            {
                Email = model.Email,
                UserName = model.Email,
                SecurityStamp = Guid.NewGuid().ToString(),
                Active = true,
                Deleted = false,
                CreatedOn = DateTime.UtcNow,
                UserType = "Internal", // Admin Global es interno
                CustomerId = model.CustomerId, // Será null para Admin Global
                EmailConfirmed = true
            };

            // 4. Crear el usuario en BD
            var result = await _userManager.CreateAsync(user, model.Password);

            if (!result.Succeeded)
            {
                return StatusCode(500, new
                {
                    message = "Error al crear usuario",
                    errors = result.Errors.Select(e => e.Description)
                });
            }

            string roleToAssign = !string.IsNullOrEmpty(model.Role) ? model.Role : "AdminGlobal";

            // Validar/Crear Rol
            if (!await _roleManager.RoleExistsAsync(roleToAssign))
            {
                var newRole = new ApplicationRole
                {
                    Name = roleToAssign,
                };
                await _roleManager.CreateAsync(newRole);
            }

            await _userManager.AddToRoleAsync(user, roleToAssign);

            // Crear Perfil (UserProfile)
            var userProfile = new UserProfile
            {
                UserId = user.Id,
                FirstName = model.FirstName,
                LastName = model.LastName,
                Phone = model.Phone,
                Preferences = "{}"
            };

            _context.UserProfiles.Add(userProfile);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Usuario Admin Global creado exitosamente", userId = user.Id });
        }
    }
}