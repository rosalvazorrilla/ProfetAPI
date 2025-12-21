using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using ProfetAPI.Dtos;
using ProfetAPI.Models;
using Swashbuckle.AspNetCore.Annotations; // <--- 1. AGREGADO
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace ProfetAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [SwaggerTag("Autenticación y Seguridad")] // <--- 2. TÍTULO DE SECCIÓN
    public class AuthController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IConfiguration _configuration;

        public AuthController(UserManager<ApplicationUser> userManager, IConfiguration configuration)
        {
            _userManager = userManager;
            _configuration = configuration;
        }

        [HttpPost("login")]
        // --- 3. DOCUMENTACIÓN DEL MÉTODO ---
        [SwaggerOperation(
            Summary = "Iniciar Sesión",
            Description = "Valida credenciales y devuelve el Token JWT con los Claims del usuario (Rol, CustomerId, etc)."
        )]
        [SwaggerResponse(200, "Login exitoso", typeof(LoginResponseDto))] // <--- Define qué devuelve
        [SwaggerResponse(401, "Usuario inactivo, eliminado o contraseña incorrecta")]
        public async Task<IActionResult> Login([FromBody] LoginDto model)
        {
            // 1. Buscar usuario por email
            var user = await _userManager.FindByEmailAsync(model.Email);

            // 2. Validar si existe y si la contraseña es correcta
            if (user != null && await _userManager.CheckPasswordAsync(user, model.Password))
            {
                // 3. Validar si está activo (TU LÓGICA ORIGINAL)
                if (user.Active == false || user.Deleted == true)
                {
                    return Unauthorized(new { message = "El usuario está inactivo o eliminado." });
                }

                // 4. Obtener roles
                var userRoles = await _userManager.GetRolesAsync(user);

                // 5. Crear los "Claims" (Datos que van dentro del token)
                var authClaims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, user.UserName ?? ""),
                    new Claim(ClaimTypes.NameIdentifier, user.Id), // El ID del usuario
                    new Claim("CustomerId", user.CustomerId?.ToString() ?? "0"), // Tu ID de cliente
                    new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                };

                foreach (var role in userRoles)
                {
                    authClaims.Add(new Claim(ClaimTypes.Role, role));
                }

                // 6. Generar la firma y el token
                var authSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["JWT:Secret"]!));

                var token = new JwtSecurityToken(
                    issuer: _configuration["JWT:ValidIssuer"],
                    audience: _configuration["JWT:ValidAudience"],
                    expires: DateTime.Now.AddHours(8),
                    claims: authClaims,
                    signingCredentials: new SigningCredentials(authSigningKey, SecurityAlgorithms.HmacSha256)
                );

                // Devolvemos el objeto LoginResponseDto para que coincida con la documentación
                return Ok(new LoginResponseDto
                {
                    Token = new JwtSecurityTokenHandler().WriteToken(token),
                    Expiration = token.ValidTo,
                    UserId = user.Id,
                    Email = user.Email ?? "",
                    Role = userRoles.FirstOrDefault() ?? "User"
                });
            }

            return Unauthorized(new { message = "Usuario o contraseña incorrectos" });
        }
    }
}