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
/// Gestión de cuentas (Accounts) desde el panel Admin Global.
/// Autenticación JWT — rol AdminGlobal.
/// Separado del SetupController (que es token-based y lo usa el cliente en el wizard).
/// </summary>
[Route("api/admin/customers/{customerId}/accounts")]
[ApiController]
[Authorize(Roles = "AdminGlobal")]
[SwaggerTag("Admin Global — Cuentas de Clientes")]
public class AdminAccountsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public AdminAccountsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<bool> CustomerExists(int customerId) =>
        await _context.Customers.AnyAsync(c => c.Id == customerId && c.Deleted == false);

    private async Task<Account?> GetAccount(int customerId, int accountId) =>
        await _context.Accounts.FirstOrDefaultAsync(a => a.AccountId == accountId && a.CustomerId == customerId);

    private static AdminAccountResponseDto MapAccount(Account a) => new()
    {
        AccountId = a.AccountId,
        Name = a.Name,
        Description = a.Description,
        Status = a.Status,
        AssignmentType = a.AssignmentType ?? "Carrusel"
    };

    private static AdminFunnelResponseDto MapFunnel(Funnel f) => new()
    {
        FunnelId = f.FunnelId,
        Name = f.Name,
        OriginatingTemplateId = f.OriginatingTemplateId,
        Stages = f.Stages.OrderBy(s => s.Order).Select(s => new AdminStageResponseDto
        {
            StageId = s.StageId, Name = s.Name, Order = s.Order, Color = s.Color
        }).ToList()
    };

    private static AdminScoringResponseDto MapScoring(ScoringModel sm, List<LeadTier> tiers, int? templateId = null) => new()
    {
        ScoringModelId = sm.ScoringModelId,
        ModelName = sm.Name,
        OriginatingTemplateId = templateId,
        Questions = sm.Questions.OrderBy(q => q.OrderPosition).Select(q => new AdminScoringQuestionResponseDto
        {
            QuestionId = q.QuestionId,
            QuestionText = q.QuestionText,
            QuestionType = q.QuestionType,
            IsRequired = q.IsRequired,
            OrderPosition = q.OrderPosition,
            AnswerOptions = q.AnswerOptions.OrderBy(a => a.OrderPosition).Select(a => new AdminAnswerOptionResponseDto
            {
                AnswerOptionId = a.AnswerOptionId,
                AnswerText = a.AnswerText,
                Points = a.Points,
                OrderPosition = a.OrderPosition
            }).ToList()
        }).ToList(),
        Tiers = tiers.OrderBy(t => t.MinScore).Select(t => new AdminTierResponseDto
        {
            TierId = t.TierId, Name = t.Name, MinScore = t.MinScore, MaxScore = t.MaxScore, Color = t.Color
        }).ToList()
    };

    // ════════════════════════════════════════════════════════════
    // ACCOUNTS CRUD
    // ════════════════════════════════════════════════════════════

    // GET /api/admin/customers/{customerId}/accounts
    [HttpGet]
    [SwaggerOperation(Summary = "Listar cuentas del cliente")]
    [SwaggerResponse(200, "Lista de cuentas", typeof(List<AdminAccountResponseDto>))]
    [SwaggerResponse(404, "Cliente no encontrado")]
    public async Task<IActionResult> GetAll(int customerId)
    {
        if (!await CustomerExists(customerId))
            return NotFound(new { message = "Cliente no encontrado." });

        var accounts = await _context.Accounts
            .Where(a => a.CustomerId == customerId)
            .Select(a => new AdminAccountResponseDto
            {
                AccountId = a.AccountId,
                Name = a.Name,
                Description = a.Description,
                Status = a.Status,
                AssignmentType = a.AssignmentType ?? "Carrusel"
            })
            .ToListAsync();

        return Ok(accounts);
    }

    // POST /api/admin/customers/{customerId}/accounts
    [HttpPost]
    [SwaggerOperation(Summary = "Crear cuenta para el cliente")]
    [SwaggerResponse(201, "Cuenta creada", typeof(AdminAccountResponseDto))]
    [SwaggerResponse(404, "Cliente no encontrado")]
    public async Task<IActionResult> Create(int customerId, [FromBody] CreateAdminAccountDto model)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (!await CustomerExists(customerId))
            return NotFound(new { message = "Cliente no encontrado." });

        var account = new Account
        {
            CustomerId = customerId,
            Name = model.Name,
            Description = model.Description,
            AssignmentType = model.AssignmentType,
            Status = "Borrador",
            CreatedOn = DateTime.UtcNow
        };
        _context.Accounts.Add(account);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAll), new { customerId }, MapAccount(account));
    }

    // PUT /api/admin/customers/{customerId}/accounts/{accountId}
    [HttpPut("{accountId}")]
    [SwaggerOperation(Summary = "Actualizar datos básicos de la cuenta")]
    [SwaggerResponse(200, "Cuenta actualizada", typeof(AdminAccountResponseDto))]
    [SwaggerResponse(404, "Cuenta no encontrada")]
    public async Task<IActionResult> Update(int customerId, int accountId, [FromBody] UpdateAdminAccountDto model)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var account = await GetAccount(customerId, accountId);
        if (account == null) return NotFound(new { message = "Cuenta no encontrada." });

        account.Name = model.Name;
        account.Description = model.Description;
        account.AssignmentType = model.AssignmentType;
        await _context.SaveChangesAsync();

        return Ok(MapAccount(account));
    }

    // DELETE /api/admin/customers/{customerId}/accounts/{accountId}
    [HttpDelete("{accountId}")]
    [SwaggerOperation(Summary = "Eliminar cuenta (solo en estado Borrador)")]
    [SwaggerResponse(204, "Eliminada")]
    [SwaggerResponse(400, "No se puede eliminar una cuenta activa")]
    [SwaggerResponse(404, "Cuenta no encontrada")]
    public async Task<IActionResult> Delete(int customerId, int accountId)
    {
        var account = await GetAccount(customerId, accountId);
        if (account == null) return NotFound(new { message = "Cuenta no encontrada." });
        if (account.Status == "Activo")
            return BadRequest(new { message = "No se puede eliminar una cuenta activa." });

        _context.Accounts.Remove(account);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // ════════════════════════════════════════════════════════════
    // FUNNEL
    // ════════════════════════════════════════════════════════════

    // GET /api/admin/customers/{customerId}/accounts/{accountId}/funnel
    [HttpGet("{accountId}/funnel")]
    [SwaggerOperation(Summary = "Obtener embudo de la cuenta")]
    [SwaggerResponse(200, "Embudo actual", typeof(AdminFunnelResponseDto))]
    [SwaggerResponse(404, "Sin embudo configurado")]
    public async Task<IActionResult> GetFunnel(int customerId, int accountId)
    {
        if (await GetAccount(customerId, accountId) == null)
            return NotFound(new { message = "Cuenta no encontrada." });

        var funnel = await _context.Funnels.Include(f => f.Stages)
            .FirstOrDefaultAsync(f => f.AccountId == accountId);

        if (funnel == null) return NotFound(new { message = "La cuenta no tiene embudo configurado." });
        return Ok(MapFunnel(funnel));
    }

    // PUT /api/admin/customers/{customerId}/accounts/{accountId}/funnel
    [HttpPut("{accountId}/funnel")]
    [SwaggerOperation(
        Summary = "Configurar embudo",
        Description = "Si TemplateId tiene valor, clona la plantilla. Si no, usa las etapas del array Stages.")]
    [SwaggerResponse(200, "Embudo configurado", typeof(AdminFunnelResponseDto))]
    [SwaggerResponse(400, "Se requiere TemplateId o al menos una etapa")]
    [SwaggerResponse(404, "Cuenta o plantilla no encontrada")]
    public async Task<IActionResult> SetFunnel(int customerId, int accountId, [FromBody] SetAdminFunnelDto model)
    {
        if (await GetAccount(customerId, accountId) == null)
            return NotFound(new { message = "Cuenta no encontrada." });

        if (model.TemplateId == null && !model.Stages.Any())
            return BadRequest(new { message = "Debes proporcionar un TemplateId o al menos una etapa." });

        using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            // Eliminar embudo anterior
            var existing = await _context.Funnels.Include(f => f.Stages)
                .FirstOrDefaultAsync(f => f.AccountId == accountId);
            if (existing != null)
            {
                _context.Stages.RemoveRange(existing.Stages);
                _context.Funnels.Remove(existing);
                await _context.SaveChangesAsync();
            }

            List<(string Name, int Order, string? Color)> stages;
            int? templateId = null;
            string funnelName;

            if (model.TemplateId.HasValue)
            {
                var template = await _context.FunnelTemplates.Include(t => t.Stages)
                    .FirstOrDefaultAsync(t => t.TemplateId == model.TemplateId.Value);
                if (template == null)
                    return NotFound(new { message = $"Plantilla {model.TemplateId} no encontrada." });

                templateId = template.TemplateId;
                funnelName = template.Name;
                stages = template.Stages.Select(s => (s.StageName, s.Order, (string?)null)).ToList();
            }
            else
            {
                funnelName = "Embudo personalizado";
                stages = model.Stages.Select(s => (s.Name, s.Order, s.Color)).ToList();
            }

            var funnel = new Funnel { AccountId = accountId, Name = funnelName, OriginatingTemplateId = templateId };
            _context.Funnels.Add(funnel);
            await _context.SaveChangesAsync();

            foreach (var (name, order, color) in stages)
                _context.Stages.Add(new Stage { FunnelId = funnel.FunnelId, Name = name, Order = order, Color = color });

            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            var result = await _context.Funnels.Include(f => f.Stages)
                .FirstAsync(f => f.FunnelId == funnel.FunnelId);
            return Ok(MapFunnel(result));
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return StatusCode(500, new { message = "Error al configurar el embudo.", details = ex.Message });
        }
    }

    // PUT /api/admin/customers/{customerId}/accounts/{accountId}/funnel/stages
    [HttpPut("{accountId}/funnel/stages")]
    [SwaggerOperation(Summary = "Actualizar etapas del embudo", Description = "Incluir StageId para actualizar existentes, omitirlo para crear nuevas. Etapas no incluidas se eliminan.")]
    [SwaggerResponse(200, "Etapas actualizadas", typeof(AdminFunnelResponseDto))]
    [SwaggerResponse(404, "Cuenta o embudo no encontrado")]
    public async Task<IActionResult> UpdateStages(int customerId, int accountId, [FromBody] List<AdminStageInputDto> stages)
    {
        if (await GetAccount(customerId, accountId) == null)
            return NotFound(new { message = "Cuenta no encontrada." });

        var funnel = await _context.Funnels.Include(f => f.Stages)
            .FirstOrDefaultAsync(f => f.AccountId == accountId);
        if (funnel == null) return NotFound(new { message = "La cuenta no tiene embudo. Usa PUT /funnel primero." });

        using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            var incomingIds = stages.Where(s => s.StageId.HasValue).Select(s => s.StageId!.Value).ToList();
            _context.Stages.RemoveRange(funnel.Stages.Where(s => !incomingIds.Contains(s.StageId)));

            foreach (var dto in stages)
            {
                if (dto.StageId.HasValue)
                {
                    var s = funnel.Stages.FirstOrDefault(x => x.StageId == dto.StageId.Value);
                    if (s != null) { s.Name = dto.Name; s.Order = dto.Order; s.Color = dto.Color; }
                }
                else
                {
                    _context.Stages.Add(new Stage { FunnelId = funnel.FunnelId, Name = dto.Name, Order = dto.Order, Color = dto.Color });
                }
            }

            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            var result = await _context.Funnels.Include(f => f.Stages).FirstAsync(f => f.FunnelId == funnel.FunnelId);
            return Ok(MapFunnel(result));
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return StatusCode(500, new { message = "Error al actualizar etapas.", details = ex.Message });
        }
    }

    // ════════════════════════════════════════════════════════════
    // SCORING
    // ════════════════════════════════════════════════════════════

    // GET /api/admin/customers/{customerId}/accounts/{accountId}/scoring
    [HttpGet("{accountId}/scoring")]
    [SwaggerOperation(Summary = "Obtener modelo de calificación de la cuenta")]
    [SwaggerResponse(200, "Modelo de calificación", typeof(AdminScoringResponseDto))]
    [SwaggerResponse(404, "Sin modelo configurado")]
    public async Task<IActionResult> GetScoring(int customerId, int accountId)
    {
        if (await GetAccount(customerId, accountId) == null)
            return NotFound(new { message = "Cuenta no encontrada." });

        var model = await _context.ScoringModels
            .Include(sm => sm.Questions).ThenInclude(q => q.AnswerOptions)
            .FirstOrDefaultAsync(sm => sm.AccountId == accountId);
        if (model == null) return NotFound(new { message = "La cuenta no tiene modelo de calificación." });

        var tiers = await _context.LeadTiers.Where(t => t.ScoringModelId == model.ScoringModelId).ToListAsync();
        return Ok(MapScoring(model, tiers));
    }

    // PUT /api/admin/customers/{customerId}/accounts/{accountId}/scoring
    [HttpPut("{accountId}/scoring")]
    [SwaggerOperation(
        Summary = "Configurar modelo de calificación",
        Description = "Clona el ScoringTemplate indicado. Crea 3 tiers por defecto (Frío/Tibio/Caliente).")]
    [SwaggerResponse(200, "Modelo configurado", typeof(AdminScoringResponseDto))]
    [SwaggerResponse(404, "Cuenta o template no encontrado")]
    public async Task<IActionResult> SetScoring(int customerId, int accountId, [FromBody] SetAdminScoringDto model)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (await GetAccount(customerId, accountId) == null)
            return NotFound(new { message = "Cuenta no encontrada." });

        if (!model.TemplateId.HasValue)
            return BadRequest(new { message = "Se requiere un TemplateId para configurar el scoring." });

        var template = await _context.ScoringTemplates
            .Include(t => t.Questions).ThenInclude(q => q.AnswerOptions)
            .FirstOrDefaultAsync(t => t.TemplateId == model.TemplateId.Value);
        if (template == null)
            return NotFound(new { message = $"ScoringTemplate {model.TemplateId} no encontrado." });

        using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            // Eliminar modelo anterior
            var existing = await _context.ScoringModels
                .Include(sm => sm.Questions).ThenInclude(q => q.AnswerOptions)
                .FirstOrDefaultAsync(sm => sm.AccountId == accountId);
            if (existing != null)
            {
                foreach (var q in existing.Questions)
                    _context.ScoringAnswerOptions.RemoveRange(q.AnswerOptions);
                _context.ScoringQuestions.RemoveRange(existing.Questions);
                var oldTiers = await _context.LeadTiers.Where(t => t.ScoringModelId == existing.ScoringModelId).ToListAsync();
                _context.LeadTiers.RemoveRange(oldTiers);
                _context.ScoringModels.Remove(existing);
                await _context.SaveChangesAsync();
            }

            // Clonar template
            var scoringModel = new ScoringModel { AccountId = accountId, Name = model.ModelName };
            _context.ScoringModels.Add(scoringModel);
            await _context.SaveChangesAsync();

            foreach (var qDto in template.Questions)
            {
                var question = new ScoringQuestion
                {
                    ScoringModelId = scoringModel.ScoringModelId,
                    QuestionText = qDto.QuestionText,
                    QuestionType = qDto.QuestionType,
                    IsRequired = qDto.IsRequired,
                    OrderPosition = qDto.OrderPosition
                };
                _context.ScoringQuestions.Add(question);
                await _context.SaveChangesAsync();

                foreach (var aDto in qDto.AnswerOptions)
                    _context.ScoringAnswerOptions.Add(new ScoringAnswerOption
                    {
                        QuestionId = question.QuestionId,
                        AnswerText = aDto.AnswerText,
                        Points = aDto.Points,
                        OrderPosition = aDto.OrderPosition
                    });
            }

            // Tiers por defecto
            var defaultTiers = new List<(string Name, decimal Min, decimal? Max, string Color)>
            {
                ("Frío",     0,  39,   "#3498db"),
                ("Tibio",    40, 69,   "#f39c12"),
                ("Caliente", 70, null, "#e74c3c")
            };
            foreach (var (name, min, max, color) in defaultTiers)
                _context.LeadTiers.Add(new LeadTier
                {
                    ScoringModelId = scoringModel.ScoringModelId,
                    Name = name, MinScore = min, MaxScore = max, Color = color
                });

            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            var result = await _context.ScoringModels
                .Include(sm => sm.Questions).ThenInclude(q => q.AnswerOptions)
                .FirstAsync(sm => sm.ScoringModelId == scoringModel.ScoringModelId);
            var tiers = await _context.LeadTiers.Where(t => t.ScoringModelId == scoringModel.ScoringModelId).ToListAsync();
            return Ok(MapScoring(result, tiers, model.TemplateId));
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return StatusCode(500, new { message = "Error al configurar scoring.", details = ex.Message });
        }
    }

    // ════════════════════════════════════════════════════════════
    // INDUSTRIAS
    // ════════════════════════════════════════════════════════════

    // PUT /api/admin/customers/{customerId}/accounts/{accountId}/industries
    [HttpPut("{accountId}/industries")]
    [SwaggerOperation(Summary = "Asignar industrias a la cuenta", Description = "Reemplaza las industrias actuales.")]
    [SwaggerResponse(200, "Industrias actualizadas")]
    [SwaggerResponse(404, "Cuenta no encontrada")]
    public async Task<IActionResult> SetIndustries(int customerId, int accountId, [FromBody] AdminAccountIndustriesDto model)
    {
        if (await GetAccount(customerId, accountId) == null)
            return NotFound(new { message = "Cuenta no encontrada." });

        var existing = await _context.AccountIndustries.Where(ai => ai.AccountId == accountId).ToListAsync();
        _context.AccountIndustries.RemoveRange(existing);

        foreach (var id in model.IndustryIds)
            _context.AccountIndustries.Add(new AccountIndustry { AccountId = accountId, IndustryId = id });

        await _context.SaveChangesAsync();
        return Ok(new { message = $"{model.IndustryIds.Count} industria(s) asignada(s)." });
    }

    // GET /api/admin/customers/{customerId}/accounts/{accountId}/industries
    [HttpGet("{accountId}/industries")]
    [SwaggerOperation(Summary = "Obtener industrias asignadas a la cuenta")]
    [SwaggerResponse(200, "Lista de IDs asignados")]
    public async Task<IActionResult> GetIndustries(int customerId, int accountId)
    {
        if (await GetAccount(customerId, accountId) == null)
            return NotFound(new { message = "Cuenta no encontrada." });

        var ids = await _context.AccountIndustries
            .Where(ai => ai.AccountId == accountId)
            .Select(ai => ai.IndustryId)
            .ToListAsync();

        return Ok(ids);
    }

    // ════════════════════════════════════════════════════════════
    // CATÁLOGOS (MOTIVOS DE PÉRDIDA)
    // ════════════════════════════════════════════════════════════

    // GET /api/admin/customers/{customerId}/accounts/{accountId}/catalogs
    [HttpGet("{accountId}/catalogs")]
    [SwaggerOperation(Summary = "Obtener motivos de pérdida activos de la cuenta")]
    [SwaggerResponse(200, "Catálogo de la cuenta", typeof(AdminCatalogsResponseDto))]
    public async Task<IActionResult> GetCatalogs(int customerId, int accountId)
    {
        if (await GetAccount(customerId, accountId) == null)
            return NotFound(new { message = "Cuenta no encontrada." });

        var reasons = await _context.LeadLostReasons
            .Where(r => r.AccountId == accountId && r.IsActive)
            .Select(r => new AdminLostReasonItemDto { Id = r.LostReasonId, Description = r.Description })
            .ToListAsync();

        return Ok(new AdminCatalogsResponseDto { LostReasons = reasons });
    }

    // PUT /api/admin/customers/{customerId}/accounts/{accountId}/catalogs
    [HttpPut("{accountId}/catalogs")]
    [SwaggerOperation(Summary = "Configurar motivos de pérdida de la cuenta", Description = "Reemplaza los motivos asignados con los del array LostReasonIds.")]
    [SwaggerResponse(200, "Catálogos actualizados")]
    [SwaggerResponse(404, "Cuenta no encontrada")]
    public async Task<IActionResult> SetCatalogs(int customerId, int accountId, [FromBody] SetAdminCatalogsDto model)
    {
        if (await GetAccount(customerId, accountId) == null)
            return NotFound(new { message = "Cuenta no encontrada." });

        var templates = await _context.LeadLostReasonTemplates
            .Where(t => model.TemplateIds.Contains(t.TemplateId))
            .ToListAsync();

        var existing = await _context.LeadLostReasons.Where(r => r.AccountId == accountId).ToListAsync();
        _context.LeadLostReasons.RemoveRange(existing);

        foreach (var t in templates)
            _context.LeadLostReasons.Add(new LeadLostReason { AccountId = accountId, Description = t.Description, CountsForCharts = t.CountsForCharts });

        await _context.SaveChangesAsync();
        return Ok(new { message = $"{templates.Count} motivo(s) configurado(s)." });
    }

    // ════════════════════════════════════════════════════════════
    // USUARIOS
    // ════════════════════════════════════════════════════════════

    // GET /api/admin/customers/{customerId}/accounts/{accountId}/users
    [HttpGet("{accountId}/users")]
    [SwaggerOperation(Summary = "Listar usuarios asignados a la cuenta")]
    [SwaggerResponse(200, "Lista de usuarios", typeof(List<AdminAccountUserResponseDto>))]
    public async Task<IActionResult> GetUsers(int customerId, int accountId)
    {
        if (await GetAccount(customerId, accountId) == null)
            return NotFound(new { message = "Cuenta no encontrada." });

        var assignments = await _context.AccountInternalUsers
            .Where(aiu => aiu.AccountId == accountId)
            .Include(aiu => aiu.User).ThenInclude(u => u.UserProfile)
            .ToListAsync();

        var result = new List<AdminAccountUserResponseDto>();
        foreach (var aiu in assignments)
        {
            var roles = await _userManager.GetRolesAsync(aiu.User);
            result.Add(new AdminAccountUserResponseDto
            {
                UserId = aiu.UserId,
                Email = aiu.User.Email ?? "",
                FullName = $"{aiu.User.UserProfile?.FirstName} {aiu.User.UserProfile?.LastName}".Trim(),
                Role = roles.FirstOrDefault() ?? "SalesRep",
                Active = aiu.User.Active ?? false,
                RoleInAccount = aiu.RoleInAccount
            });
        }
        return Ok(result);
    }

    // POST /api/admin/customers/{customerId}/accounts/{accountId}/users
    [HttpPost("{accountId}/users")]
    [SwaggerOperation(Summary = "Asignar usuario existente a la cuenta")]
    [SwaggerResponse(200, "Usuario asignado")]
    [SwaggerResponse(400, "Ya está asignado")]
    [SwaggerResponse(404, "Cuenta o usuario no encontrado")]
    public async Task<IActionResult> AssignUser(int customerId, int accountId, [FromBody] AssignAdminUserDto model)
    {
        if (await GetAccount(customerId, accountId) == null)
            return NotFound(new { message = "Cuenta no encontrada." });

        var userExists = await _context.Users.AnyAsync(u => u.Id == model.UserId && u.CustomerId == customerId);
        if (!userExists) return NotFound(new { message = "Usuario no encontrado en este cliente." });

        var alreadyAssigned = await _context.AccountInternalUsers
            .AnyAsync(aiu => aiu.AccountId == accountId && aiu.UserId == model.UserId);
        if (alreadyAssigned) return BadRequest(new { message = "El usuario ya está asignado a esta cuenta." });

        _context.AccountInternalUsers.Add(new AccountInternalUser
        {
            AccountId = accountId,
            UserId = model.UserId,
            RoleInAccount = model.RoleInAccount
        });
        await _context.SaveChangesAsync();
        return Ok(new { message = "Usuario asignado correctamente." });
    }

    // DELETE /api/admin/customers/{customerId}/accounts/{accountId}/users/{userId}
    [HttpDelete("{accountId}/users/{userId}")]
    [SwaggerOperation(Summary = "Desasignar usuario de la cuenta")]
    [SwaggerResponse(204, "Desasignado")]
    [SwaggerResponse(404, "Asignación no encontrada")]
    public async Task<IActionResult> UnassignUser(int customerId, int accountId, string userId)
    {
        var assignment = await _context.AccountInternalUsers
            .FirstOrDefaultAsync(aiu => aiu.AccountId == accountId && aiu.UserId == userId);
        if (assignment == null) return NotFound(new { message = "El usuario no está asignado a esta cuenta." });

        _context.AccountInternalUsers.Remove(assignment);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
