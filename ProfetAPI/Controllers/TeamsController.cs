using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Dtos;
using ProfetAPI.Models;
using Swashbuckle.AspNetCore.Annotations;
using System.Security.Claims;

namespace ProfetAPI.Controllers;

/// <summary>
/// Gestión de Equipos (Teams) y su líder — reemplazo moderno de la jerarquía
/// vieja Manager/ManagerAdmin. A diferencia de SetupController (que solo crea
/// equipos una vez durante el alta con SetupToken), este controlador permite
/// gestionarlos en el uso normal del día a día (agregar/quitar miembros,
/// cambiar líder, crear/eliminar equipos).
/// </summary>
[Route("api/teams")]
[ApiController]
[Authorize]
[SwaggerTag("Equipos y líder de equipo")]
public class TeamsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    public TeamsController(ApplicationDbContext context) => _context = context;

    private string? CurrentUserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    private bool    IsAdminGlobal => User.FindFirst(ClaimTypes.Role)?.Value == "AdminGlobal";

    private async Task<int?> ResolveCustomerId(int? accountId)
    {
        int? resolvedAccountId;
        if (accountId.HasValue)
        {
            if (!IsAdminGlobal)
            {
                var belongs = await _context.AccountInternalUsers
                    .AnyAsync(a => a.AccountId == accountId && a.UserId == CurrentUserId);
                if (!belongs) return null;
            }
            resolvedAccountId = accountId;
        }
        else
        {
            if (IsAdminGlobal) return null;
            resolvedAccountId = await _context.AccountInternalUsers
                .Where(a => a.UserId == CurrentUserId)
                .Select(a => (int?)a.AccountId).FirstOrDefaultAsync();
        }
        if (resolvedAccountId == null) return null;

        return await _context.Accounts.AsNoTracking()
            .Where(a => a.AccountId == resolvedAccountId)
            .Select(a => (int?)a.CustomerId)
            .FirstOrDefaultAsync();
    }

    private async Task<bool> UserBelongsToCustomer(string userId, int customerId)
    {
        return await _context.AccountInternalUsers
            .AnyAsync(aiu => aiu.UserId == userId && aiu.Account.CustomerId == customerId);
    }

    private static SetupTeamResponseDto ToDto(Team t) => new()
    {
        TeamId = t.Id,
        Name = t.Name,
        LeaderId = t.LeaderId,
        LeaderName = t.Leader != null
            ? $"{t.Leader.UserProfile?.FirstName} {t.Leader.UserProfile?.LastName}".Trim()
            : null,
        Members = t.UserTeams.Select(ut => new SetupTeamMemberDto
        {
            UserId = ut.UserId,
            FullName = $"{ut.User.UserProfile?.FirstName} {ut.User.UserProfile?.LastName}".Trim(),
            Email = ut.User.Email ?? "",
        }).ToList(),
    };

    // GET /api/teams?accountId=
    [HttpGet]
    [SwaggerOperation(Summary = "Listar equipos del cliente, con líder y miembros. Si no eres AdminGlobal, solo ves el/los equipo(s) que lideras.")]
    public async Task<IActionResult> GetTeams([FromQuery] int? accountId)
    {
        var customerId = await ResolveCustomerId(accountId);
        if (customerId == null) return NotFound(new { message = "Sin cuenta." });

        var query = _context.Teams
            .Include(t => t.Leader).ThenInclude(l => l!.UserProfile)
            .Include(t => t.UserTeams).ThenInclude(ut => ut.User).ThenInclude(u => u.UserProfile)
            .Where(t => t.CustomerId == customerId);

        if (!IsAdminGlobal)
            query = query.Where(t => t.LeaderId == CurrentUserId);

        var teams = await query.OrderBy(t => t.Name).ToListAsync();

        return Ok(teams.Select(ToDto));
    }

    // GET /api/teams/members-available?accountId=  -- usuarios del cliente para agregar
    [HttpGet("members-available")]
    [SwaggerOperation(Summary = "Usuarios del cliente disponibles para asignar a un equipo")]
    public async Task<IActionResult> GetAvailableMembers([FromQuery] int? accountId)
    {
        var customerId = await ResolveCustomerId(accountId);
        if (customerId == null) return NotFound(new { message = "Sin cuenta." });

        var users = await _context.AccountInternalUsers
            .Where(aiu => aiu.Account.CustomerId == customerId)
            .Select(aiu => aiu.User)
            .Distinct()
            .Select(u => new
            {
                userId = u.Id,
                fullName = (u.UserProfile != null ? (u.UserProfile.FirstName + " " + u.UserProfile.LastName) : u.UserName) ?? u.Email,
                email = u.Email,
            })
            .ToListAsync();

        return Ok(users);
    }

    // POST /api/teams?accountId=
    [HttpPost]
    [SwaggerOperation(Summary = "Crear equipo")]
    public async Task<IActionResult> CreateTeam([FromQuery] int? accountId, [FromBody] CreateSetupTeamDto model)
    {
        if (!IsAdminGlobal) return Forbid();
        var customerId = await ResolveCustomerId(accountId);
        if (customerId == null) return NotFound(new { message = "Sin cuenta." });
        if (string.IsNullOrWhiteSpace(model.Name)) return BadRequest(new { message = "El nombre es obligatorio." });

        foreach (var uid in model.UserIds)
            if (!await UserBelongsToCustomer(uid, customerId.Value))
                return BadRequest(new { message = $"El usuario {uid} no pertenece a este cliente." });
        if (model.LeaderId != null && !await UserBelongsToCustomer(model.LeaderId, customerId.Value))
            return BadRequest(new { message = "El líder indicado no pertenece a este cliente." });

        var team = new Team { Name = model.Name.Trim(), CustomerId = customerId, LeaderId = model.LeaderId };
        _context.Teams.Add(team);
        await _context.SaveChangesAsync();

        foreach (var uid in model.UserIds.Distinct())
            _context.UserTeams.Add(new UserTeam { TeamId = team.Id, UserId = uid });
        await _context.SaveChangesAsync();

        var result = await _context.Teams
            .Include(t => t.Leader).ThenInclude(l => l!.UserProfile)
            .Include(t => t.UserTeams).ThenInclude(ut => ut.User).ThenInclude(u => u.UserProfile)
            .FirstAsync(t => t.Id == team.Id);

        return Ok(ToDto(result));
    }

    // PUT /api/teams/{id}?accountId=
    [HttpPut("{id:int}")]
    [SwaggerOperation(Summary = "Editar equipo (nombre, líder, miembros)")]
    public async Task<IActionResult> UpdateTeam(int id, [FromQuery] int? accountId, [FromBody] UpdateSetupTeamDto model)
    {
        var customerId = await ResolveCustomerId(accountId);
        if (customerId == null) return NotFound(new { message = "Sin cuenta." });

        var team = await _context.Teams.Include(t => t.UserTeams)
            .FirstOrDefaultAsync(t => t.Id == id && t.CustomerId == customerId);
        if (team == null) return NotFound(new { message = "Equipo no encontrado." });

        // Un líder de equipo (no AdminGlobal) solo puede gestionar los miembros de SU propio equipo,
        // no renombrarlo ni reasignar el liderazgo.
        if (!IsAdminGlobal && team.LeaderId != CurrentUserId) return Forbid();

        foreach (var uid in model.UserIds)
            if (!await UserBelongsToCustomer(uid, customerId.Value))
                return BadRequest(new { message = $"El usuario {uid} no pertenece a este cliente." });

        if (IsAdminGlobal)
        {
            if (model.LeaderId != null && !await UserBelongsToCustomer(model.LeaderId, customerId.Value))
                return BadRequest(new { message = "El líder indicado no pertenece a este cliente." });
            team.Name = model.Name.Trim();
            team.LeaderId = model.LeaderId;
        }

        _context.UserTeams.RemoveRange(team.UserTeams);
        foreach (var uid in model.UserIds.Distinct())
            _context.UserTeams.Add(new UserTeam { TeamId = team.Id, UserId = uid });
        await _context.SaveChangesAsync();

        var result = await _context.Teams
            .Include(t => t.Leader).ThenInclude(l => l!.UserProfile)
            .Include(t => t.UserTeams).ThenInclude(ut => ut.User).ThenInclude(u => u.UserProfile)
            .FirstAsync(t => t.Id == team.Id);

        return Ok(ToDto(result));
    }

    // DELETE /api/teams/{id}?accountId=
    [HttpDelete("{id:int}")]
    [SwaggerOperation(Summary = "Eliminar equipo")]
    public async Task<IActionResult> DeleteTeam(int id, [FromQuery] int? accountId)
    {
        if (!IsAdminGlobal) return Forbid();
        var customerId = await ResolveCustomerId(accountId);
        if (customerId == null) return NotFound(new { message = "Sin cuenta." });

        var team = await _context.Teams.Include(t => t.UserTeams)
            .FirstOrDefaultAsync(t => t.Id == id && t.CustomerId == customerId);
        if (team == null) return NotFound(new { message = "Equipo no encontrado." });

        _context.UserTeams.RemoveRange(team.UserTeams);
        _context.Teams.Remove(team);
        await _context.SaveChangesAsync();

        return Ok(new { deleted = true });
    }
}
