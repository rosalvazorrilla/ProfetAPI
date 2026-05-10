using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Dtos.Admin;
using ProfetAPI.Models;
using Swashbuckle.AspNetCore.Annotations;

namespace ProfetAPI.Controllers;

/// <summary>
/// Gestión de usuarios de un cliente desde el panel Admin Global.
/// </summary>
[Route("api/admin/customers/{customerId}/users")]
[ApiController]
[Authorize(Roles = "AdminGlobal")]
[SwaggerTag("Admin Global — Usuarios de Clientes")]
public class AdminCustomerUsersController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public AdminCustomerUsersController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    private async Task<bool> CustomerExists(int customerId) =>
        await _context.Customers.AnyAsync(c => c.Id == customerId && c.Deleted == false);

    // GET /api/admin/customers/{customerId}/users
    [HttpGet]
    [SwaggerOperation(Summary = "Listar todos los usuarios del cliente")]
    [SwaggerResponse(200, "Lista de usuarios", typeof(List<AdminAccountUserResponseDto>))]
    [SwaggerResponse(404, "Cliente no encontrado")]
    public async Task<IActionResult> GetAll(int customerId)
    {
        if (!await CustomerExists(customerId))
            return NotFound(new { message = "Cliente no encontrado." });

        var users = await _context.Users
            .Include(u => u.UserProfile)
            .Where(u => u.CustomerId == customerId && u.Deleted == false)
            .ToListAsync();

        var result = new List<AdminAccountUserResponseDto>();
        foreach (var u in users)
        {
            var roles = await _userManager.GetRolesAsync(u);
            result.Add(new AdminAccountUserResponseDto
            {
                UserId = u.Id,
                Email = u.Email ?? "",
                FullName = $"{u.UserProfile?.FirstName} {u.UserProfile?.LastName}".Trim(),
                Role = roles.FirstOrDefault() ?? "SalesRep",
                Active = u.Active ?? false
            });
        }
        return Ok(result);
    }

    // POST /api/admin/customers/{customerId}/users
    [HttpPost]
    [SwaggerOperation(Summary = "Crear usuario para el cliente", Description = "Se crea con Active=false hasta que se active manualmente o al completar el setup.")]
    [SwaggerResponse(201, "Usuario creado", typeof(AdminAccountUserResponseDto))]
    [SwaggerResponse(400, "Email ya registrado o datos inválidos")]
    [SwaggerResponse(404, "Cliente no encontrado")]
    public async Task<IActionResult> Create(int customerId, [FromBody] CreateAdminUserDto model)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (!await CustomerExists(customerId))
            return NotFound(new { message = "Cliente no encontrado." });

        var existing = await _userManager.FindByEmailAsync(model.Email);
        if (existing != null)
            return BadRequest(new { message = $"El email '{model.Email}' ya está registrado." });

        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            CustomerId = customerId,
            Active = false,
            Deleted = false,
            UserType = "Client",
            CreatedOn = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
            return BadRequest(new { message = "Error al crear usuario.", errors = result.Errors });

        await _userManager.AddToRoleAsync(user, model.Role);

        _context.UserProfiles.Add(new UserProfile
        {
            UserId = user.Id,
            FirstName = model.FirstName,
            LastName = model.LastName,
            Phone = model.Phone
        });
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAll), new { customerId }, new AdminAccountUserResponseDto
        {
            UserId = user.Id,
            Email = user.Email!,
            FullName = $"{model.FirstName} {model.LastName}".Trim(),
            Role = model.Role,
            Active = false
        });
    }

    // DELETE /api/admin/customers/{customerId}/users/{userId}
    [HttpDelete("{userId}")]
    [SwaggerOperation(Summary = "Eliminar usuario del cliente (soft delete)")]
    [SwaggerResponse(204, "Eliminado")]
    [SwaggerResponse(404, "Usuario no encontrado")]
    public async Task<IActionResult> Delete(int customerId, string userId)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId && u.CustomerId == customerId && u.Deleted == false);
        if (user == null) return NotFound(new { message = "Usuario no encontrado." });

        user.Deleted = true;
        user.Active = false;
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
