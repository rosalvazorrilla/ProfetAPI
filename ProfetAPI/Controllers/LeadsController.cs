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
[SwaggerTag("CRM — Prospectos (Leads)")]
public class LeadsController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public LeadsController(ApplicationDbContext context) => _context = context;

    private string? CurrentUserId => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    private string? CurrentUserRole => User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
    private bool IsAdminGlobal => CurrentUserRole == "AdminGlobal";

    // GET /api/leads
    [HttpGet]
    [SwaggerOperation(Summary = "Listar prospectos con filtros y paginación")]
    [SwaggerResponse(200, "Lista paginada de leads")]
    public async Task<IActionResult> GetLeads(
        [FromQuery] int? accountId,
        [FromQuery] string? search,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] string? status,
        [FromQuery] string? prospectSource,
        [FromQuery] string? ownerId,
        [FromQuery] int? tagId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        // Resolve account
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

        // Build query — only load what we need, avoid deep Include chains
        var query = _context.Leads
            .Where(l => l.AccountId == resolvedAccountId && l.Deleted != true);

        // Filtros
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            // SQL Server uses case-insensitive collation by default — no ToLower() needed
            query = query.Where(l =>
                (l.Name    != null && l.Name.Contains(s))    ||
                (l.Email   != null && l.Email.Contains(s))   ||
                (l.Phone   != null && l.Phone.Contains(s))   ||
                (l.Company != null && l.Company.Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(l => l.Status == status);
        if (!string.IsNullOrWhiteSpace(prospectSource))
            query = query.Where(l => l.ProspectSource == prospectSource);
        if (!string.IsNullOrWhiteSpace(ownerId))
            query = query.Where(l => l.OwnerUserId == ownerId);
        if (tagId.HasValue)
            query = query.Where(l => _context.Taggings.Any(t => t.LeadId == (int)l.LeadId && t.TagId == tagId.Value));
        if (dateFrom.HasValue)
            query = query.Where(l => l.CreatedOn >= dateFrom.Value);
        if (dateTo.HasValue)
            query = query.Where(l => l.CreatedOn <= dateTo.Value.AddDays(1));

        var total = await query.CountAsync();

        // Fetch flat lead data — no navigation includes to avoid translation issues
        var leads = await query
            .OrderByDescending(l => l.CreatedOn)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new
            {
                l.LeadId,
                l.Name,
                l.Email,
                l.Phone,
                l.Company,
                l.ContactId,
                l.OwnerUserId,
                l.ProspectSource,
                l.Status,
                l.OriginType,
                l.CreatedOn,
                l.AdName,
            })
            .ToListAsync();

        // Load Contacts separately for leads that have one
        var contactIds = leads
            .Where(l => l.ContactId.HasValue)
            .Select(l => l.ContactId!.Value)
            .Distinct()
            .ToList();

        var contactRows = contactIds.Any()
            ? await _context.Contacts
                .Where(c => contactIds.Contains(c.ContactId))
                .Select(c => new
                {
                    c.ContactId,
                    c.FirstName,
                    c.LastName,
                    c.Email,
                    c.PhoneNumber,
                    c.CompanyId,
                })
                .ToListAsync()
            : new List<object>().Select(_ => new
                {
                    ContactId  = 0,
                    FirstName  = (string?)null,
                    LastName   = (string?)null,
                    Email      = (string?)null,
                    PhoneNumber= (string?)null,
                    CompanyId  = (int?)null,
                }).ToList();

        var contactDict = contactRows.ToDictionary(c => c.ContactId);

        // Load companies for those contacts
        var companyIds = contactRows
            .Where(c => c.CompanyId.HasValue)
            .Select(c => c.CompanyId!.Value)
            .Distinct()
            .ToList();

        var companies = companyIds.Any()
            ? await _context.Companies
                .Where(co => companyIds.Contains(co.CompanyId))
                .Select(co => new { co.CompanyId, co.Name })
                .ToDictionaryAsync(co => co.CompanyId, co => co.Name)
            : new Dictionary<int, string>();

        // Load owners
        var ownerIds = leads
            .Where(l => l.OwnerUserId != null)
            .Select(l => l.OwnerUserId!)
            .Distinct()
            .ToList();

        var ownerRows = ownerIds.Any()
            ? await _context.Users
                .Where(u => ownerIds.Contains(u.Id))
                .Include(u => u.UserProfile)
                .Select(u => new
                {
                    u.Id,
                    u.UserName,
                    FirstName = u.UserProfile != null ? u.UserProfile.FirstName : null,
                    LastName  = u.UserProfile != null ? u.UserProfile.LastName  : null,
                })
                .ToListAsync()
            : new List<object>().Select(_ => new
                {
                    Id        = "",
                    UserName  = (string?)null,
                    FirstName = (string?)null,
                    LastName  = (string?)null,
                }).ToList();

        var ownerDict = ownerRows.ToDictionary(o => o.Id);

        // Tags via LeadsTags
        var leadIdInts = leads.Select(l => (int)l.LeadId).ToList();
        var taggings = leadIdInts.Any()
            ? await _context.Taggings
                .Include(t => t.Tag)
                .Where(t => leadIdInts.Contains(t.LeadId))
                .ToListAsync()
            : new List<Tagging>();

        var tagsByLead = taggings
            .GroupBy(t => t.LeadId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(t => new
                {
                    t.Tag.TagId,
                    t.Tag.Name,
                    t.Tag.Color,
                    t.Tag.FontColor
                }).ToList<object>());

        var result = leads.Select(l =>
        {
            // Contact fields (prefer Contact record, fall back to Lead flat fields)
            contactDict.TryGetValue(l.ContactId ?? -1, out var contact);
            string? firstName   = contact?.FirstName;
            string? lastName    = contact?.LastName;
            string? email       = contact?.Email   ?? l.Email;
            string? phone       = contact?.PhoneNumber ?? l.Phone;
            string? companyName = (contact?.CompanyId.HasValue == true
                                   && companies.TryGetValue(contact.CompanyId!.Value, out var cn)) ? cn
                                : l.Company;

            // If no Contact name, split Lead.Name
            if (firstName == null && lastName == null && l.Name != null)
            {
                var parts = l.Name.Trim().Split(' ', 2);
                firstName = parts[0];
                lastName  = parts.Length > 1 ? parts[1] : null;
            }

            // Owner
            string ownerName = "";
            string ownerInitials = "?";
            if (l.OwnerUserId != null && ownerDict.TryGetValue(l.OwnerUserId, out var owner))
            {
                ownerName = ($"{owner.FirstName} {owner.LastName}").Trim();
                if (string.IsNullOrWhiteSpace(ownerName)) ownerName = owner.UserName ?? "";
                ownerInitials = string.Concat(
                    ownerName.Split(' ')
                             .Where(p => p.Length > 0)
                             .Take(2)
                             .Select(p => p[0].ToString().ToUpper()));
                if (string.IsNullOrEmpty(ownerInitials)) ownerInitials = "?";
            }

            tagsByLead.TryGetValue((int)l.LeadId, out var tags);
            return new
            {
                leadId         = l.LeadId,
                contactId      = l.ContactId,
                firstName,
                lastName,
                email,
                phone,
                company        = companyName,
                prospectSource = l.ProspectSource,
                status         = l.Status,
                originType     = l.OriginType,
                createdOn      = l.CreatedOn,
                ownerId        = l.OwnerUserId,
                ownerName,
                ownerInitials,
                tags           = tags ?? new List<object>(),
            };
        });

        return Ok(new
        {
            total,
            page,
            pageSize,
            data = result,
        });
    }

    // GET /api/leads/users?accountId=1  — usuarios de la cuenta para filtro de responsable
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

    // GET /api/leads/sources?accountId=1  — fuentes de prospecto de la cuenta
    [HttpGet("sources")]
    [SwaggerOperation(Summary = "Fuentes de prospecto disponibles en la cuenta")]
    public async Task<IActionResult> GetSources([FromQuery] int? accountId)
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

        // Fuentes desde ProspectSources ligadas a la account via leads existentes
        var sources = await _context.Leads
            .Where(l => l.AccountId == resolvedAccountId
                     && l.ProspectSource != null
                     && l.Deleted != true)
            .Select(l => l.ProspectSource!)
            .Distinct()
            .OrderBy(s => s)
            .Select(s => new { name = s })
            .ToListAsync();

        return Ok(sources);
    }

    // GET /api/leads/tags?accountId=1
    [HttpGet("tags")]
    [SwaggerOperation(Summary = "Etiquetas disponibles para filtrar leads")]
    public async Task<IActionResult> GetTags([FromQuery] int? accountId)
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

        // Obtener la cuenta para saber el CustomerId (las tags son por Customer)
        var account = await _context.Accounts.AsNoTracking()
            .Where(a => a.AccountId == resolvedAccountId)
            .Select(a => new { a.CustomerId })
            .FirstOrDefaultAsync();
        if (account == null) return NotFound();

        var tags = await _context.Tags
            .AsNoTracking()
            .Where(t => t.CustomerId == account.CustomerId)
            .OrderBy(t => t.Name)
            .Select(t => new { t.TagId, t.Name, t.Color, t.FontColor })
            .ToListAsync();

        return Ok(tags);
    }

    // GET /api/leads/statuses  — estados posibles
    [HttpGet("statuses")]
    [SwaggerOperation(Summary = "Estatus posibles de un lead")]
    public IActionResult GetStatuses()
    {
        return Ok(new[] { "Nuevo", "Contactado", "Calificado", "No calificado", "Convertido", "Perdido" });
    }

    // GET /api/leads/{id}  — detalle completo de un lead
    [HttpGet("{id:long}")]
    [SwaggerOperation(Summary = "Detalle completo de un lead")]
    [SwaggerResponse(200, "Lead encontrado")]
    [SwaggerResponse(404, "No encontrado")]
    public async Task<IActionResult> GetLead(long id)
    {
        var lead = await _context.Leads
            .AsNoTracking()
            .Where(l => l.LeadId == id && l.Deleted != true)
            .Select(l => new
            {
                l.LeadId, l.Name, l.Email, l.Phone, l.Company,
                l.Position, l.City, l.Status, l.OriginType,
                l.ProspectSource, l.AdName, l.InitialMessage,
                l.AccountId, l.OwnerUserId, l.ContactId,
                l.StageId, l.CampaignId, l.LifecycleStatus,
                l.CreatedOn,
            })
            .FirstOrDefaultAsync();

        if (lead == null) return NotFound(new { message = "Prospecto no encontrado." });

        if (!IsAdminGlobal)
        {
            var belongs = await _context.AccountInternalUsers
                .AnyAsync(a => a.AccountId == lead.AccountId && a.UserId == CurrentUserId);
            if (!belongs) return Forbid();
        }

        // Owner info
        object? ownerObj = null;
        if (lead.OwnerUserId != null)
        {
            ownerObj = await _context.Users
                .AsNoTracking()
                .Where(u => u.Id == lead.OwnerUserId)
                .Select(u => new
                {
                    id = u.Id,
                    name = u.UserProfile != null
                        ? (u.UserProfile.FirstName + " " + u.UserProfile.LastName).Trim()
                        : u.UserName ?? "",
                })
                .FirstOrDefaultAsync();
        }

        // Contact info (if linked)
        object? contactObj = null;
        if (lead.ContactId.HasValue)
        {
            contactObj = await _context.Contacts
                .AsNoTracking()
                .Where(c => c.ContactId == lead.ContactId.Value)
                .Select(c => new
                {
                    c.ContactId, c.FirstName, c.LastName, c.Email,
                    c.PhoneNumber, c.Position, c.CompanyId,
                    companyName = c.Company != null ? c.Company.Name : null,
                })
                .FirstOrDefaultAsync();
        }

        // Tags
        var tags = await _context.Taggings
            .AsNoTracking()
            .Where(t => t.LeadId == (int)id)
            .Select(t => new { t.Tag.TagId, t.Tag.Name, t.Tag.Color, t.Tag.FontColor })
            .ToListAsync();

        // Scoring: total points from saved answers
        var scoringTotal = await _context.LeadScoringAnswers
            .AsNoTracking()
            .Where(a => a.LeadId == id)
            .SumAsync(a => (decimal?)a.PointsAwarded) ?? 0m;

        // Account users for owner select
        var accountUsers = lead.AccountId.HasValue
            ? await _context.AccountInternalUsers
                .AsNoTracking()
                .Where(au => au.AccountId == lead.AccountId.Value)
                .Select(au => new
                {
                    userId = au.UserId,
                    name   = au.User.UserProfile != null
                        ? (au.User.UserProfile.FirstName + " " + au.User.UserProfile.LastName).Trim()
                        : au.User.UserName ?? "",
                })
                .ToListAsync()
            : new List<object>().Select(_ => new { userId = "", name = "" }).ToList();

        return Ok(new
        {
            leadId         = lead.LeadId,
            name           = lead.Name,
            email          = lead.Email,
            phone          = lead.Phone,
            company        = lead.Company,
            position       = lead.Position,
            city           = lead.City,
            status         = lead.Status,
            originType     = lead.OriginType,
            prospectSource = lead.ProspectSource,
            adName         = lead.AdName,
            initialMessage = lead.InitialMessage,
            accountId      = lead.AccountId,
            contactId      = lead.ContactId,
            lifecycleStatus = lead.LifecycleStatus,
            createdOn      = lead.CreatedOn,
            owner          = ownerObj,
            contact        = contactObj,
            tags,
            scoringTotal,
            accountUsers,
        });
    }

    // POST /api/leads  — crear lead manualmente
    [HttpPost]
    [SwaggerOperation(Summary = "Crear nuevo prospecto")]
    [SwaggerResponse(201, "Lead creado")]
    [SwaggerResponse(400, "Datos inválidos")]
    public async Task<IActionResult> CreateLead([FromBody] CreateLeadDto model)
    {
        // Resolve account
        int resolvedAccountId;
        if (model.AccountId.HasValue)
        {
            if (!IsAdminGlobal)
            {
                var belongs = await _context.AccountInternalUsers
                    .AnyAsync(a => a.AccountId == model.AccountId && a.UserId == CurrentUserId);
                if (!belongs) return Forbid();
            }
            resolvedAccountId = model.AccountId.Value;
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

        // Validate account exists
        var account = await _context.Accounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.AccountId == resolvedAccountId);
        if (account == null) return NotFound(new { message = "Cuenta no encontrada." });

        var lead = new Lead
        {
            AccountId      = resolvedAccountId,
            CampaignId     = account.CustomerId, // use customerId as campaignId placeholder
            Name           = model.Name,
            Email          = model.Email,
            Phone          = model.Phone,
            Company        = model.Company,
            Position       = model.Position,
            City           = model.City,
            ProspectSource = model.ProspectSource,
            InitialMessage = model.InitialMessage,
            Status         = "Nuevo",
            OriginType     = "Manual",
            OwnerUserId    = model.OwnerId ?? CurrentUserId,
            Active         = true,
            Deleted        = false,
            CreatedOn      = DateTime.UtcNow,
        };

        _context.Leads.Add(lead);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetLead), new { id = lead.LeadId }, new
        {
            leadId    = lead.LeadId,
            name      = lead.Name,
            status    = lead.Status,
            accountId = lead.AccountId,
            createdOn = lead.CreatedOn,
        });
    }

    // PATCH /api/leads/{id}/status  — actualizar estatus
    [HttpPatch("{id:long}/status")]
    [SwaggerOperation(Summary = "Actualizar estatus del prospecto")]
    [SwaggerResponse(200, "Estatus actualizado")]
    [SwaggerResponse(404, "No encontrado")]
    public async Task<IActionResult> UpdateStatus(long id, [FromBody] UpdateLeadStatusDto model)
    {
        var lead = await _context.Leads.FindAsync(id);
        if (lead == null || lead.Deleted == true) return NotFound(new { message = "Prospecto no encontrado." });

        if (!IsAdminGlobal)
        {
            var belongs = await _context.AccountInternalUsers
                .AnyAsync(a => a.AccountId == lead.AccountId && a.UserId == CurrentUserId);
            if (!belongs) return Forbid();
        }

        lead.Status = model.Status;
        await _context.SaveChangesAsync();
        return Ok(new { leadId = lead.LeadId, status = lead.Status });
    }

    // GET /api/leads/{id}/scoring  — cuestionario de calificación con respuestas actuales
    [HttpGet("{id:long}/scoring")]
    [SwaggerOperation(Summary = "Cuestionario de calificación y respuestas actuales del lead")]
    [SwaggerResponse(200, "Cuestionario cargado")]
    public async Task<IActionResult> GetScoring(long id)
    {
        var lead = await _context.Leads.AsNoTracking()
            .Where(l => l.LeadId == id && l.Deleted != true)
            .Select(l => new { l.AccountId })
            .FirstOrDefaultAsync();
        if (lead == null) return NotFound(new { message = "Prospecto no encontrado." });

        if (!IsAdminGlobal && lead.AccountId.HasValue)
        {
            var belongs = await _context.AccountInternalUsers
                .AnyAsync(a => a.AccountId == lead.AccountId && a.UserId == CurrentUserId);
            if (!belongs) return Forbid();
        }

        // Get scoring model for this account
        var model = await _context.ScoringModels
            .AsNoTracking()
            .Where(m => m.AccountId == lead.AccountId)
            .Select(m => new { m.ScoringModelId, m.Name })
            .FirstOrDefaultAsync();

        if (model == null) return Ok(new { hasScoring = false, questions = Array.Empty<object>(), totalPoints = 0m });

        // Questions with options
        var questions = await _context.ScoringQuestions
            .AsNoTracking()
            .Where(q => q.ScoringModelId == model.ScoringModelId)
            .OrderBy(q => q.OrderPosition)
            .Select(q => new
            {
                q.QuestionId,
                q.QuestionText,
                q.QuestionType,
                q.IsRequired,
                q.OrderPosition,
                options = q.AnswerOptions
                    .OrderBy(o => o.OrderPosition)
                    .Select(o => new
                    {
                        o.AnswerOptionId,
                        o.AnswerText,
                        o.Points,
                        o.OrderPosition,
                    }).ToList(),
            })
            .ToListAsync();

        // Current answers for this lead
        var currentAnswers = await _context.LeadScoringAnswers
            .AsNoTracking()
            .Where(a => a.LeadId == id)
            .Select(a => new
            {
                a.ScoringAnswerId,
                a.QuestionId,
                a.AnswerOptionId,
                a.TextValue,
                a.NumericValue,
                a.PointsAwarded,
            })
            .ToListAsync();

        var answersByQuestion = currentAnswers.ToDictionary(a => a.QuestionId);
        var totalPoints = currentAnswers.Sum(a => a.PointsAwarded);

        // Max possible score
        var maxPoints = questions.Sum(q =>
            q.options.Count > 0 ? q.options.Max(o => o.Points) : 0);

        return Ok(new
        {
            hasScoring  = true,
            scoringModelId = model.ScoringModelId,
            modelName   = model.Name,
            questions   = questions.Select(q =>
            {
                answersByQuestion.TryGetValue(q.QuestionId, out var ans);
                return new
                {
                    q.QuestionId,
                    q.QuestionText,
                    q.QuestionType,
                    q.IsRequired,
                    q.OrderPosition,
                    q.options,
                    currentAnswerOptionId = ans?.AnswerOptionId,
                    currentTextValue      = ans?.TextValue,
                    currentNumericValue   = ans?.NumericValue,
                    pointsAwarded         = ans?.PointsAwarded ?? 0,
                };
            }).ToList(),
            totalPoints,
            maxPoints,
            percentage  = maxPoints > 0 ? Math.Round(totalPoints / maxPoints * 100, 1) : 0,
        });
    }

    // POST /api/leads/{id}/scoring/answers  — guardar respuestas y recalcular score
    [HttpPost("{id:long}/scoring/answers")]
    [SwaggerOperation(Summary = "Guardar respuestas de calificación")]
    [SwaggerResponse(200, "Respuestas guardadas")]
    public async Task<IActionResult> SaveScoringAnswers(long id, [FromBody] List<ScoringAnswerDto> answers)
    {
        var lead = await _context.Leads
            .Where(l => l.LeadId == id && l.Deleted != true)
            .FirstOrDefaultAsync();
        if (lead == null) return NotFound(new { message = "Prospecto no encontrado." });

        if (!IsAdminGlobal)
        {
            var belongs = await _context.AccountInternalUsers
                .AnyAsync(a => a.AccountId == lead.AccountId && a.UserId == CurrentUserId);
            if (!belongs) return Forbid();
        }

        // Load all answer options for point calculation
        var optionIds = answers.Where(a => a.AnswerOptionId.HasValue).Select(a => a.AnswerOptionId!.Value).ToList();
        var optionPoints = optionIds.Any()
            ? await _context.ScoringAnswerOptions.AsNoTracking()
                .Where(o => optionIds.Contains(o.AnswerOptionId))
                .ToDictionaryAsync(o => o.AnswerOptionId, o => o.Points)
            : new Dictionary<int, decimal>();

        // Remove existing answers for modified questions
        var questionIds = answers.Select(a => a.QuestionId).Distinct().ToList();
        var existing = await _context.LeadScoringAnswers
            .Where(a => a.LeadId == id && questionIds.Contains(a.QuestionId))
            .ToListAsync();
        _context.LeadScoringAnswers.RemoveRange(existing);

        // Add new answers
        var newAnswers = answers.Select(a =>
        {
            var pts = a.AnswerOptionId.HasValue && optionPoints.TryGetValue(a.AnswerOptionId.Value, out var p) ? p : 0m;
            return new LeadScoringAnswer
            {
                LeadId         = id,
                QuestionId     = a.QuestionId,
                AnswerOptionId = a.AnswerOptionId,
                TextValue      = a.TextValue,
                NumericValue   = a.NumericValue,
                PointsAwarded  = pts,
            };
        }).ToList();

        _context.LeadScoringAnswers.AddRange(newAnswers);
        await _context.SaveChangesAsync();

        var totalPoints = await _context.LeadScoringAnswers
            .Where(a => a.LeadId == id)
            .SumAsync(a => (decimal?)a.PointsAwarded) ?? 0m;

        return Ok(new { leadId = id, totalPoints });
    }

    // POST /api/leads/{id}/convert-to-deal  — convertir lead en oportunidad
    [HttpPost("{id:long}/convert-to-deal")]
    [SwaggerOperation(Summary = "Convertir prospecto en oportunidad (crea Contacto, Empresa y Deal)")]
    [SwaggerResponse(201, "Deal creado")]
    [SwaggerResponse(400, "Error en conversión")]
    [SwaggerResponse(404, "Lead no encontrado")]
    public async Task<IActionResult> ConvertToDeal(long id, [FromBody] ConvertToDealDto model)
    {
        var lead = await _context.Leads
            .Where(l => l.LeadId == id && l.Deleted != true)
            .FirstOrDefaultAsync();
        if (lead == null) return NotFound(new { message = "Prospecto no encontrado." });

        if (!IsAdminGlobal)
        {
            var belongs = await _context.AccountInternalUsers
                .AnyAsync(a => a.AccountId == lead.AccountId && a.UserId == CurrentUserId);
            if (!belongs) return Forbid();
        }

        if (!lead.AccountId.HasValue) return BadRequest(new { message = "El lead no tiene cuenta asignada." });
        var accountId = lead.AccountId.Value;

        using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            // ── 1. Company ───────────────────────────────────────────────────────
            int? companyId = lead.ContactId.HasValue
                ? (await _context.Contacts.AsNoTracking()
                    .Where(c => c.ContactId == lead.ContactId.Value)
                    .Select(c => c.CompanyId)
                    .FirstOrDefaultAsync())
                : null;

            var companyName = model.CompanyName ?? lead.Company;
            if (!string.IsNullOrWhiteSpace(companyName) && companyId == null)
            {
                var company = new Company
                {
                    Name            = companyName,
                    LifecycleStatus = "Prospecto",
                    CreatedOn       = DateTime.UtcNow,
                    ModifiedOn      = DateTime.UtcNow,
                };
                _context.Companies.Add(company);
                await _context.SaveChangesAsync();
                companyId = company.CompanyId;
            }

            // ── 2. Contact ───────────────────────────────────────────────────────
            int? contactId = lead.ContactId;
            if (contactId == null)
            {
                var email     = model.ContactEmail ?? lead.Email;
                var firstName = model.ContactFirstName;
                var lastName  = model.ContactLastName;

                // Split Name if names not provided
                if (firstName == null && lastName == null && lead.Name != null)
                {
                    var parts = lead.Name.Trim().Split(' ', 2);
                    firstName = parts[0];
                    lastName  = parts.Length > 1 ? parts[1] : null;
                }

                // Try to find existing contact by email to avoid UNIQUE constraint violation
                Contact? existingContact = null;
                if (!string.IsNullOrWhiteSpace(email))
                {
                    existingContact = await _context.Contacts
                        .Where(c => c.Email == email)
                        .FirstOrDefaultAsync();
                }

                if (existingContact != null)
                {
                    contactId = existingContact.ContactId;
                    // Update company if not set
                    if (existingContact.CompanyId == null && companyId != null)
                    {
                        existingContact.CompanyId  = companyId;
                        existingContact.ModifiedOn = DateTime.UtcNow;
                    }
                }
                else
                {
                    var contact = new Contact
                    {
                        FirstName       = firstName ?? lead.Name,
                        LastName        = lastName,
                        Email           = email,
                        PhoneNumber     = model.ContactPhone ?? lead.Phone,
                        Position        = lead.Position,
                        CompanyId       = companyId,
                        LifecycleStatus = "Lead",
                        CreatedOn       = DateTime.UtcNow,
                        ModifiedOn      = DateTime.UtcNow,
                    };
                    _context.Contacts.Add(contact);
                    await _context.SaveChangesAsync();
                    contactId = contact.ContactId;
                }

                // Link contact back to lead
                lead.ContactId = contactId;
            }

            // ── 3. First Stage of Account Funnel ─────────────────────────────────
            var firstStageId = await _context.Stages
                .AsNoTracking()
                .Where(s => s.Funnel.AccountId == accountId)
                .OrderBy(s => s.Order)
                .Select(s => (int?)s.StageId)
                .FirstOrDefaultAsync();

            // ── 4. Deal ──────────────────────────────────────────────────────────
            var nameParts = lead.Name?.Trim().Split(' ', 2);
            var firstName2 = nameParts?[0];
            var dealName = model.DealName
                ?? (string.IsNullOrWhiteSpace(lead.Company)
                    ? $"Oportunidad de {lead.Name}"
                    : $"{lead.Company} — {firstName2}");

            var deal = new Deal
            {
                DealName          = dealName,
                AccountId         = accountId,
                CompanyId         = companyId,
                PrimaryContactId  = contactId,
                StageId           = firstStageId,
                Status            = "Abierto",
                DealType          = model.DealType ?? "NewBusiness",
                QuotedAmount      = model.QuotedAmount,
                OriginatingLeadId = id,
                ProspectSource    = lead.ProspectSource,
                AdName            = lead.AdName,
                OriginType        = lead.OriginType,
                CreatedOn         = DateTime.UtcNow,
            };
            _context.Deals.Add(deal);
            await _context.SaveChangesAsync();

            // ── 5. DealUser (owner) ──────────────────────────────────────────────
            var ownerId = lead.OwnerUserId ?? CurrentUserId;
            if (!string.IsNullOrEmpty(ownerId))
            {
                _context.DealUsers.Add(new DealUser
                {
                    DealId     = deal.DealId,
                    UserId     = ownerId,
                    RoleInDeal = "Owner",
                });
                await _context.SaveChangesAsync();
            }

            // ── 6. Update Lead ───────────────────────────────────────────────────
            lead.Status    = "Convertido";
            lead.ContactId = contactId;
            await _context.SaveChangesAsync();

            await tx.CommitAsync();

            return StatusCode(201, new
            {
                dealId    = deal.DealId,
                contactId = contactId,
                companyId = companyId,
                leadId    = id,
                message   = "Convertido exitosamente.",
            });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return BadRequest(new { message = "Error en la conversión.", detail = ex.Message });
        }
    }

    // GET /api/leads/customers?activeOnly=true — solo AdminGlobal
    [HttpGet("customers")]
    [Authorize(Roles = "AdminGlobal")]
    [SwaggerOperation(Summary = "Lista de clientes (AdminGlobal)")]
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

    // GET /api/leads/accounts?customerId=&activeOnly=true
    [HttpGet("accounts")]
    [SwaggerOperation(Summary = "Cuentas accesibles para el selector")]
    public async Task<IActionResult> GetAccounts(
        [FromQuery] int? customerId,
        [FromQuery] bool activeOnly = true)
    {
        if (IsAdminGlobal)
        {
            var q = _context.Accounts.AsNoTracking().AsQueryable();
            if (customerId.HasValue) q = q.Where(a => a.CustomerId == customerId.Value);
            if (activeOnly) q = q.Where(a => a.Status == "Activo");
            var list = await q
                .OrderBy(a => a.Name)
                .Select(a => new { a.AccountId, a.Name, a.CustomerId, customerName = a.Customer.Name, a.Status })
                .ToListAsync();
            return Ok(list);
        }
        else
        {
            var q = _context.AccountInternalUsers.AsNoTracking().Where(a => a.UserId == CurrentUserId);
            if (activeOnly)
                q = q.Where(a => a.Account.Status == "Activo");
            var list = await q
                .OrderBy(a => a.Account.Name)
                .Select(a => new { a.Account.AccountId, a.Account.Name, a.Account.CustomerId, customerName = "", a.Account.Status })
                .ToListAsync();
            return Ok(list);
        }
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

public class CreateLeadDto
{
    public int? AccountId      { get; set; }
    public string? Name        { get; set; }
    public string? Email       { get; set; }
    public string? Phone       { get; set; }
    public string? Company     { get; set; }
    public string? Position    { get; set; }
    public string? City        { get; set; }
    public string? ProspectSource { get; set; }
    public string? InitialMessage { get; set; }
    public string? OwnerId     { get; set; }
}

public class UpdateLeadStatusDto
{
    public string Status { get; set; } = "Nuevo";
}

public class ScoringAnswerDto
{
    public int QuestionId         { get; set; }
    public int? AnswerOptionId    { get; set; }
    public string? TextValue      { get; set; }
    public decimal? NumericValue  { get; set; }
}

public class ConvertToDealDto
{
    public string? DealName         { get; set; }
    public string? DealType         { get; set; }
    public decimal? QuotedAmount    { get; set; }
    public string? CompanyName      { get; set; }
    public string? ContactFirstName { get; set; }
    public string? ContactLastName  { get; set; }
    public string? ContactEmail     { get; set; }
    public string? ContactPhone     { get; set; }
}
