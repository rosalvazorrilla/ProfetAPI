using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Models;
using Swashbuckle.AspNetCore.Annotations;

namespace ProfetAPI.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
[SwaggerTag("Perfil del usuario autenticado")]
public class ProfileController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public ProfileController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    private string CurrentUserId => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
    private string? CurrentUserRole => User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

    // GET /api/profile  — datos del usuario logueado
    [HttpGet]
    [SwaggerOperation(Summary = "Obtener perfil del usuario autenticado")]
    [SwaggerResponse(200, "Perfil del usuario")]
    public async Task<IActionResult> GetProfile()
    {
        var user = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == CurrentUserId)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.UserName,
                u.CustomerId,
                u.UserType,
                u.CreatedOn,
                firstName = u.UserProfile != null ? u.UserProfile.FirstName : null,
                lastName  = u.UserProfile != null ? u.UserProfile.LastName  : null,
                phone     = u.UserProfile != null ? u.UserProfile.Phone     : null,
                mobile    = u.UserProfile != null ? u.UserProfile.Mobile    : null,
                phoneExt  = u.UserProfile != null ? u.UserProfile.PhoneExt : null,
            })
            .FirstOrDefaultAsync();

        if (user == null) return NotFound(new { message = "Usuario no encontrado." });

        // Account info
        var accountInfo = await _context.AccountInternalUsers
            .AsNoTracking()
            .Where(a => a.UserId == CurrentUserId)
            .Select(a => new
            {
                a.AccountId,
                accountName    = a.Account.Name,
                a.RoleInAccount,
                customerName   = a.Account.Customer != null ? a.Account.Customer.Name : null,
                customerId     = a.Account.CustomerId,
                accountStatus  = a.Account.Status,
            })
            .FirstOrDefaultAsync();

        // Customer info (if linked)
        object? customerInfo = null;
        if (user.CustomerId.HasValue)
        {
            customerInfo = await _context.Customers
                .AsNoTracking()
                .Where(c => c.Id == user.CustomerId.Value)
                .Select(c => new { c.Id, c.Name })
                .FirstOrDefaultAsync();
        }

        return Ok(new
        {
            userId     = user.Id,
            email      = user.Email,
            userName   = user.UserName,
            customerId = user.CustomerId,
            userType   = user.UserType,
            role       = CurrentUserRole,
            createdOn  = user.CreatedOn,
            firstName  = user.firstName,
            lastName   = user.lastName,
            phone      = user.phone,
            mobile     = user.mobile,
            phoneExt   = user.phoneExt,
            account    = accountInfo,
            customer   = customerInfo,
        });
    }

    // PUT /api/profile  — actualizar perfil personal
    [HttpPut]
    [SwaggerOperation(Summary = "Actualizar perfil personal")]
    [SwaggerResponse(200, "Perfil actualizado")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto model)
    {
        var user = await _context.Users.FindAsync(CurrentUserId);
        if (user == null) return NotFound(new { message = "Usuario no encontrado." });

        var profile = await _context.UserProfiles.FindAsync(CurrentUserId);
        if (profile == null)
        {
            profile = new UserProfile { UserId = CurrentUserId };
            _context.UserProfiles.Add(profile);
        }

        profile.FirstName = model.FirstName ?? profile.FirstName;
        profile.LastName  = model.LastName  ?? profile.LastName;
        profile.Phone     = model.Phone     ?? profile.Phone;
        profile.Mobile    = model.Mobile    ?? profile.Mobile;
        profile.PhoneExt  = model.PhoneExt  ?? profile.PhoneExt;

        await _context.SaveChangesAsync();

        return Ok(new
        {
            userId    = CurrentUserId,
            firstName = profile.FirstName,
            lastName  = profile.LastName,
            phone     = profile.Phone,
            updated   = true,
        });
    }

    // PUT /api/profile/password  — cambiar contraseña
    [HttpPut("password")]
    [SwaggerOperation(Summary = "Cambiar contraseña del usuario autenticado")]
    [SwaggerResponse(200, "Contraseña cambiada")]
    [SwaggerResponse(400, "Contraseña actual incorrecta")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto model)
    {
        var user = await _userManager.FindByIdAsync(CurrentUserId);
        if (user == null) return NotFound(new { message = "Usuario no encontrado." });

        if (string.IsNullOrWhiteSpace(model.CurrentPassword) || string.IsNullOrWhiteSpace(model.NewPassword))
            return BadRequest(new { message = "Contraseña actual y nueva son requeridas." });

        if (model.NewPassword.Length < 6)
            return BadRequest(new { message = "La nueva contraseña debe tener al menos 6 caracteres." });

        var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return BadRequest(new { message = "No se pudo cambiar la contraseña.", detail = errors });
        }

        return Ok(new { message = "Contraseña cambiada exitosamente." });
    }

    // GET /api/profile/team  — equipo de la cuenta del usuario
    [HttpGet("team")]
    [SwaggerOperation(Summary = "Usuarios del equipo de la cuenta del usuario autenticado")]
    [SwaggerResponse(200, "Lista de usuarios del equipo")]
    public async Task<IActionResult> GetTeam()
    {
        // Get the account for the current user
        var accountId = await _context.AccountInternalUsers
            .AsNoTracking()
            .Where(a => a.UserId == CurrentUserId)
            .Select(a => (int?)a.AccountId)
            .FirstOrDefaultAsync();

        if (!accountId.HasValue)
            return Ok(new { team = Array.Empty<object>(), accountId = (int?)null });

        var team = await _context.AccountInternalUsers
            .AsNoTracking()
            .Where(a => a.AccountId == accountId.Value)
            .Select(a => new
            {
                userId      = a.UserId,
                email       = a.User.Email,
                role        = a.RoleInAccount,
                firstName   = a.User.UserProfile != null ? a.User.UserProfile.FirstName : null,
                lastName    = a.User.UserProfile != null ? a.User.UserProfile.LastName  : null,
                phone       = a.User.UserProfile != null ? a.User.UserProfile.Phone     : null,
                isCurrentUser = a.UserId == CurrentUserId,
            })
            .OrderBy(u => u.firstName)
            .ToListAsync();

        return Ok(new { team, accountId });
    }

    // GET /api/profile/funnel  — embudo de la cuenta del usuario
    [HttpGet("funnel")]
    [SwaggerOperation(Summary = "Embudo y etapas de la cuenta del usuario autenticado")]
    [SwaggerResponse(200, "Datos del embudo")]
    public async Task<IActionResult> GetFunnel()
    {
        var accountId = await _context.AccountInternalUsers
            .AsNoTracking()
            .Where(a => a.UserId == CurrentUserId)
            .Select(a => (int?)a.AccountId)
            .FirstOrDefaultAsync();

        if (!accountId.HasValue)
            return Ok(new { hasFunnel = false });

        var funnel = await _context.Funnels
            .AsNoTracking()
            .Where(f => f.AccountId == accountId.Value)
            .Select(f => new { f.FunnelId, f.Name })
            .FirstOrDefaultAsync();

        if (funnel == null)
            return Ok(new { hasFunnel = false });

        var stages = await _context.Stages
            .AsNoTracking()
            .Where(s => s.FunnelId == funnel.FunnelId)
            .OrderBy(s => s.Order)
            .Select(s => new { s.StageId, s.Name, s.Order, s.Color })
            .ToListAsync();

        return Ok(new { hasFunnel = true, funnel, stages });
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

public class UpdateProfileDto
{
    public string? FirstName { get; set; }
    public string? LastName  { get; set; }
    public string? Phone     { get; set; }
    public string? Mobile    { get; set; }
    public string? PhoneExt  { get; set; }
}

public class ChangePasswordDto
{
    public string? CurrentPassword { get; set; }
    public string? NewPassword     { get; set; }
}
