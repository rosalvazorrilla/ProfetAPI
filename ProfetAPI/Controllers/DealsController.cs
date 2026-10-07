using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Models;
using Swashbuckle.AspNetCore.Annotations;

namespace ProfetAPI.Controllers;

/// <summary>Proyección liviana de un deal para una columna del kanban (Top-N por etapa).</summary>
public record StageDealRow(
    int DealId, string DealName, decimal? QuotedAmount, string Status, DateTime CreatedOn,
    DateTime? CloseDate, int? StageId, string? Company, string? Contact, string OwnerRaw);

[Route("api/[controller]")]
[ApiController]
[Authorize]
[ServiceFilter(typeof(ProfetAPI.Services.DealVisibilityFilter))]
[SwaggerTag("CRM — Oportunidades (Deals)")]
public class DealsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ProfetAPI.Services.AutomationExecutorService _automations;
    private readonly ProfetAPI.Services.ITimelineLogger _timeline;
    private readonly ProfetAPI.Services.PlaybookService _playbooks;
    private readonly ProfetAPI.Services.INextActionService _nextAction;

    public DealsController(
        ApplicationDbContext context,
        ProfetAPI.Services.AutomationExecutorService automations,
        ProfetAPI.Services.ITimelineLogger timeline,
        ProfetAPI.Services.PlaybookService playbooks,
        ProfetAPI.Services.INextActionService nextAction)
    {
        _context     = context;
        _automations = automations;
        _timeline    = timeline;
        _playbooks   = playbooks;
        _nextAction  = nextAction;
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

        // Un vendedor solo ve las oportunidades donde participa (o las de su equipo si lo lidera).
        var visScope = await HttpContext.RequestServices.GetRequiredService<ProfetAPI.Services.IVisibilityService>().ForAccountAsync(User, resolvedAccountId);
        if (!visScope.All)
        {
            var visIds = visScope.UserIds;
            baseQ = baseQ.Where(d => d.DealUsers.Any(du => visIds.Contains(du.UserId)));
        }

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

        // ── 2. Solo los primeros `dealsPerStage` deals de CADA etapa (Top-N en SQL) ──
        // Antes se traían TODOS los deals de la cuenta y se recortaban en memoria;
        // con cuentas grandes eso cargaba miles de filas en cada apertura del kanban.
        static string Initials(string name) =>
            string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                              .Take(2).Select(p => char.ToUpper(p[0]).ToString()));

        var stageIds = stages.Select(s => s.StageId).ToList();
        var dealsByStage = new Dictionary<int, List<StageDealRow>>();

        // Una query por etapa (acotada con Take): el DbContext no soporta paralelismo,
        // así que se recorren secuencialmente — cada una es un Top-N indexado, no un escaneo completo.
        foreach (var stageId in stageIds)
        {
            dealsByStage[stageId] = await baseQ
                .Where(d => d.StageId == stageId)
                .OrderByDescending(d => d.CreatedOn)
                .Take(dealsPerStage)
                .Select(d => new StageDealRow(
                    d.DealId, d.DealName, d.QuotedAmount, d.Status, d.CreatedOn, d.CloseDate, d.StageId,
                    d.Company != null ? d.Company.Name : null,
                    d.PrimaryContact != null ? (d.PrimaryContact.FirstName + " " + d.PrimaryContact.LastName).Trim() : null,
                    d.DealUsers
                        .Where(du => du.RoleInDeal == "Owner" || du.RoleInDeal == null)
                        .Select(du => du.User.UserProfile != null
                            ? (du.User.UserProfile.FirstName + " " + du.User.UserProfile.LastName).Trim()
                            : du.User.UserName ?? "")
                        .FirstOrDefault() ?? ""))
                .ToListAsync();
        }

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
                    dealId       = d.DealId,
                    dealName     = d.DealName,
                    company      = d.Company,
                    contact      = d.Contact,
                    quotedAmount = d.QuotedAmount,
                    status       = d.Status,
                    createdOn    = d.CreatedOn,
                    closeDate    = d.CloseDate,
                    stageId      = d.StageId,
                    ownerName     = d.OwnerRaw,
                    ownerInitials = d.OwnerRaw.Length > 0 ? Initials(d.OwnerRaw) : "?",
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

        var stageScope = await HttpContext.RequestServices.GetRequiredService<ProfetAPI.Services.IVisibilityService>().ForAccountAsync(User, resolvedAccountId);
        if (!stageScope.All)
        {
            var stageIds = stageScope.UserIds;
            query = query.Where(d => d.DealUsers.Any(du => stageIds.Contains(du.UserId)));
        }

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

    // GET /api/deals/{id}/next-action  — próxima mejor acción (IA)
    [HttpGet("{id:int}/next-action")]
    [SwaggerOperation(Summary = "Próxima mejor acción sugerida por IA para la oportunidad")]
    public async Task<IActionResult> GetNextAction(int id)
    {
        var deal = await _context.Deals.AsNoTracking()
            .Where(d => d.DealId == id)
            .Select(d => new { d.AccountId }).FirstOrDefaultAsync();
        if (deal == null) return NotFound(new { message = "Deal no encontrado." });

        if (!IsAdminGlobal)
        {
            var belongs = await _context.AccountInternalUsers
                .AnyAsync(a => a.AccountId == deal.AccountId && a.UserId == CurrentUserId);
            if (!belongs) return Forbid();
        }

        var r = await _nextAction.GetForDealAsync(id);
        return Ok(new { available = r.Available, summary = r.Summary, action = r.Action, priority = r.Priority, reason = r.Reason });
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
                d.StageId, d.AccountId, d.CompanyId, d.PrimaryContactId, d.SequencePaused,
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
            accountId      = deal.AccountId,
            sequencePaused = deal.SequencePaused,
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

    // GET /api/deals/{id}/pending-tasks — tareas abiertas de la etapa actual (para el aviso antes de mover)
    [HttpGet("{id}/pending-tasks")]
    [SwaggerOperation(Summary = "Tareas de la secuencia todavía abiertas en la etapa actual de este deal")]
    public async Task<IActionResult> GetPendingTasks(int id)
    {
        var deal = await _context.Deals.AsNoTracking().FirstOrDefaultAsync(d => d.DealId == id);
        if (deal == null) return NotFound(new { message = "Deal no encontrado." });

        if (!IsAdminGlobal)
        {
            var belongs = await _context.AccountInternalUsers
                .AnyAsync(a => a.AccountId == deal.AccountId && a.UserId == CurrentUserId);
            if (!belongs) return Forbid();
        }

        if (!deal.StageId.HasValue) return Ok(new { gatingMode = "Warn", tasks = Array.Empty<object>() });

        var gatingMode = await _playbooks.GetGatingModeAsync(deal.AccountId);
        var pending = await _playbooks.GetOpenGatingTasksAsync("Deal", deal.DealId, deal.StageId.Value);
        return Ok(new
        {
            gatingMode,
            tasks = pending.Select(p => new { p.ActivityId, p.Subject, p.TaskStatus, p.DueDate }),
        });
    }

    // PATCH /api/deals/{id}/sequence-pause  — pausar/reanudar el envío automático de la secuencia
    [HttpPatch("{id}/sequence-pause")]
    [SwaggerOperation(Summary = "Pausar o reanudar el envío automático de la secuencia para este deal")]
    public async Task<IActionResult> SetSequencePause(int id, [FromBody] SequencePauseDto dto)
    {
        var deal = await _context.Deals.FindAsync(id);
        if (deal == null) return NotFound(new { message = "Deal no encontrado." });

        if (!IsAdminGlobal)
        {
            var belongs = await _context.AccountInternalUsers
                .AnyAsync(a => a.AccountId == deal.AccountId && a.UserId == CurrentUserId);
            if (!belongs) return Forbid();
        }

        deal.SequencePaused = dto.Paused;
        await _context.SaveChangesAsync();
        return Ok(new { deal.DealId, deal.SequencePaused });
    }


    public record DealStatusDto(string Status, decimal? FinalAmount);

    // PATCH /api/deals/{id}/status — marcar la oportunidad como Ganada, Perdida o reabrirla
    [HttpPatch("{id:int}/status")]
    [SwaggerOperation(Summary = "Cerrar una oportunidad (Ganado / Perdido) o reabrirla (Abierto)")]
    public async Task<IActionResult> SetStatus(int id, [FromBody] DealStatusDto dto,
        [FromServices] Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopes,
        [FromServices] ProfetAPI.Services.IAlertService alerts)
    {
        if (dto.Status is not ("Abierto" or "Ganado" or "Perdido"))
            return BadRequest(new { message = "El estatus debe ser Abierto, Ganado o Perdido." });

        var deal = await _context.Deals.FindAsync(id);
        if (deal == null) return NotFound(new { message = "Deal no encontrado." });
        if (!IsAdminGlobal && !await _context.AccountInternalUsers.AnyAsync(a => a.AccountId == deal.AccountId && a.UserId == CurrentUserId))
            return Forbid();

        var previous = deal.Status;
        if (previous == dto.Status) return Ok(new { deal.DealId, deal.Status, deal.CloseDate, deal.FinalAmount, unchanged = true });

        deal.Status = dto.Status;
        if (dto.Status == "Abierto") { deal.CloseDate = null; }
        else
        {
            deal.CloseDate = DateTime.UtcNow;
            if (dto.Status == "Ganado") deal.FinalAmount = dto.FinalAmount ?? deal.FinalAmount ?? deal.QuotedAmount;
        }
        await _context.SaveChangesAsync();

        await _timeline.LogAsync(deal.AccountId, "Deal", deal.DealId, "deal_status",
            dto.Status == "Ganado" ? "Oportunidad ganada" : dto.Status == "Perdido" ? "Oportunidad perdida" : "Oportunidad reabierta",
            detail: $"Estatus: {previous} → {dto.Status}", userId: CurrentUserId);

        if (dto.Status == "Ganado")
            await NotifyManagersDealWonAsync(deal, alerts);

        // Webhooks salientes configurados para este evento (en segundo plano, con reintentos)
        if (dto.Status is "Ganado" or "Perdido")
            ProfetAPI.Services.WebhookDispatchExtensions.DispatchInBackground(scopes, deal.AccountId, dto.Status == "Ganado" ? "DealWon" : "DealLost", new
            {
                dealId = deal.DealId, dealName = deal.DealName, status = deal.Status,
                amount = deal.FinalAmount ?? deal.QuotedAmount, closeDate = deal.CloseDate, accountId = deal.AccountId,
            });

        return Ok(new { deal.DealId, deal.Status, deal.CloseDate, deal.FinalAmount });
    }

    /// <summary>Avisa de inmediato a quien gerencia: Admin/Manager del cliente y el líder del equipo del vendedor (no al que cerró).</summary>
    private async Task NotifyManagersDealWonAsync(Models.Deal deal, ProfetAPI.Services.IAlertService alerts)
    {
        var customerId = await _context.Accounts.Where(a => a.AccountId == deal.AccountId).Select(a => a.CustomerId).FirstOrDefaultAsync();
        var managerRoles = new[] { "Admin", "ManagerAdmin", "Manager" };

        var recipients = await (from u in _context.Users
                                join ur in _context.UserRoles on u.Id equals ur.UserId
                                join r in _context.Roles on ur.RoleId equals r.Id
                                where u.CustomerId == customerId && managerRoles.Contains(r.Name!)
                                select u.Id).Distinct().ToListAsync();

        // Líder(es) del equipo del responsable del trato
        var ownerId = await _context.DealUsers.Where(du => du.DealId == deal.DealId && du.RoleInDeal == "Owner").Select(du => du.UserId).FirstOrDefaultAsync();
        var closerId = ownerId ?? CurrentUserId;
        if (closerId != null)
        {
            var leaders = await (from ut in _context.UserTeams
                                 join t in _context.Teams on ut.TeamId equals t.Id
                                 where ut.UserId == closerId && t.LeaderId != null
                                 select t.LeaderId!).Distinct().ToListAsync();
            recipients.AddRange(leaders);
        }

        var closerName = closerId == null ? "Un vendedor" : await _context.Users.Where(u => u.Id == closerId)
            .Select(u => u.UserProfile != null ? (u.UserProfile.FirstName + " " + u.UserProfile.LastName) : u.Email).FirstOrDefaultAsync() ?? "Un vendedor";
        var amount = deal.FinalAmount ?? deal.QuotedAmount;
        var message = $"¡Trato ganado! {closerName.Trim()} ganó \"{deal.DealName}\"" + (amount > 0 ? $" por ${amount:N0}" : "");

        foreach (var uid in recipients.Distinct().Where(x => x != CurrentUserId && x != closerId))
            await alerts.SendAsync(uid, ProfetAPI.Services.AlertType.DealWon, message, $"/oportunidades?id={deal.DealId}", "Deal", deal.DealId);
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

        // ── Gating: si el playbook bloquea, no se puede salir de la etapa actual con tareas abiertas ──
        var previousStageId = deal.StageId;
        if (previousStageId.HasValue)
        {
            var gatingMode = await _playbooks.GetGatingModeAsync(deal.AccountId);
            if (gatingMode == "Block")
            {
                var pending = await _playbooks.GetOpenGatingTasksAsync("Deal", deal.DealId, previousStageId.Value);
                if (pending.Count > 0)
                    return BadRequest(new
                    {
                        message = "Este deal tiene tareas pendientes de la etapa actual. Complétalas antes de avanzar.",
                        pendingTasks = pending.Select(p => new { p.ActivityId, p.Subject, p.TaskStatus }),
                    });
            }
        }

        deal.StageId = model.StageId;
        await _context.SaveChangesAsync();

        // ── Tareas de la nueva etapa (secuencia) ──────────────────────────────
        if (model.StageId.HasValue)
            await _playbooks.ApplyDealStageAsync(deal.AccountId, deal.DealId, model.StageId.Value, CurrentUserId);

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

        // Registrar el cambio de etapa en la línea de tiempo del deal
        await _timeline.LogAsync(accId, "Deal", deal.DealId, "stage_change",
            string.IsNullOrWhiteSpace(stageName) ? "Cambio de etapa" : $"Movida a: {stageName}",
            userId: CurrentUserId);

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

    // GET /api/deals/{id}/timeline  — hilo cronológico del deal
    [HttpGet("{id:int}/timeline")]
    [SwaggerOperation(Summary = "Línea de tiempo de la oportunidad")]
    public async Task<IActionResult> GetTimeline(int id, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var deal = await _context.Deals.AsNoTracking()
            .Where(d => d.DealId == id).Select(d => new { d.AccountId }).FirstOrDefaultAsync();
        if (deal == null) return NotFound(new { message = "Oportunidad no encontrada." });

        if (!IsAdminGlobal)
        {
            var belongs = await _context.AccountInternalUsers
                .AnyAsync(a => a.AccountId == deal.AccountId && a.UserId == CurrentUserId);
            if (!belongs) return Forbid();
        }

        var events = await _context.TimelineEvents.AsNoTracking()
            .Where(e => e.EntityType == "Deal" && e.EntityId == id && !e.Deleted)
            .OrderByDescending(e => e.CreatedOn)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new { e.TimelineEventId, e.Type, e.Title, e.Detail, e.CreatedByUserId, e.CreatedOn })
            .ToListAsync();
        return Ok(events);
    }

    // POST /api/deals/{id}/timeline/note
    [HttpPost("{id:int}/timeline/note")]
    [SwaggerOperation(Summary = "Agregar nota a la línea de tiempo de la oportunidad")]
    public async Task<IActionResult> AddTimelineNote(int id, [FromBody] DealNoteDto dto)
    {
        var deal = await _context.Deals.AsNoTracking()
            .Where(d => d.DealId == id).Select(d => new { d.AccountId }).FirstOrDefaultAsync();
        if (deal == null) return NotFound(new { message = "Oportunidad no encontrada." });

        if (!IsAdminGlobal)
        {
            var belongs = await _context.AccountInternalUsers
                .AnyAsync(a => a.AccountId == deal.AccountId && a.UserId == CurrentUserId);
            if (!belongs) return Forbid();
        }
        if (string.IsNullOrWhiteSpace(dto.Text)) return BadRequest(new { message = "La nota no puede estar vacía." });

        await _timeline.LogAsync(deal.AccountId, "Deal", id, "note", "Nota",
            detail: dto.Text.Trim(), userId: CurrentUserId);
        return Ok(new { added = true });
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


    // GET /api/deals/export?accountId=&status=  — descarga las oportunidades de la cuenta en Excel
    [HttpGet("export")]
    [SwaggerOperation(Summary = "Exportar oportunidades a Excel")]
    public async Task<IActionResult> ExportDeals([FromQuery] int? accountId, [FromQuery] string? status)
    {
        int acc;
        if (accountId.HasValue)
        {
            if (!IsAdminGlobal && !await _context.AccountInternalUsers.AnyAsync(a => a.AccountId == accountId && a.UserId == CurrentUserId))
                return Forbid();
            acc = accountId.Value;
        }
        else
        {
            if (IsAdminGlobal) return BadRequest(new { message = "AdminGlobal debe especificar accountId." });
            var mine = await _context.AccountInternalUsers.Where(a => a.UserId == CurrentUserId).Select(a => (int?)a.AccountId).FirstOrDefaultAsync();
            if (mine == null) return NotFound(new { message = "Sin cuenta asignada." });
            acc = mine.Value;
        }

        var q = _context.Deals.AsNoTracking().Where(d => d.AccountId == acc);
        var expScope = await HttpContext.RequestServices.GetRequiredService<ProfetAPI.Services.IVisibilityService>().ForAccountAsync(User, acc);
        if (!expScope.All)
        {
            var expIds = expScope.UserIds;
            q = q.Where(d => d.DealUsers.Any(du => expIds.Contains(du.UserId)));
        }
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(d => d.Status == status);

        var rows = await q.OrderByDescending(d => d.CreatedOn).Take(20000)
            .Select(d => new { d.DealId, d.DealName, d.QuotedAmount, d.FinalAmount,
                Company = d.Company != null ? d.Company.Name : null,
                ContactFirst = d.PrimaryContact != null ? d.PrimaryContact.FirstName : null,
                ContactLast = d.PrimaryContact != null ? d.PrimaryContact.LastName : null,
                Stage = d.Stage != null ? d.Stage.Name : null,
                d.Status, d.DealType, d.ProspectSource, d.CloseDate, d.CreatedOn })
            .ToListAsync();

        var dealIds = rows.Select(r => r.DealId).ToList();
        var ownerRows = new List<(int DealId, string UserId)>();
        foreach (var chunk in dealIds.Chunk(1000))
        {
            var part = await _context.DealUsers.AsNoTracking()
                .Where(du => chunk.Contains(du.DealId) && du.RoleInDeal == "Owner")
                .Select(du => new { du.DealId, du.UserId }).ToListAsync();
            ownerRows.AddRange(part.Select(p => (p.DealId, p.UserId)));
        }
        var ownerUserIds = ownerRows.Select(o => o.UserId).Distinct().ToList();
        var names = await _context.Users.AsNoTracking().Where(u => ownerUserIds.Contains(u.Id))
            .Select(u => new { u.Id, Name = u.UserProfile != null ? (u.UserProfile.FirstName + " " + u.UserProfile.LastName) : u.Email })
            .ToDictionaryAsync(u => u.Id, u => (u.Name ?? "").Trim());
        var ownerByDeal = ownerRows.GroupBy(o => o.DealId).ToDictionary(g => g.Key, g => names.TryGetValue(g.First().UserId, out var n) ? n : null);

        var typeLabel = new Dictionary<string, string> { ["NewBusiness"] = "Nuevo negocio", ["Upsell"] = "Venta adicional", ["Renewal"] = "Renovación" };
        var bytes = ProfetAPI.Services.XlsxExport.Build("Oportunidades",
            new[] { "ID", "Oportunidad", "Monto cotizado", "Monto final", "Empresa", "Contacto", "Etapa", "Estatus", "Tipo", "Fuente", "Fecha de cierre", "Responsable", "Fecha de alta" },
            rows.Select(r => new object?[] { r.DealId, r.DealName, r.QuotedAmount, r.FinalAmount, r.Company,
                $"{r.ContactFirst} {r.ContactLast}".Trim(), r.Stage, r.Status,
                r.DealType != null && typeLabel.TryGetValue(r.DealType, out var t) ? t : r.DealType, r.ProspectSource, r.CloseDate,
                ownerByDeal.TryGetValue(r.DealId, out var ow) ? ow : null, r.CreatedOn }));
        return File(bytes, ProfetAPI.Services.XlsxExport.ContentType, $"oportunidades_{DateTime.UtcNow:yyyyMMdd}.xlsx");
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
        // Los selectores operativos solo ofrecen clientes ya migrados al nuevo esquema de planes (o dados de alta con él);
        // la lista completa sigue en Administración > Clientes.
        var q = _context.Customers.AsNoTracking().Where(c => (c.Deleted ?? false) == false && c.IsMigrated);
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

public class DealNoteDto
{
    public string Text { get; set; } = "";
}
