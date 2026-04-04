using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Dtos;
using ProfetAPI.Models;
using Swashbuckle.AspNetCore.Annotations;

namespace ProfetAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [SwaggerTag("Gesti�n de Usuarios (Admin)")] // <--- 2. T�TULO DE SECCI�N
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
        // --- 3. DOCUMENTACI�N DEL M�TODO ---
        [SwaggerOperation(
            Summary = "Crear Admin Global",
            Description = "Crea un superusuario interno, asigna el rol 'AdminGlobal' y crea su UserProfile."
        )]
        [SwaggerResponse(200, "Usuario creado correctamente")]
        [SwaggerResponse(400, "Datos inv�lidos o el usuario ya existe")]
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

            // 3. Preparar el objeto ApplicationUser (TU L�GICA ORIGINAL)
            ApplicationUser user = new ApplicationUser()
            {
                Email = model.Email,
                UserName = model.Email,
                SecurityStamp = Guid.NewGuid().ToString(),
                Active = true,
                Deleted = false,
                CreatedOn = DateTime.UtcNow,
                UserType = "Internal", // Admin Global es interno
                CustomerId = model.CustomerId, // Ser� null para Admin Global
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

        // GET: api/users
        [HttpGet]
        [Authorize(Roles = "AdminGlobal")]
        [SwaggerOperation(Summary = "Listar usuarios Admin Global", Description = "Devuelve todos los usuarios internos activos con su perfil y rol.")]
        [SwaggerResponse(200, "Lista de usuarios", typeof(List<AdminUserResponseDto>))]
        public async Task<IActionResult> GetAll()
        {
            var users = await _context.Users
                .Include(u => u.UserProfile)
                .Where(u => u.Deleted == false && u.UserType == "Internal")
                .ToListAsync();

            var result = new List<AdminUserResponseDto>();
            foreach (var u in users)
            {
                var roles = await _userManager.GetRolesAsync(u);
                result.Add(new AdminUserResponseDto
                {
                    Id = u.Id,
                    Email = u.Email,
                    FirstName = u.UserProfile?.FirstName,
                    LastName = u.UserProfile?.LastName,
                    Phone = u.UserProfile?.Phone,
                    Role = roles.FirstOrDefault(),
                    Active = u.Active ?? false
                });
            }

            return Ok(result);
        }

        // GET: api/users/{id}
        [HttpGet("{id}")]
        [Authorize(Roles = "AdminGlobal")]
        [SwaggerOperation(Summary = "Obtener usuario por ID")]
        [SwaggerResponse(200, "Usuario encontrado", typeof(AdminUserResponseDto))]
        [SwaggerResponse(404, "No encontrado")]
        public async Task<IActionResult> GetById(string id)
        {
            var u = await _context.Users.Include(u => u.UserProfile)
                .FirstOrDefaultAsync(u => u.Id == id && u.Deleted == false);
            if (u == null) return NotFound(new { message = "Usuario no encontrado." });

            var roles = await _userManager.GetRolesAsync(u);
            return Ok(new AdminUserResponseDto
            {
                Id = u.Id,
                Email = u.Email,
                FirstName = u.UserProfile?.FirstName,
                LastName = u.UserProfile?.LastName,
                Phone = u.UserProfile?.Phone,
                Role = roles.FirstOrDefault(),
                Active = u.Active ?? false
            });
        }

        // PUT: api/users/{id}
        [HttpPut("{id}")]
        [Authorize(Roles = "AdminGlobal")]
        [SwaggerOperation(Summary = "Actualizar usuario", Description = "Actualiza nombre, apellido y teléfono del usuario.")]
        [SwaggerResponse(200, "Usuario actualizado", typeof(AdminUserResponseDto))]
        [SwaggerResponse(404, "No encontrado")]
        public async Task<IActionResult> Update(string id, [FromBody] UpdateAdminUserDto model)
        {
            var u = await _context.Users.Include(u => u.UserProfile)
                .FirstOrDefaultAsync(u => u.Id == id && u.Deleted == false);
            if (u == null) return NotFound(new { message = "Usuario no encontrado." });

            if (u.UserProfile == null)
            {
                u.UserProfile = new UserProfile { UserId = id };
                _context.UserProfiles.Add(u.UserProfile);
            }

            if (!string.IsNullOrWhiteSpace(model.FirstName)) u.UserProfile.FirstName = model.FirstName;
            if (!string.IsNullOrWhiteSpace(model.LastName)) u.UserProfile.LastName = model.LastName;
            if (!string.IsNullOrWhiteSpace(model.Phone)) u.UserProfile.Phone = model.Phone;

            await _context.SaveChangesAsync();

            var roles = await _userManager.GetRolesAsync(u);
            return Ok(new AdminUserResponseDto
            {
                Id = u.Id,
                Email = u.Email,
                FirstName = u.UserProfile.FirstName,
                LastName = u.UserProfile.LastName,
                Phone = u.UserProfile.Phone,
                Role = roles.FirstOrDefault(),
                Active = u.Active ?? false
            });
        }

        // DELETE: api/users/{id}
        [HttpDelete("{id}")]
        [Authorize(Roles = "AdminGlobal")]
        [SwaggerOperation(Summary = "Desactivar usuario (soft delete)", Description = "Marca el usuario como eliminado. No borra físicamente el registro.")]
        [SwaggerResponse(200, "Usuario desactivado")]
        [SwaggerResponse(404, "No encontrado")]
        public async Task<IActionResult> Delete(string id)
        {
            var u = await _context.Users.FirstOrDefaultAsync(u => u.Id == id && u.Deleted == false);
            if (u == null) return NotFound(new { message = "Usuario no encontrado." });

            u.Deleted = true;
            u.Active = false;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Usuario desactivado correctamente." });
        }
    }
}