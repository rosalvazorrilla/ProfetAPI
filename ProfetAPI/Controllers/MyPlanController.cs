using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ProfetAPI.Data;
using ProfetAPI.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace ProfetAPI.Controllers;

/// <summary>El cliente ve su plan, sus límites y cuánto lleva usado.</summary>
[Route("api/my-plan")]
[ApiController]
[Authorize]
[SwaggerTag("Mi plan y límites")]
public class MyPlanController(
    ApplicationDbContext db, IPlanLimitsService planLimits, IEmailService email,
    INotificationService notify, IMemoryCache cache) : ControllerBase
{
    private string? UserId => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    private bool IsAdmin   => User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value == "AdminGlobal";
    private string? Role   => User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

    /// <summary>El Admin del cliente (y ManagerAdmin) puede ver a todos y pedir cambios de plan.</summary>
    private bool IsCustomerAdmin => Role is "Admin" or "ManagerAdmin";

    private async Task<int> ResolveCustomerId()
    {
        return await db.Users.Where(u => u.Id == UserId).Select(u => u.CustomerId).FirstOrDefaultAsync()
            ?? await db.AccountInternalUsers.Where(u => u.UserId == UserId)
                 .Select(u => (int?)u.Account.CustomerId).FirstOrDefaultAsync() ?? 0;
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Plan contratado, límites y consumo del cliente")]
    public async Task<IActionResult> Get([FromQuery] int? customerId)
    {
        int custId;
        if (IsAdmin)
        {
            if (customerId == null) return BadRequest(new { message = "AdminGlobal debe indicar customerId." });
            custId = customerId.Value;
        }
        else
        {
            custId = await db.Users.Where(u => u.Id == UserId).Select(u => u.CustomerId).FirstOrDefaultAsync()
                  ?? await db.AccountInternalUsers.Where(u => u.UserId == UserId)
                       .Select(u => (int?)u.Account.CustomerId).FirstOrDefaultAsync() ?? 0;
            if (custId == 0) return NotFound(new { message = "Sin cliente asignado." });
        }

        var usage = await planLimits.GetUsageAsync(custId);
        var cap = await db.Customers.AsNoTracking().Where(c => c.Id == custId)
            .Select(c => c.AiQuantMonthlyCapUsd).FirstOrDefaultAsync();
        return Ok(new
        {
            planName = usage.PlanName,
            monthlyPrice = usage.MonthlyPrice,
            features = usage.Features.Select(f => new
            {
                code = f.Code, name = f.Name, included = f.Included, viaAddOn = f.ViaAddOn,
                limit = f.Limit, unlimited = f.Included && f.LimitText != null && f.Limit == null, used = f.Used,
            }),
            aiQuantMonthlyCapUsd = cap,
        });
    }

    // ── GET api/my-plan/users ─ usuarios que el rol del solicitante puede ver ──────────────────────
    [HttpGet("users")]
    [SwaggerOperation(Summary = "Usuarios visibles según el rol: Admin ve todos; un líder, su equipo; el resto, solo a sí mismo")]
    public async Task<IActionResult> Users()
    {
        if (IsAdmin) return BadRequest(new { message = "Esta vista es para usuarios de un cliente." });
        var custId = await ResolveCustomerId();
        if (custId == 0) return NotFound(new { message = "Sin cliente asignado." });

        string scope;
        List<string>? visibleIds = null;
        if (IsCustomerAdmin) scope = "all";
        else
        {
            var myTeamIds = await db.Teams.AsNoTracking().Where(t => t.LeaderId == UserId).Select(t => t.Id).ToListAsync();
            if (myTeamIds.Count > 0)
            {
                scope = "team";
                visibleIds = await db.UserTeams.AsNoTracking().Where(ut => myTeamIds.Contains(ut.TeamId))
                    .Select(ut => ut.UserId).Distinct().ToListAsync();
                visibleIds.Add(UserId!);
            }
            else { scope = "self"; visibleIds = new List<string> { UserId! }; }
        }

        var q = db.Users.AsNoTracking().Where(u => u.CustomerId == custId && u.Deleted == false);
        if (visibleIds != null) q = q.Where(u => visibleIds.Contains(u.Id));

        var users = await q.Select(u => new
        {
            userId = u.Id, email = u.Email,
            firstName = u.UserProfile != null ? u.UserProfile.FirstName : null,
            lastName  = u.UserProfile != null ? u.UserProfile.LastName : null,
            active = u.Active,
        }).OrderBy(u => u.firstName).Take(500).ToListAsync();

        var ids = users.Select(u => u.userId).ToList();
        var roleRows = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id
                              where ids.Contains(ur.UserId) select new { ur.UserId, r.Name }).ToListAsync();
        var roleByUser = roleRows.GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => g.First().Name);

        var userLimit = await planLimits.GetLimitAsync(custId, "MAX_USERS");
        return Ok(new
        {
            scope,
            // El total y el tope del plan solo los ve quien administra al cliente.
            totalUsers = scope == "all" ? users.Count(u => u.active != false) : (int?)null,
            usersUnlimited = userLimit == null || userLimit.Unlimited,
            usersLimit = userLimit?.Limit,
            users = users.Select(u => new
            {
                u.userId, u.email, u.firstName, u.lastName, active = u.active != false,
                role = roleByUser.TryGetValue(u.userId, out var rn) ? rn : null,
                isCurrentUser = u.userId == UserId,
            }),
        });
    }

    public record PlanRequestDto(string Topic, string? Message);

    // ── POST api/my-plan/request ─ "quiero más usuarios / cuentas / consultas / otro plan" ─────────
    [HttpPost("request")]
    [SwaggerOperation(Summary = "El Admin del cliente solicita a Profet más usuarios, cuentas, consultas o un cambio de plan")]
    public async Task<IActionResult> RequestMore([FromBody] PlanRequestDto dto)
    {
        if (IsAdmin) return BadRequest(new { message = "Solo para usuarios de un cliente." });
        if (!IsCustomerAdmin) return Forbid();
        var custId = await ResolveCustomerId();
        if (custId == 0) return NotFound(new { message = "Sin cliente asignado." });

        var topics = new Dictionary<string, string>
        {
            ["users"] = "Más usuarios", ["accounts"] = "Más cuentas", ["ai"] = "Más consultas del Analista AI",
            ["plan"] = "Cambio de plan", ["other"] = "Otra solicitud",
        };
        if (!topics.TryGetValue(dto.Topic ?? "", out var topicLabel)) return BadRequest(new { message = "Tema inválido." });

        var key = $"planreq:{custId}";
        if (cache.TryGetValue(key, out _))
            return StatusCode(429, new { message = "Ya enviaste una solicitud hace unos minutos. Profet te contactará pronto." });

        var customer = await db.Customers.AsNoTracking().Where(c => c.Id == custId).Select(c => c.Name).FirstOrDefaultAsync() ?? $"Cliente {custId}";
        var me = await db.Users.AsNoTracking().Where(u => u.Id == UserId)
            .Select(u => new { u.Email, Name = u.UserProfile != null ? u.UserProfile.FirstName + " " + u.UserProfile.LastName : null }).FirstOrDefaultAsync();
        var planName = (await planLimits.GetUsageAsync(custId)).PlanName ?? "sin plan";
        var text = (dto.Message ?? "").Trim();
        if (text.Length > 1000) text = text[..1000];

        var html = $"<p><b>{WebUtility.HtmlEncode(customer)}</b> (plan {WebUtility.HtmlEncode(planName)}) solicita: <b>{WebUtility.HtmlEncode(topicLabel)}</b>.</p>"
                 + $"<p>De: {WebUtility.HtmlEncode(me?.Name?.Trim() ?? "")} &lt;{WebUtility.HtmlEncode(me?.Email ?? "")}&gt;</p>"
                 + (text.Length > 0 ? $"<p>Mensaje: {WebUtility.HtmlEncode(text)}</p>" : "");
        var (sent, error) = await email.SendAsync(email.GlobalConfig.FromAddress, $"[Profet] {customer}: {topicLabel}", html, replyTo: me?.Email);

        // Aviso dentro del sistema a los Admin Global, por si el correo no llega.
        var adminIds = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id
                              where r.Name == "AdminGlobal" select ur.UserId).Distinct().ToListAsync();
        foreach (var aid in adminIds)
            await notify.NotifyAsync(aid, $"{customer} solicita: {topicLabel}", url: $"/clientes?id={custId}");

        cache.Set(key, true, TimeSpan.FromMinutes(10));
        return Ok(new { sent = true, emailed = sent, notifiedAdmins = adminIds.Count });
    }
}
