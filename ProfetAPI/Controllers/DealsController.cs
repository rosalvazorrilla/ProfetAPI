using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Models;
using Swashbuckle.AspNetCore.Annotations;

namespace ProfetAPI.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
[SwaggerTag("CRM — Oportunidades (Deals)")]
public class DealsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ProfetAPI.Services.AutomationExecutorService _automations;

    public DealsController(ApplicationDbContext context, ProfetAPI.Services.AutomationExecutorService automations)
    {
        _context     = context;
        _automations = automations;
    }

    private string? CurrentUserId => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    private string? CurrentUserRole => User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
    private bool IsAdminGlobal => CurrentUserRole == "AdminGlobal";

    // GET /api/deals?accountId=1&search=&dateFrom=&dateTo=&tagId=&ownerId=&dealsPerStage=20
    [HttpGet]
    [SwaggerOperation(Summary = "Listar deals del kanban", Description = "Devuelve deals agrupados por etapa. Usa dealsPerStage para paginar cada columna.")]
    [SwaggerResponse(200, "Kanban data")]
    public async Task<IActionResult> GetKanban(
        [FromQuery] int? accountId,
        [FromQuery] string? search,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] int? tagId,
        [FromQuery] string? ownerId,
        [FromQuery] string? status = "Abierto",
        [FromQuery] int dealsPerStage = 20)
    {
        // Validar acceso a la cuenta
        int resolvedAccountId;
        if (accountId.HasValue)
        {
            if (!IsAdminGlobal)
            {
                var user = await _context.Users.FindAsync(CurrentUserId);
                var belongs = await _context.AccountInternalUsers
                    .AnyAsync(a => a.AccountId == accountId && a.UserId == CurrentUserId);
                if (!belongs) return Forbid();
            }
            resolvedAccountId = accountId.Value;
        }
        else
        {
            if (IsAdminGlobal) return BadRequest(new { message = "AdminGlobal debe especificar accountId." });
            var assignment = await _context.AccountInternalUsers
                .Where(a => a.UserId == CurrentUserId)
                .FirstOrDefaultAsync();
            if (assignment == null) return NotFound(new { message = "Sin cuenta asignada." });
            resolvedAccountId = assignment.AccountId;
        }

        // ── Funnel + stages (sin Include de deals) ───────────────────────────────
        var funnel = await _context.Funnels
            .AsNoTracking()
            .Where(f => f.AccountId == resolvedAccountId)
            .Select(f => new { f.FunnelId, f.Name })
            .FirstOrDefaultAsync();

        if (funnel == null)
            return Ok(new { stages = Array.Empty<object>(), totalAmount = 0 });

        var stages = await _context.Stages
            .AsNoTracking()
            .Where(s => s.FunnelId == funnel.FunnelId)
            .OrderBy(s => s.Order)
            .Select(s => new { s.StageId, s.Name, s.Order, s.Color })
            .ToListAsync();

        // ── Base filter (sin Include, sin tracking) ───────────────────────────
        // Usamos IQueryable sobre Deals limpio; las navegaciones se resuelven en .Select()
        var baseQ = _context.Deals
            .AsNoTracking()
            .Where(d => d.AccountId == resolvedAccountId);

        if (!string.IsNullOrWhiteSpace(status))
            baseQ = baseQ.Where(d => d.Status == status);
        if (!string.IsNullOrWhiteSpace(ownerId))
            baseQ = baseQ.Where(d => d.DealUsers.Any(du => du.UserId == ownerId));
        if (!string.IsNullOrWhiteSpace(search))
            baseQ = baseQ.Where(d =>
                d.DealName.Contains(search) ||
                (d.Company != null && d.Company.Name.Contains(search)) ||
                (d.PrimaryContact != null && (d.PrimaryContact.FirstName + " " + d.PrimaryContact.LastName).Contains(search)));
        if (dateFrom.HasValue) baseQ = baseQ.Where(d => d.CreatedOn >= dateFrom.Value);
        if (dateTo.HasValue)   baseQ = baseQ.Where(d => d.CreatedOn <= dateTo.Value.AddDays(1));

        // ── 1. Conteos + montos por stage (1 sola query GROUP BY) ────────────
        var stageStats = await baseQ
            .GroupBy(d => d.StageId)
            .Select(g => new
            {
                stageId     = g.Key,
                totalCount  = g.Count(),
                totalAmount = g.Sum(d => (decimal?)(d.QuotedAmount ?? 0)) ?? 0m,
            })
            .ToListAsync();

        var statsMap = stageStats.ToDictionary(x => x.stageId ?? -1);

        // ── 2. Todos los deals del kanban en UNA sola query ──────────────────
        static string Initials(string name) =>
            string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                              .Take(2).Select(p => char.ToUpper(p[0]).ToString()));

        var stageIds = stages.Select(s => s.StageId).ToList();

        var allDeals = await baseQ
            .Where(d => d.StageId != null && stageIds.Contains(d.StageId.Value))
            .OrderByDescending(d => d.CreatedOn)
            .Select(d => new
            {
                dealId       = d.DealId,
                dealName     = d.DealName,
                quotedAmount = d.QuotedAmount,
                status       = d.Status,
                createdOn    = d.CreatedOn,
                closeDate    = d.CloseDate,
                stageId      = d.StageId,
                company      = d.Company != null ? d.Company.Name : null,
                contact      = d.PrimaryContact != null
                    ? (d.PrimaryContact.FirstName + " " + d.PrimaryContact.LastName).Trim()
                    : null,
                ownerRaw = d.DealUsers
                    .Where(du => du.RoleInDeal == "Owner" || du.RoleInDeal == null)
                    .Select(du => du.User.UserProfile != null
                        ? (du.User.UserProfile.FirstName + " " + du.User.UserProfile.LastName).Trim()
                        : du.User.UserName ?? "")
                    .FirstOrDefault() ?? "",
            })
            .ToListAsync();

        // Agrupar en memoria — evita N+1 queries al backend
        var dealsByStage = allDeals
            .GroupBy(d => d.stageId ?? -1)
            .ToDictionary(g => g.Key, g => g.Take(dealsPerStage).ToList());

        var kanbanStages = stages.Select(s =>
        {
            statsMap.TryGetValue(s.StageId, out var stat);
            dealsByStage.TryGetValue(s.StageId, out var pageDeals);
            pageDeals ??= [];

            return (object)new
            {
                stageId     = s.StageId,
                name        = s.Name,
                order       = s.Order,
                color       = s.Color ?? "#6366f1",
                totalCount  = stat?.totalCount  ?? 0,
                dealCount   = stat?.totalCount  ?? 0,
                totalAmount = stat?.totalAmount ?? 0m,
                deals = pageDeals.Select(d => new
                {
                    d.dealId,
                    d.dealName,
                    d.company,
                    d.contact,
                    d.quotedAmount,
                    d.status,
                    d.createdOn,
                    d.closeDate,
                    d.stageId,
                    ownerName     = d.ownerRaw,
                    ownerInitials = d.ownerRaw.Length > 0 ? Initials(d.ownerRaw) : "?",
                    tags          = Array.Empty<object>(),
                }).ToList(),
            };
        }).ToList();

        var totals = stageStats.Aggregate(
            (count: 0, amount: 0m),
            (acc, x) => (acc.count + x.totalCount, acc.amount + x.totalAmount));

        return Ok(new
        {
            funnelId   = funnel.FunnelId,
            funnelName = funnel.Name,
            stages     = kanbanStages,
            totalDeals  = totals.count,
            totalAmount = totals.amount,
        });
    }

    // GET /api/deals/stage/{stageId}?accountId=&page=2&pageSize=20&...filters
    [HttpGet("stage/{stageId}")]
    [SwaggerOperation(Summary = "Cargar más deals de una etapa (paginación por columna)")]
    [SwaggerResponse(200, "Página de deals de la etapa")]
    public async Task<IActionResult> GetStageDeals(
        int stageId,
        [FromQuery] int? accountId,
        [FromQuery] string? search,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] string? ownerId,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        // Resolver account
        int resolvedAccountId;
        if (accountId.HasValue)
        {
            if (!IsAdminGlobal)
            {
                var belongs = await _context.AccountInternalUsers
                    .AnyAsync(a => a.AccountId == accountId && a.UserId == CurrentUserId);
                if (!belongs) return Forbid();
            }
            resolvedAccountId = accountId.Value;
        }
        else
        {
            if (IsAdminGlobal) return BadRequest(new { message = "AdminGlobal debe especificar accountId." });
            var assignment = await _context.AccountInternalUsers
                .Where(a => a.UserId == CurrentUserId)
                .FirstOrDefaultAsync();
            if (assignment == null) return NotFound(new { message = "Sin cuenta asignada." });
            resolvedAccountId = assignment.AccountId;
        }

        var query = _context.Deals
            .Include(d => d.PrimaryContact)
            .Include(d => d.Company)
            .Include(d => d.DealUsers).ThenInclude(du => du.User).ThenInclude(u => u.UserProfile)
            .Where(d => d.AccountId == resolvedAccountId && d.StageId == stageId);

        if (!string.IsNullOrWhiteSpace(status))   query = query.Where(d => d.Status == status);
        if (!string.IsNullOrWhiteSpace(ownerId))  query = query.Where(d => d.DealUsers.Any(du => du.UserId == ownerId));
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(d => d.DealName.Contains(search) ||
                (d.Company != null && d.Company.Name.Contains(search)) ||
                (d.PrimaryContact != null && (d.PrimaryContact.FirstName + " " + d.PrimaryContact.LastName).Contains(search)));
        if (dateFrom.HasValue) query = query.Where(d => d.CreatedOn >= dateFrom.Value);
        if (dateTo.HasValue)   query = query.Where(d => d.CreatedOn <= dateTo.Value.AddDays(1));

        var totalCount = await query.CountAsync();

        var deals = await query
            .OrderByDescending(d => d.CreatedOn)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var tagsByDeal = new Dictionary<int, List<object>>();

        static object ProjectDealStage(Deal d, Dictionary<int, List<object>> tagsByDeal)
        {
            var owner = d.DealUsers.FirstOrDefault(du => du.RoleInDeal == "Owner") ?? d.DealUsers.FirstOrDefault();
            var ownerName = owner?.User?.UserProfile != null
                ? $"{owner.User.UserProfile.FirstName} {owner.User.UserProfile.LastName}".Trim()
                : owner?.User?.UserName ?? "";
            var initials = ownerName.Length > 0
                ? string.Concat(ownerName.Split(' ').Where(p => p.Length > 0).Take(2).Select(p => p[0].ToString().ToUpper()))
                : "?";
            return new
            {
                dealId = d.DealId,
                dealName = d.DealName,
                company = d.Company?.Name,
                contact = d.PrimaryContact != null
                    ? $"{d.PrimaryContact.FirstName} {d.PrimaryContact.LastName}".Trim()
                    : null,
                quotedAmount = d.QuotedAmount,
                status = d.Status,
                createdOn = d.CreatedOn,
                closeDate = d.CloseDate,
                stageId = d.StageId,
                ownerName,
                ownerInitials = initials,
                tags = tagsByDeal.TryGetValue(d.DealId, out var t) ? t : new List<object>(),
            };
        }

        return Ok(new
        {
            stageId,
            page,
            pageSize,
            totalCount,
            deals = deals.Select(d => ProjectDealStage(d, tagsByDeal)).ToList(),
        });
    }

    // GET /api/deals/{id}  — detalle completo de un deal
    [HttpGet("{id:int}")]
    [SwaggerOperation(Summary = "Detalle completo de un deal")]
    [SwaggerResponse(200, "Deal encontrado")]
    [SwaggerResponse(404, "No encontrado")]
    public async Task<IActionResult> GetDeal(int id)
    {
        var deal = await _context.Deals
            .AsNoTracking()
            .Where(d => d.DealId == id)
            .Select(d => new
            {
                d.DealId, d.DealName, d.Status, d.DealType,
                d.QuotedAmount, d.FinalAmount, d.CreatedOn, d.CloseDate,
                d.ProspectSource, d.AdName, d.OriginType,
                d.StageId, d.AccountId, d.CompanyId, d.PrimaryContactId,
                stageName    = d.Stage != null ? d.Stage.Name  : null,
                stageOrder   = d.Stage != null ? (int?)d.Stage.Order : null,
                stageColor   = d.Stage != null ? d.Stage.Color : null,
                funnelId     = d.Stage != null ? (int?)d.Stage.FunnelId : null,
                companyName  = d.Company != null ? d.Company.Name : null,
                contactFirst = d.PrimaryContact != null ? d.PrimaryContact.FirstName : null,
                contactLast  = d.PrimaryContact != null ? d.PrimaryContact.LastName  : null,
                contactEmail = d.PrimaryContact != null ? d.PrimaryContact.Email       : null,
                contactPhone = d.PrimaryContact != null ? d.PrimaryContact.PhoneNumber : null,
            })
            .FirstOrDefaultAsync();

        if (deal == null) return NotFound(new { message = "Deal no encontrado." });

        if (!IsAdminGlobal)
        {
            var belongs = await _context.AccountInternalUsers
                .AnyAsync(a => a.AccountId == deal.AccountId && a.UserId == CurrentUserId);
            if (!belongs) return Forbid();
        }

        // Owner — preferir RoleInDeal == "Owner"; si no hay, tomar el primero
        var ownerRow = await _context.DealUsers
            .AsNoTracking()
            .Where(du => du.DealId == id && du.RoleInDeal == "Owner")
            .Select(du => new
            {
                du.UserId,
                name = du.User.UserProfile != null
                    ? (du.User.UserProfile.FirstName + " " + du.User.UserProfile.LastName).Trim()
                    : du.User.UserName ?? "",
            })
            .FirstOrDefaultAsync()
            ?? await _context.DealUsers
                .AsNoTracking()
                .Where(du => du.DealId == id)
                .Select(du => new
                {
                    du.UserId,
                    name = du.User.UserProfile != null
                        ? (du.User.UserProfile.FirstName + " " + du.User.UserProfile.LastName).Trim()
                        : du.User.UserName ?? "",
                })
                .FirstOrDefaultAsync();

        var ownerName     = ownerRow?.name ?? "";
        var ownerInitials = ownerName.Length > 0
            ? string.Concat(ownerName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                     .Take(2).Select(p => p[0].ToString().ToUpper()))
            : "?";

        // Etapas del funnel — lista concreta para serialización correcta
        var stageRows = deal.funnelId.HasValue
            ? await _context.Stages
                .AsNoTracking()
                .Where(s => s.FunnelId == deal.funnelId.Value)
                .OrderBy(s => s.Order)
                .Select(s => new { s.StageId, s.Name, s.Order, s.Color })
                .ToListAsync()
            : new List<object>()
                .Select(_ => new { StageId = 0, Name = "", Order = 0, Color = (string?)null })
                .ToList();

        return Ok(new
        {
            dealId         = deal.DealId,
            dealName       = deal.DealName,
            status         = deal.Status,
            dealType       = deal.DealType,
            quotedAmount   = deal.QuotedAmount,
            finalAmount    = deal.FinalAmount,
            createdOn      = deal.CreatedOn,
            closeDate      = deal.CloseDate,
            prospectSource = deal.ProspectSource,
            adName         = deal.AdName,
            originType     = deal.OriginType,
            stageId        = deal.StageId,
            stageName      = deal.stageName,
            company = deal.CompanyId.HasValue
                ? new { id = deal.CompanyId, name = deal.companyName }
                : null,
            contact = deal.PrimaryContactId.HasValue
                ? new
                {
                    id    = deal.PrimaryContactId,
                    name  = ((deal.contactFirst ?? "") + " " + (deal.contactLast ?? "")).Trim(),
                    email = deal.contactEmail,
                    phone = deal.contactPhone,
                }
                : null,
            owner  = new { id = ownerRow?.UserId, name = ownerName, initials = ownerInitials },
            stages = stageRows,
        });
    }

    // PATCH /api/deals/{id}/stage  — mover deal a otra etapa (drag & drop)
    [HttpPatch("{id}/stage")]
    [SwaggerOperation(Summary = "Mover deal a otra etapa")]
    [SwaggerResponse(200, "Stage actualizado")]
    [SwaggerResponse(404, "Deal no encontrado")]
    public async Task<IActionResult> MoveStage(int id, [FromBody] MoveStageDto model)
    {
        var deal = await _context.Deals.FindAsync(id);
        if (deal == null) return NotFound(new { message = "Deal no encontrado." });

        if (!IsAdminGlobal)
        {
            var belongs = await _context.AccountInternalUsers
                .AnyAsync(a => a.AccountId == deal.AccountId && a.UserId == CurrentUserId);
            if (!belongs) return Forbid();
        }

        deal.StageId = model.StageId;
        await _context.SaveChangesAsync();

        // ── Disparar automatizaciones de oportunidad ──────────────────────────
        var stageName = model.StageId.HasValue
            ? await _context.Stages.Where(s => s.StageId == model.StageId.Value)
                                   .Select(s => s.Name).FirstOrDefaultAsync() ?? ""
            : "";

        var fields = new Dictionary<string, string>
        {
            ["_dealId"]   = deal.DealId.ToString(),
            ["dealName"]  = deal.DealName ?? "",
            ["stageId"]   = deal.StageId?.ToString() ?? "",
            ["stageName"] = stageName,
            ["amount"]    = deal.QuotedAmount?.ToString() ?? "",
            ["status"]    = deal.Status,
        };

        var accId = deal.AccountId;
        // StageChanged siempre; DealWon/DealLost si la etapa destino lo indica por su nombre
        _ = Task.Run(async () =>
        {
            await _automations.FireAsync(accId, "StageChanged", new Dictionary<string, string>(fields));
            var kind = ClassifyStage(stageName);
            if (kind == "won")  await _automations.FireAsync(accId, "DealWon",  new Dictionary<string, string>(fields));
            if (kind == "lost") await _automations.FireAsync(accId, "DealLost", new Dictionary<string, string>(fields));
        });

        return Ok(new { dealId = deal.DealId, stageId = deal.StageId });
    }

    /// <summary>Clasifica una etapa como "won"/"lost"/"" según palabras clave en su nombre.</summary>
    private static string ClassifyStage(string name)
    {
        var n = name.ToLowerInvariant()
            .Replace("á", "a").Replace("é", "e").Replace("í", "i").Replace("ó", "o").Replace("ú", "u");
        if (n.Contains("ganad") || n.Contains("won") || n.Contains("cerrada ganada") || n.Contains("exito")) return "won";
        if (n.Contains("perdid") || n.Contains("lost") || n.Contains("cerrada perdida") || n.Contains("descartad")) return "lost";
        return "";
    }

    // GET /api/deals/accounts?customerId=&activeOnly=true
    [HttpGet("accounts")]
    [SwaggerOperation(Summary = "Cuentas accesibles (para selector del kanban)")]
    public async Task<IActionResult> GetAccessibleAccounts(
        [FromQuery] int? customerId,
        [FromQuery] bool activeOnly = true)
    {
        if (IsAdminGlobal)
        {
            var q = _context.Accounts.AsNoTracking().AsQueryable();
            if (customerId.HasValue) q = q.Where(a => a.CustomerId == customerId.Value);
            if (activeOnly) q = q.Where(a => a.Status != "Inactivo" && a.Status != "Eliminado");
            var list = await q
                .OrderBy(a => a.Name)
                .Select(a => new { a.AccountId, a.Name, a.CustomerId, customerName = a.Customer.Name, a.Status })
                .ToListAsync();
            return Ok(list);
        }
        else
        {
            var q = _context.AccountInternalUsers
                .AsNoTracking()
                .Where(a => a.UserId == CurrentUserId);
            if (activeOnly)
                q = q.Where(a => a.Account.Status != "Inactivo" && a.Account.Status != "Eliminado");
            var list = await q
                .OrderBy(a => a.Account.Name)
                .Select(a => new { a.Account.AccountId, a.Account.Name, a.Account.CustomerId, customerName = "", a.Account.Status })
                .ToListAsync();
            return Ok(list);
        }
    }

    // GET /api/deals/customers?activeOnly=true — solo AdminGlobal
    [HttpGet("customers")]
    [Authorize(Roles = "AdminGlobal")]
    [SwaggerOperation(Summary = "Lista de customers (AdminGlobal)")]
    public async Task<IActionResult> GetCustomers([FromQuery] bool activeOnly = true)
    {
        var q = _context.Customers.AsNoTracking().Where(c => c.Deleted != true);
        if (activeOnly) q = q.Where(c => c.Active == true);
        var list = await q
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name, active = c.Active == true })
            .ToListAsync();
        return Ok(list);
    }

    // GET /api/deals/users?accountId=1  — usuarios de la cuenta para filtro de responsable
    [HttpGet("users")]
    [SwaggerOperation(Summary = "Usuarios de la cuenta para filtro de responsable")]
    public async Task<IActionResult> GetAccountUsers([FromQuery] int? accountId)
    {
        int resolvedAccountId;
        if (accountId.HasValue)
        {
            if (!IsAdminGlobal)
            {
                var belongs = await _context.AccountInternalUsers
                    .AnyAsync(a => a.AccountId == accountId && a.UserId == CurrentUserId);
                if (!belongs) return Forbid();
            }
            resolvedAccountId = accountId.Value;
        }
        else
        {
            if (IsAdminGlobal) return BadRequest(new { message = "AdminGlobal debe especificar accountId." });
            var assignment = await _context.AccountInternalUsers
                .Where(a => a.UserId == CurrentUserId)
                .FirstOrDefaultAsync();
            if (assignment == null) return NotFound();
            resolvedAccountId = assignment.AccountId;
        }

        var userIds = await _context.AccountInternalUsers
            .Where(a => a.AccountId == resolvedAccountId)
            .Select(a => a.UserId)
            .Distinct()
            .ToListAsync();

        var users = await _context.Users
            .Where(u => userIds.Contains(u.Id))
            .Include(u => u.UserProfile)
            .Select(u => new
            {
                userId = u.Id,
                name = (u.UserProfile != null
                    ? (u.UserProfile.FirstName + " " + u.UserProfile.LastName).Trim()
                    : u.UserName) ?? ""
            })
            .OrderBy(u => u.name)
            .ToListAsync();

        return Ok(users);
    }

    // GET /api/deals/tags?accountId=1
    [HttpGet("tags")]
    [SwaggerOperation(Summary = "Tags disponibles para filtrar")]
    public async Task<IActionResult> GetTags([FromQuery] int accountId)
    {
        var account = await _context.Accounts.FindAsync(accountId);
        if (account == null) return NotFound();

        var tags = await _context.Tags
            .Where(t => t.CustomerId == account.CustomerId)
            .Select(t => new { t.TagId, t.Name, t.Color, t.FontColor })
            .ToListAsync();
        return Ok(tags);
    }
}

public class MoveStageDto
{
    public int? StageId { get; set; }
}
