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
    private bool IsAdminGlobal => CurrentUserRole == "AdminGlobal";

    /// <summary>AdminGlobal puede pasar accountId por query (no pertenece a ninguna cuenta). El resto
    /// puede pertenecer a varias cuentas del mismo cliente (PM/Manager) — si pasan un accountId que
    /// SÍ es una de las suyas, se respeta; si no, se usa la primera. Antes esto ignoraba el query
    /// param por completo y siempre devolvía la primera cuenta del usuario.</summary>
    private async Task<int?> ResolveAccountId(int? accountId)
    {
        if (IsAdminGlobal) return accountId;

        var userAccountIds = await _context.AccountInternalUsers
            .AsNoTracking()
            .Where(a => a.UserId == CurrentUserId)
            .Select(a => a.AccountId)
            .ToListAsync();

        if (accountId.HasValue && userAccountIds.Contains(accountId.Value)) return accountId;
        return userAccountIds.Count > 0 ? userAccountIds[0] : (int?)null;
    }

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

        // El helper PreferencesAsObject no es traducible a SQL sobre IQueryable —
        // se trae el JSON crudo y se deserializa en memoria.
        var rawPreferences = await _context.UserProfiles.AsNoTracking()
            .Where(p => p.UserId == CurrentUserId)
            .Select(p => p.Preferences)
            .FirstOrDefaultAsync();
        var hasSeenOnboarding = !string.IsNullOrEmpty(rawPreferences)
            && (System.Text.Json.JsonSerializer.Deserialize<ProfetAPI.Dtos.UserPreferences>(rawPreferences)?.HasSeenOnboarding ?? false);

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

        var effectiveCustomerId = user.CustomerId ?? accountInfo?.customerId;
        var activeFeatureCodes = effectiveCustomerId.HasValue
            ? await GetActiveFeatureCodesAsync(effectiveCustomerId.Value)
            : new List<string>();

        return Ok(new
        {
            userId     = user.Id,
            email      = user.Email,
            userName   = user.UserName,
            customerId = user.CustomerId,
            userType   = user.UserType,
            role       = CurrentUserRole,
            hasSeenOnboarding = hasSeenOnboarding,
            activeFeatureCodes = activeFeatureCodes,
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

    // PATCH /api/profile/onboarding  — marcar visto/no visto el tour de bienvenida
    [HttpPatch("onboarding")]
    [SwaggerOperation(Summary = "Marcar el tour de bienvenida como visto (u omitido)")]
    [SwaggerResponse(200, "Guardado")]
    public async Task<IActionResult> SetOnboardingSeen([FromBody] SetOnboardingDto model)
    {
        var profile = await _context.UserProfiles.FindAsync(CurrentUserId);
        if (profile == null)
        {
            profile = new UserProfile { UserId = CurrentUserId };
            _context.UserProfiles.Add(profile);
        }

        var prefs = profile.PreferencesAsObject;
        prefs.HasSeenOnboarding = model.Seen;
        profile.PreferencesAsObject = prefs;

        await _context.SaveChangesAsync();
        return Ok(new { hasSeenOnboarding = model.Seen });
    }

    /// <summary>Todos los FeatureCode activos para un customer — incluidos en su Plan
    /// o desbloqueados por un AddOn comprado — para que el frontend arme candados
    /// de cualquier función sin pedirle uno por uno al backend.</summary>
    private async Task<List<string>> GetActiveFeatureCodesAsync(int customerId)
    {
        var sub = await _context.Subscriptions
            .Where(s => s.CustomerId == customerId && (s.Status == "Active" || s.Status == "Trialing"))
            .Select(s => new { s.SubscriptionId, s.PlanId })
            .FirstOrDefaultAsync();
        if (sub == null) return new List<string>();

        var planCodes = await _context.PlanFeatures
            .Where(pf => pf.PlanId == sub.PlanId)
            .Select(pf => pf.Feature.FeatureCode)
            .ToListAsync();

        var now = DateTime.UtcNow;
        var addonCodes = await _context.CustomerPurchasedAddOns
            .Where(p => p.SubscriptionId == sub.SubscriptionId && (p.ExpiryDate == null || p.ExpiryDate > now))
            .Select(p => p.AddOn.Feature.FeatureCode)
            .ToListAsync();

        return planCodes.Concat(addonCodes).Distinct().ToList();
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
    [SwaggerOperation(Summary = "Usuarios del equipo de la cuenta del usuario autenticado (AdminGlobal puede pasar accountId)")]
    [SwaggerResponse(200, "Lista de usuarios del equipo")]
    public async Task<IActionResult> GetTeam([FromQuery] int? accountId)
    {
        accountId = await ResolveAccountId(accountId);

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
    [SwaggerOperation(Summary = "Embudo y etapas de la cuenta del usuario autenticado (AdminGlobal puede pasar accountId)")]
    [SwaggerResponse(200, "Datos del embudo")]
    public async Task<IActionResult> GetFunnel([FromQuery] int? accountId)
    {
        accountId = await ResolveAccountId(accountId);

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

public class SetOnboardingDto
{
    public bool Seen { get; set; }
}
