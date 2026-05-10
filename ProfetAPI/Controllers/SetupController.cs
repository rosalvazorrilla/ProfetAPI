using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Dtos;
using ProfetAPI.Models;
using Swashbuckle.AspNetCore.Annotations;

namespace ProfetAPI.Controllers
{
    /// <summary>
    /// Wizard de configuración inicial del cliente.
    /// Autenticación por token (query param ?token=...) — sin JWT.
    /// Flujo: Cuentas → Usuarios → Equipos → Preview → Completar
    /// </summary>
    [Route("api/setup")]
    [ApiController]
    [SwaggerTag("Setup Wizard — Configuración inicial del cliente (token-based)")]
    public class SetupController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly string _frontendLoginUrl = "http://localhost:3000/login";

        public SetupController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // ════════════════════════════════════════════════════════════
        // HELPER — Validar token y obtener Customer
        // ════════════════════════════════════════════════════════════

        private async Task<Customer?> GetCustomerByToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;
            return await _context.Customers
                .FirstOrDefaultAsync(c => c.SetupToken == token && c.Deleted == false);
        }

        private async Task<bool> AccountBelongsToCustomer(int accountId, int customerId)
        {
            return await _context.Accounts.AnyAsync(a => a.AccountId == accountId && a.CustomerId == customerId);
        }

        private async Task<bool> UserBelongsToCustomer(string userId, int customerId)
        {
            return await _context.Users.AnyAsync(u => u.Id == userId && u.CustomerId == customerId);
        }

        // ════════════════════════════════════════════════════════════
        // STATUS / CHECKLIST
        // GET /api/setup/status?token=
        // ════════════════════════════════════════════════════════════

        // PUT /api/setup/step?token=&step=
        [HttpPut("step")]
        [SwaggerOperation(Summary = "Guardar paso actual del wizard", Description = "Persiste el paso actual en Customer.SetupStep para re-entrada.")]
        [SwaggerResponse(204, "Paso guardado")]
        [SwaggerResponse(401, "Token inválido")]
        public async Task<IActionResult> SaveStep([FromQuery] string token, [FromQuery] int step)
        {
            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });
            customer.SetupStep = step;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        [HttpGet("status")]
        [SwaggerOperation(
            Summary = "Estado del wizard",
            Description = "Devuelve el checklist de configuración derivado de los datos reales: cuentas, embudo, variables, scoring, catálogos y usuarios asignados. También indica si CanComplete."
        )]
        [SwaggerResponse(200, "Estado actual del setup", typeof(SetupStatusDto))]
        [SwaggerResponse(401, "Token inválido o expirado")]
        public async Task<IActionResult> GetStatus([FromQuery] string token)
        {
            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido o expirado." });

            // Obtener subscription/plan info
            var subscription = await _context.Subscriptions
                .Include(s => s.Plan)
                .FirstOrDefaultAsync(s => s.CustomerId == customer.Id && s.Status != "Canceled");

            // Cuentas con su checklist derivado de datos
            var accounts = await _context.Accounts
                .Where(a => a.CustomerId == customer.Id)
                .Include(a => a.Funnels).ThenInclude(f => f.Stages)
                .Include(a => a.InternalUsers)
                .ToListAsync();

            var accountIndustries = await _context.AccountIndustries
                .Where(ai => accounts.Select(a => a.AccountId).Contains(ai.AccountId))
                .ToListAsync();

            var accountCustomFields = await _context.AccountCustomFields
                .Where(acf => accounts.Select(a => a.AccountId).Contains(acf.AccountId))
                .ToListAsync();

            var scoringModels = await _context.ScoringModels
                .Include(sm => sm.Questions)
                .Where(sm => accounts.Select(a => a.AccountId).Contains(sm.AccountId))
                .ToListAsync();

            var accountLostReasons = await _context.LeadLostReasons
                .Where(r => accounts.Select(a => a.AccountId).Contains(r.AccountId) && r.IsActive)
                .ToListAsync();

            var accountChecklist = accounts.Select(a =>
            {
                var funnel = a.Funnels.FirstOrDefault();
                var scoring = scoringModels.FirstOrDefault(sm => sm.AccountId == a.AccountId);
                var lostReasonCount = accountLostReasons.Count(r => r.AccountId == a.AccountId);
                var fieldCount = accountCustomFields.Count(acf => acf.AccountId == a.AccountId);
                var userCount = a.InternalUsers.Count;
                var industryCount = accountIndustries.Count(ai => ai.AccountId == a.AccountId);

                return new SetupAccountChecklistDto
                {
                    AccountId = a.AccountId,
                    Name = a.Name,
                    Funnel = new SetupChecklistItem
                    {
                        Done = funnel != null && funnel.Stages.Any(),
                        Detail = funnel != null ? $"{funnel.Stages.Count} etapas" : "Sin embudo"
                    },
                    Variables = new SetupChecklistItem
                    {
                        Done = fieldCount > 0,
                        Detail = $"{fieldCount} variable(s) activa(s)"
                    },
                    Scoring = new SetupChecklistItem
                    {
                        Done = scoring != null && scoring.Questions.Any(),
                        Detail = scoring != null ? $"{scoring.Questions.Count} pregunta(s)" : "Sin modelo"
                    },
                    Catalogs = new SetupChecklistItem
                    {
                        Done = lostReasonCount > 0,
                        Detail = $"{lostReasonCount} motivo(s) de pérdida"
                    },
                    Users = new SetupChecklistItem
                    {
                        Done = userCount > 0,
                        Detail = $"{userCount} usuario(s) asignado(s)"
                    }
                };
            }).ToList();

            // Usuarios del customer
            var users = await _context.Users
                .Include(u => u.UserProfile)
                .Where(u => u.CustomerId == customer.Id && u.Deleted == false)
                .ToListAsync();

            var userAssignments = await _context.AccountInternalUsers
                .Where(aiu => aiu.User.CustomerId == customer.Id)
                .ToListAsync();

            var usersSummary = users.Select(u => new SetupUserSummaryDto
            {
                UserId = u.Id,
                FullName = $"{u.UserProfile?.FirstName} {u.UserProfile?.LastName}".Trim(),
                Email = u.Email ?? "",
                Role = _userManager.GetRolesAsync(u).Result.FirstOrDefault() ?? "SalesRep",
                AccountsAssigned = userAssignments
                    .Where(aiu => aiu.UserId == u.Id)
                    .Select(aiu => aiu.AccountId)
                    .ToList()
            }).ToList();

            // Equipos
            var teams = await _context.Teams
                .Include(t => t.UserTeams)
                .Where(t => t.CustomerId == customer.Id)
                .ToListAsync();

            var teamsSummary = teams.Select(t => new SetupTeamSummaryDto
            {
                TeamId = t.Id,
                Name = t.Name,
                MemberCount = t.UserTeams.Count
            }).ToList();

            // Validación básica para CanComplete
            var errors = new List<string>();
            var warnings = new List<string>();

            if (!accounts.Any())
                errors.Add("Debes crear al menos una cuenta.");
            else
            {
                foreach (var a in accountChecklist)
                {
                    if (!a.Funnel.Done)
                        errors.Add($"La cuenta '{a.Name}' no tiene embudo configurado.");
                    if (!a.Scoring.Done)
                        warnings.Add($"La cuenta '{a.Name}' no tiene modelo de calificación.");
                }
            }

            if (!users.Any())
                warnings.Add("No has creado ningún usuario.");

            var canComplete = !errors.Any();

            return Ok(new SetupStatusDto
            {
                CustomerId = customer.Id,
                CustomerName = customer.Name,
                PlanName = subscription?.Plan.Name ?? "Sin plan",
                CurrentStep = customer.SetupStep,
                CanComplete = canComplete,
                Accounts = accountChecklist,
                Users = usersSummary,
                Teams = teamsSummary,
                Errors = errors,
                Warnings = warnings
            });
        }

        // ════════════════════════════════════════════════════════════
        // CUENTAS (ACCOUNTS)
        // ════════════════════════════════════════════════════════════

        // GET /api/setup/accounts?token=
        [HttpGet("accounts")]
        [SwaggerOperation(Summary = "Listar cuentas del wizard", Description = "Retorna todas las cuentas (Borrador y Activo) del customer.")]
        [SwaggerResponse(200, "Lista de cuentas", typeof(List<SetupAccountResponseDto>))]
        [SwaggerResponse(401, "Token inválido")]
        public async Task<IActionResult> GetAccounts([FromQuery] string token)
        {
            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });

            var accounts = await _context.Accounts
                .Where(a => a.CustomerId == customer.Id)
                .Select(a => new SetupAccountResponseDto
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

        // POST /api/setup/accounts?token=
        [HttpPost("accounts")]
        [SwaggerOperation(Summary = "Crear cuenta", Description = "Crea una nueva cuenta en estado 'Borrador'. Puede crearse múltiples cuentas.")]
        [SwaggerResponse(201, "Cuenta creada", typeof(SetupAccountResponseDto))]
        [SwaggerResponse(401, "Token inválido")]
        public async Task<IActionResult> CreateAccount([FromQuery] string token, [FromBody] CreateSetupAccountDto model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });

            var account = new Account
            {
                CustomerId = customer.Id,
                Name = model.Name,
                Description = model.Description,
                AssignmentType = model.AssignmentType,
                AssignmentUserId = model.AssignmentUserId,
                Status = "Borrador",
                CreatedOn = DateTime.UtcNow
            };

            _context.Accounts.Add(account);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetAccounts), new { token },
                new SetupAccountResponseDto
                {
                    AccountId = account.AccountId,
                    Name = account.Name,
                    Description = account.Description,
                    Status = account.Status,
                    AssignmentType = account.AssignmentType ?? "Carrusel"
                });
        }

        // PUT /api/setup/accounts/{accountId}?token=
        [HttpPut("accounts/{accountId}")]
        [SwaggerOperation(Summary = "Actualizar datos básicos de la cuenta")]
        [SwaggerResponse(200, "Cuenta actualizada", typeof(SetupAccountResponseDto))]
        [SwaggerResponse(401, "Token inválido")]
        [SwaggerResponse(404, "Cuenta no encontrada")]
        public async Task<IActionResult> UpdateAccount([FromQuery] string token, int accountId, [FromBody] UpdateSetupAccountDto model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });

            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.AccountId == accountId && a.CustomerId == customer.Id);
            if (account == null) return NotFound(new { message = "Cuenta no encontrada." });

            account.Name = model.Name;
            account.Description = model.Description;
            account.AssignmentType = model.AssignmentType;
            account.AssignmentUserId = model.AssignmentUserId;
            await _context.SaveChangesAsync();

            return Ok(new SetupAccountResponseDto
            {
                AccountId = account.AccountId,
                Name = account.Name,
                Description = account.Description,
                Status = account.Status,
                AssignmentType = account.AssignmentType ?? "Carrusel"
            });
        }

        // DELETE /api/setup/accounts/{accountId}?token=
        [HttpDelete("accounts/{accountId}")]
        [SwaggerOperation(Summary = "Eliminar cuenta (solo en Borrador)")]
        [SwaggerResponse(204, "Cuenta eliminada")]
        [SwaggerResponse(400, "No se puede eliminar una cuenta ya activa")]
        [SwaggerResponse(401, "Token inválido")]
        [SwaggerResponse(404, "Cuenta no encontrada")]
        public async Task<IActionResult> DeleteAccount([FromQuery] string token, int accountId)
        {
            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });

            var account = await _context.Accounts.FirstOrDefaultAsync(a => a.AccountId == accountId && a.CustomerId == customer.Id);
            if (account == null) return NotFound(new { message = "Cuenta no encontrada." });
            if (account.Status == "Activo") return BadRequest(new { message = "No se puede eliminar una cuenta activa." });

            _context.Accounts.Remove(account);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // ════════════════════════════════════════════════════════════
        // INDUSTRIAS
        // ════════════════════════════════════════════════════════════

        [HttpGet("industry-templates")]
        [SwaggerOperation(Summary = "Listar industrias disponibles")]
        [SwaggerResponse(200, "Lista de industrias")]
        public async Task<IActionResult> GetIndustryTemplates([FromQuery] string token)
        {
            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });

            var list = await _context.Industries
                .OrderBy(i => i.NameES)
                .Select(i => new { i.Id, Name = i.NameES ?? i.NameEN })
                .ToListAsync();
            return Ok(list);
        }

        [HttpGet("accounts/{accountId}/industries")]
        [SwaggerOperation(Summary = "Industrias asignadas a la cuenta")]
        [SwaggerResponse(200, "Lista de industrias de la cuenta")]
        public async Task<IActionResult> GetIndustries([FromQuery] string token, int accountId)
        {
            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });
            if (!await AccountBelongsToCustomer(accountId, customer.Id))
                return NotFound(new { message = "Cuenta no encontrada." });

            var selected = await _context.AccountIndustries
                .Where(ai => ai.AccountId == accountId)
                .Select(ai => ai.IndustryId)
                .ToListAsync();
            return Ok(new { selectedIds = selected });
        }

        [HttpPut("accounts/{accountId}/industries")]
        [SwaggerOperation(Summary = "Asignar sectores/industrias a la cuenta", Description = "Reemplaza las industrias actuales de la cuenta con las proporcionadas.")]
        [SwaggerResponse(200, "Industrias actualizadas")]
        [SwaggerResponse(401, "Token inválido")]
        [SwaggerResponse(404, "Cuenta no encontrada")]
        public async Task<IActionResult> SetIndustries([FromQuery] string token, int accountId, [FromBody] SetupAccountIndustriesDto model)
        {
            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });
            if (!await AccountBelongsToCustomer(accountId, customer.Id))
                return NotFound(new { message = "Cuenta no encontrada." });

            // Reemplazar
            var existing = await _context.AccountIndustries.Where(ai => ai.AccountId == accountId).ToListAsync();
            _context.AccountIndustries.RemoveRange(existing);

            foreach (var industryId in model.IndustryIds)
            {
                _context.AccountIndustries.Add(new AccountIndustry
                {
                    AccountId = accountId,
                    IndustryId = industryId
                });
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = $"{model.IndustryIds.Count} industria(s) asignada(s)." });
        }

        // ════════════════════════════════════════════════════════════
        // FUENTES DE PROSPECTOS
        // ════════════════════════════════════════════════════════════

        [HttpGet("prospect-source-templates")]
        [SwaggerOperation(Summary = "Listar fuentes de prospectos disponibles")]
        [SwaggerResponse(200, "Lista de fuentes")]
        public async Task<IActionResult> GetProspectSourceTemplates([FromQuery] string token)
        {
            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });

            var list = await _context.ProspectSources
                .OrderBy(s => s.Name)
                .Select(s => new { s.SourceId, s.Name })
                .ToListAsync();
            return Ok(list);
        }

        [HttpGet("accounts/{accountId}/prospect-sources")]
        [SwaggerOperation(Summary = "Fuentes de prospectos asignadas a la cuenta")]
        [SwaggerResponse(200, "Lista de fuentes de la cuenta")]
        public async Task<IActionResult> GetAccountProspectSources([FromQuery] string token, int accountId)
        {
            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });
            if (!await AccountBelongsToCustomer(accountId, customer.Id))
                return NotFound(new { message = "Cuenta no encontrada." });

            var selected = await _context.AccountProspectSources
                .Where(s => s.AccountId == accountId)
                .Select(s => s.SourceId)
                .ToListAsync();
            return Ok(new { selectedIds = selected });
        }

        [HttpPut("accounts/{accountId}/prospect-sources")]
        [SwaggerOperation(Summary = "Configurar fuentes de prospectos de la cuenta")]
        [SwaggerResponse(200, "Fuentes actualizadas")]
        public async Task<IActionResult> SetAccountProspectSources([FromQuery] string token, int accountId, [FromBody] SetupProspectSourcesDto model)
        {
            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });
            if (!await AccountBelongsToCustomer(accountId, customer.Id))
                return NotFound(new { message = "Cuenta no encontrada." });

            var existing = await _context.AccountProspectSources.Where(s => s.AccountId == accountId).ToListAsync();
            _context.AccountProspectSources.RemoveRange(existing);

            foreach (var sourceId in model.SourceIds)
                _context.AccountProspectSources.Add(new AccountProspectSource { AccountId = accountId, SourceId = sourceId });

            await _context.SaveChangesAsync();
            return Ok(new { message = $"{model.SourceIds.Count} fuente(s) configurada(s)." });
        }

        // ════════════════════════════════════════════════════════════
        // EMBUDO
        // ════════════════════════════════════════════════════════════

        // GET /api/setup/funnel-templates?token=
        [HttpGet("funnel-templates")]
        [SwaggerOperation(Summary = "Listar plantillas de embudo disponibles")]
        [SwaggerResponse(200, "Lista de plantillas")]
        [SwaggerResponse(401, "Token inválido")]
        public async Task<IActionResult> GetFunnelTemplates([FromQuery] string token)
        {
            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });

            var templates = await _context.FunnelTemplates
                .Include(ft => ft.Stages)
                .OrderBy(ft => ft.TemplateId)
                .Select(ft => new
                {
                    ft.TemplateId,
                    ft.Name,
                    ft.Description,
                    Stages = ft.Stages.OrderBy(s => s.Order).Select(s => new
                    {
                        s.TemplateStageId,
                        s.StageName,
                        s.Order
                    }).ToList()
                })
                .ToListAsync();

            return Ok(templates);
        }

        // GET /api/setup/scoring-templates?token=
        [HttpGet("scoring-templates")]
        [SwaggerOperation(Summary = "Listar plantillas de calificación disponibles")]
        [SwaggerResponse(200, "Lista de plantillas de scoring con sus preguntas y respuestas")]
        [SwaggerResponse(401, "Token inválido")]
        public async Task<IActionResult> GetScoringTemplates([FromQuery] string token)
        {
            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });

            var templates = await _context.ScoringTemplates
                .Include(t => t.Questions.OrderBy(q => q.OrderPosition))
                    .ThenInclude(q => q.AnswerOptions.OrderBy(a => a.OrderPosition))
                .OrderBy(t => t.TemplateId)
                .Select(t => new
                {
                    t.TemplateId,
                    t.Name,
                    t.Description,
                    QuestionCount = t.Questions.Count,
                    Questions = t.Questions.OrderBy(q => q.OrderPosition).Select(q => new
                    {
                        q.TemplateQuestionId,
                        q.QuestionText,
                        q.QuestionType,
                        q.IsRequired,
                        q.OrderPosition,
                        AnswerOptions = q.AnswerOptions.OrderBy(a => a.OrderPosition).Select(a => new
                        {
                            TemplateAnswerOptionId = a.TemplateAnswerId,
                            a.AnswerText,
                            a.Points,
                            a.OrderPosition
                        })
                    })
                })
                .ToListAsync();

            return Ok(templates);
        }

        // GET /api/setup/accounts/{accountId}/funnel?token=
        [HttpGet("accounts/{accountId}/funnel")]
        [SwaggerOperation(Summary = "Obtener embudo de la cuenta")]
        [SwaggerResponse(200, "Embudo actual", typeof(SetupFunnelResponseDto))]
        [SwaggerResponse(404, "Cuenta o embudo no encontrado")]
        public async Task<IActionResult> GetFunnel([FromQuery] string token, int accountId)
        {
            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });
            if (!await AccountBelongsToCustomer(accountId, customer.Id))
                return NotFound(new { message = "Cuenta no encontrada." });

            var funnel = await _context.Funnels
                .Include(f => f.Stages)
                .FirstOrDefaultAsync(f => f.AccountId == accountId);

            if (funnel == null) return NotFound(new { message = "La cuenta aún no tiene embudo." });

            return Ok(MapFunnelToDto(funnel));
        }

        // PUT /api/setup/accounts/{accountId}/funnel?token=
        [HttpPut("accounts/{accountId}/funnel")]
        [SwaggerOperation(
            Summary = "Configurar embudo de la cuenta",
            Description = "Si se proporciona TemplateId, clona la plantilla. Si no, usa las etapas personalizadas del array Stages."
        )]
        [SwaggerResponse(200, "Embudo configurado", typeof(SetupFunnelResponseDto))]
        [SwaggerResponse(400, "Se requiere TemplateId o al menos una etapa personalizada")]
        [SwaggerResponse(404, "Cuenta o plantilla no encontrada")]
        public async Task<IActionResult> SetFunnel([FromQuery] string token, int accountId, [FromBody] SetupFunnelDto model)
        {
            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });
            if (!await AccountBelongsToCustomer(accountId, customer.Id))
                return NotFound(new { message = "Cuenta no encontrada." });

            if (model.TemplateId == null && !model.Stages.Any())
                return BadRequest(new { message = "Debes proporcionar un TemplateId o al menos una etapa personalizada." });

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Eliminar embudo anterior si existe
                var existingFunnel = await _context.Funnels.Include(f => f.Stages)
                    .FirstOrDefaultAsync(f => f.AccountId == accountId);
                if (existingFunnel != null)
                {
                    _context.Stages.RemoveRange(existingFunnel.Stages);
                    _context.Funnels.Remove(existingFunnel);
                    await _context.SaveChangesAsync();
                }

                List<SetupStageDto> stages;
                int? originatingTemplateId = null;
                string funnelName;

                if (model.TemplateId.HasValue)
                {
                    var template = await _context.FunnelTemplates
                        .Include(ft => ft.Stages)
                        .FirstOrDefaultAsync(ft => ft.TemplateId == model.TemplateId.Value);

                    if (template == null)
                        return NotFound(new { message = $"Plantilla de embudo {model.TemplateId} no encontrada." });

                    originatingTemplateId = template.TemplateId;
                    funnelName = template.Name;
                    stages = template.Stages.Select(s => new SetupStageDto
                    {
                        Name = s.StageName,
                        Order = s.Order,
                        Color = null  // FunnelTemplateStage no tiene color
                    }).ToList();
                }
                else
                {
                    funnelName = "Embudo personalizado";
                    stages = model.Stages;
                }

                var funnel = new Funnel
                {
                    AccountId = accountId,
                    Name = funnelName,
                    OriginatingTemplateId = originatingTemplateId
                };
                _context.Funnels.Add(funnel);
                await _context.SaveChangesAsync();

                foreach (var s in stages)
                {
                    _context.Stages.Add(new Stage
                    {
                        FunnelId = funnel.FunnelId,
                        Name = s.Name,
                        Order = s.Order,
                        Color = s.Color
                    });
                }
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // Recargar con includes
                var result = await _context.Funnels.Include(f => f.Stages)
                    .FirstAsync(f => f.FunnelId == funnel.FunnelId);
                return Ok(MapFunnelToDto(result));
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { message = "Error al configurar el embudo.", details = ex.Message });
            }
        }

        // PUT /api/setup/accounts/{accountId}/funnel/stages?token=
        [HttpPut("accounts/{accountId}/funnel/stages")]
        [SwaggerOperation(
            Summary = "Actualizar etapas del embudo",
            Description = "Reemplaza las etapas del embudo. Incluir StageId para actualizar existentes, omitirlo para crear nuevas. Etapas existentes no incluidas serán eliminadas."
        )]
        [SwaggerResponse(200, "Etapas actualizadas", typeof(SetupFunnelResponseDto))]
        [SwaggerResponse(404, "Cuenta o embudo no encontrado")]
        public async Task<IActionResult> UpdateFunnelStages([FromQuery] string token, int accountId, [FromBody] UpdateSetupFunnelStagesDto model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });
            if (!await AccountBelongsToCustomer(accountId, customer.Id))
                return NotFound(new { message = "Cuenta no encontrada." });

            var funnel = await _context.Funnels.Include(f => f.Stages)
                .FirstOrDefaultAsync(f => f.AccountId == accountId);
            if (funnel == null) return NotFound(new { message = "La cuenta no tiene embudo. Usa PUT /funnel primero." });

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var incomingIds = model.Stages.Where(s => s.StageId.HasValue).Select(s => s.StageId!.Value).ToList();

                // Eliminar las que no vienen
                var toRemove = funnel.Stages.Where(s => !incomingIds.Contains(s.StageId)).ToList();
                _context.Stages.RemoveRange(toRemove);

                foreach (var stageDto in model.Stages)
                {
                    if (stageDto.StageId.HasValue)
                    {
                        var existing = funnel.Stages.FirstOrDefault(s => s.StageId == stageDto.StageId.Value);
                        if (existing != null)
                        {
                            existing.Name = stageDto.Name;
                            existing.Order = stageDto.Order;
                            existing.Color = stageDto.Color;
                        }
                    }
                    else
                    {
                        _context.Stages.Add(new Stage
                        {
                            FunnelId = funnel.FunnelId,
                            Name = stageDto.Name,
                            Order = stageDto.Order,
                            Color = stageDto.Color
                        });
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                var result = await _context.Funnels.Include(f => f.Stages)
                    .FirstAsync(f => f.FunnelId == funnel.FunnelId);
                return Ok(MapFunnelToDto(result));
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { message = "Error al actualizar etapas.", details = ex.Message });
            }
        }

        // ════════════════════════════════════════════════════════════
        // VARIABLES (CUSTOM FIELDS)
        // ════════════════════════════════════════════════════════════

        // GET /api/setup/accounts/{accountId}/variables?token=
        [HttpGet("accounts/{accountId}/variables")]
        [SwaggerOperation(
            Summary = "Todas las variables disponibles para la cuenta",
            Description = "Devuelve el pool global de CustomFieldDefinitions con isActive=true para las que ya están activadas en esta cuenta y isVisibleOnCard según su configuración."
        )]
        [SwaggerResponse(200, "Lista completa de variables con estado de activación")]
        [SwaggerResponse(404, "Cuenta no encontrada")]
        public async Task<IActionResult> GetVariables([FromQuery] string token, int accountId)
        {
            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });
            if (!await AccountBelongsToCustomer(accountId, customer.Id))
                return NotFound(new { message = "Cuenta no encontrada." });

            // Pool global de variables (excluye campos de sistema)
            var allFields = await _context.CustomFieldDefinitions
                .Where(f => !f.IsSystem)
                .OrderBy(f => f.FieldName)
                .ToListAsync();

            // Variables ya activadas para esta cuenta
            var activeMap = await _context.AccountCustomFields
                .Where(acf => acf.AccountId == accountId)
                .ToDictionaryAsync(acf => acf.FieldId);

            var result = allFields.Select(f => new
            {
                f.FieldId,
                f.FieldCode,
                f.FieldName,
                f.FieldType,
                f.Options,
                IsActive = activeMap.ContainsKey(f.FieldId),
                IsVisibleOnCard = activeMap.TryGetValue(f.FieldId, out var acf) && acf.IsVisibleOnCard
            });

            return Ok(result);
        }

        // POST /api/setup/accounts/{accountId}/variables/custom?token=
        [HttpPost("accounts/{accountId}/variables/custom")]
        [SwaggerOperation(
            Summary = "Crear campo personalizado para la cuenta",
            Description = "Crea un nuevo CustomFieldDefinition específico y lo activa inmediatamente para esta cuenta."
        )]
        [SwaggerResponse(200, "Campo creado y activado")]
        [SwaggerResponse(404, "Cuenta no encontrada")]
        public async Task<IActionResult> AddCustomVariable([FromQuery] string token, int accountId, [FromBody] CreateCustomFieldDto model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });
            if (!await AccountBelongsToCustomer(accountId, customer.Id))
                return NotFound(new { message = "Cuenta no encontrada." });

            // Generar FieldCode único para campos custom
            var fieldCode = $"custom_{Guid.NewGuid():N}"[..24];

            var definition = new CustomFieldDefinition
            {
                FieldCode = fieldCode,
                FieldName = model.FieldName.Trim(),
                FieldType = model.FieldType ?? "text",
                Options = model.Options
            };
            _context.CustomFieldDefinitions.Add(definition);
            await _context.SaveChangesAsync();

            _context.AccountCustomFields.Add(new AccountCustomField
            {
                AccountId = accountId,
                FieldId = definition.FieldId,
                IsVisibleOnCard = false
            });
            await _context.SaveChangesAsync();

            return Ok(new
            {
                definition.FieldId,
                definition.FieldCode,
                definition.FieldName,
                definition.FieldType,
                definition.Options,
                IsActive = true,
                IsVisibleOnCard = false
            });
        }

        // PUT /api/setup/accounts/{accountId}/variables?token=
        [HttpPut("accounts/{accountId}/variables")]
        [SwaggerOperation(
            Summary = "Configurar variables (campos personalizados) de la cuenta",
            Description = "Reemplaza las variables activas. Los FieldId deben existir en el catálogo global de CustomFieldDefinitions."
        )]
        [SwaggerResponse(200, "Variables actualizadas")]
        [SwaggerResponse(400, "Uno o más FieldId no existen en el catálogo")]
        [SwaggerResponse(404, "Cuenta no encontrada")]
        public async Task<IActionResult> SetVariables([FromQuery] string token, int accountId, [FromBody] SetupVariablesDto model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });
            if (!await AccountBelongsToCustomer(accountId, customer.Id))
                return NotFound(new { message = "Cuenta no encontrada." });

            // Validar que todos los FieldIds existen
            var fieldIds = model.Fields.Select(f => f.FieldId).ToList();
            if (fieldIds.Any())
            {
                var foundIds = await _context.CustomFieldDefinitions
                    .Where(cf => fieldIds.Contains(cf.FieldId))
                    .Select(cf => cf.FieldId)
                    .ToListAsync();
                var missing = fieldIds.Except(foundIds).ToList();
                if (missing.Any())
                    return BadRequest(new { message = $"FieldId(s) no encontrados en el catálogo: {string.Join(", ", missing)}" });
            }

            var existing = await _context.AccountCustomFields.Where(acf => acf.AccountId == accountId).ToListAsync();
            _context.AccountCustomFields.RemoveRange(existing);

            foreach (var f in model.Fields)
            {
                _context.AccountCustomFields.Add(new AccountCustomField
                {
                    AccountId = accountId,
                    FieldId = f.FieldId,
                    IsVisibleOnCard = f.IsVisibleOnCard
                });
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = $"{model.Fields.Count} variable(s) configurada(s)." });
        }

        // ════════════════════════════════════════════════════════════
        // SCORING
        // ════════════════════════════════════════════════════════════

        // GET /api/setup/accounts/{accountId}/scoring?token=
        [HttpGet("accounts/{accountId}/scoring")]
        [SwaggerOperation(Summary = "Modelo de calificación de la cuenta")]
        [SwaggerResponse(200, "Modelo de calificación", typeof(SetupScoringResponseDto))]
        [SwaggerResponse(404, "Cuenta o modelo no encontrado")]
        public async Task<IActionResult> GetScoring([FromQuery] string token, int accountId)
        {
            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });
            if (!await AccountBelongsToCustomer(accountId, customer.Id))
                return NotFound(new { message = "Cuenta no encontrada." });

            var model = await _context.ScoringModels
                .Include(sm => sm.Questions).ThenInclude(q => q.AnswerOptions)
                .Include(sm => sm.Rules).ThenInclude(r => r.Conditions).ThenInclude(c => c.Field)
                .FirstOrDefaultAsync(sm => sm.AccountId == accountId);

            // Sin modelo aún → 204 (el frontend lo trata como estado inicial limpio)
            if (model == null) return NoContent();

            var tiers = await _context.LeadTiers.Where(t => t.ScoringModelId == model.ScoringModelId).ToListAsync();

            return Ok(MapScoringToDto(model, tiers));
        }

        // PUT /api/setup/accounts/{accountId}/scoring?token=
        [HttpPut("accounts/{accountId}/scoring")]
        [SwaggerOperation(
            Summary = "Configurar modelo de calificación",
            Description = "Si se proporciona TemplateId, clona las preguntas/respuestas del template. Si no, usa el modelo manual. Si no se envían Tiers, se crean 3 por defecto (Frío/Tibio/Caliente)."
        )]
        [SwaggerResponse(200, "Modelo configurado", typeof(SetupScoringResponseDto))]
        [SwaggerResponse(404, "Cuenta o template no encontrado")]
        public async Task<IActionResult> SetScoring([FromQuery] string token, int accountId, [FromBody] SetupScoringDto model)
        {
            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });
            if (!await AccountBelongsToCustomer(accountId, customer.Id))
                return NotFound(new { message = "Cuenta no encontrada." });

            // Se permite crear el modelo solo con tiers (sin preguntas) durante el wizard.
            // Las preguntas se configuran luego desde el panel de administración o con templateId.

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // ── DELETE previo con SQL directo (1 roundtrip sin cargar entidades) ──
                await _context.Database.ExecuteSqlRawAsync(@"
                    DECLARE @mid INT = (SELECT TOP 1 ScoringModelId FROM ScoringModels WHERE AccountId = {0});
                    IF @mid IS NOT NULL BEGIN
                        DELETE FROM ScoringRuleConditions WHERE RuleId IN (SELECT RuleId FROM ScoringRules WHERE ScoringModelId = @mid);
                        DELETE FROM ScoringRules    WHERE ScoringModelId = @mid;
                        DELETE FROM ScoringAnswerOptions WHERE QuestionId IN (SELECT QuestionId FROM ScoringQuestions WHERE ScoringModelId = @mid);
                        DELETE FROM ScoringQuestions WHERE ScoringModelId = @mid;
                        DELETE FROM LeadTiers        WHERE ScoringModelId = @mid;
                        DELETE FROM ScoringModels    WHERE ScoringModelId = @mid;
                    END", accountId);

                List<SetupScoringQuestionDto> questions;
                int? originatingTemplateId = null;

                if (model.TemplateId.HasValue)
                {
                    var template = await _context.ScoringTemplates
                        .Include(t => t.Questions).ThenInclude(q => q.AnswerOptions)
                        .FirstOrDefaultAsync(t => t.TemplateId == model.TemplateId.Value);

                    if (template == null)
                        return NotFound(new { message = $"Template de scoring {model.TemplateId} no encontrado." });

                    originatingTemplateId = template.TemplateId;
                    questions = template.Questions.Select(q => new SetupScoringQuestionDto
                    {
                        QuestionText = q.QuestionText,
                        QuestionType = q.QuestionType,
                        IsRequired = q.IsRequired,
                        OrderPosition = q.OrderPosition,
                        AnswerOptions = q.AnswerOptions.Select(a => new SetupAnswerOptionDto
                        {
                            AnswerText = a.AnswerText,
                            Points = a.Points,
                            OrderPosition = a.OrderPosition
                        }).ToList()
                    }).ToList();
                }
                else
                {
                    questions = model.Questions;
                }

                var scoringModel = new ScoringModel
                {
                    AccountId = accountId,
                    Name = model.ModelName
                };
                _context.ScoringModels.Add(scoringModel);
                await _context.SaveChangesAsync();

                // ── Roundtrip 2: todas las preguntas + respuestas en un solo SaveChanges ──
                // EF Core resuelve los FKs automáticamente con navigation properties
                var createdQuestions = questions.Select(qDto =>
                {
                    var q = new ScoringQuestion
                    {
                        ScoringModelId = scoringModel.ScoringModelId,
                        QuestionText = qDto.QuestionText,
                        QuestionType = qDto.QuestionType,
                        IsRequired = qDto.IsRequired,
                        OrderPosition = qDto.OrderPosition,
                        AnswerOptions = qDto.AnswerOptions.Select(aDto => new ScoringAnswerOption
                        {
                            AnswerText = aDto.AnswerText,
                            Points = aDto.Points,
                            OrderPosition = aDto.OrderPosition
                        }).ToList()
                    };
                    return q;
                }).ToList();

                _context.ScoringQuestions.AddRange(createdQuestions);
                await _context.SaveChangesAsync(); // IDs populados por EF Core en todos los objetos

                // ── Roundtrip 3: reglas + condiciones ──
                var rulesDto = model.Rules ?? new List<SetupScoringRuleDto>();
                if (rulesDto.Any())
                {
                    // Índice por orderPosition para resolver condiciones tipo "answer"
                    var qByOrder = createdQuestions.ToDictionary(q => q.OrderPosition);

                    foreach (var ruleDto in rulesDto)
                    {
                        var rule = new ScoringRule
                        {
                            ScoringModelId = scoringModel.ScoringModelId,
                            Name = ruleDto.Name,
                            BonusPoints = ruleDto.BonusPoints,
                            ExecutionOrder = ruleDto.ExecutionOrder,
                            ActionType = "ADD_POINTS",
                            ActionValue = ruleDto.BonusPoints.ToString(),
                            Conditions = new List<ScoringRuleCondition>()
                        };

                        foreach (var condDto in ruleDto.Conditions)
                        {
                            var cond = new ScoringRuleCondition
                            {
                                ConditionType = condDto.ConditionType ?? "answer",
                                LogicOperator = condDto.LogicOperator ?? "AND",
                                FieldId = condDto.FieldId,
                                ConditionValue = condDto.ConditionValue
                            };

                            if (cond.ConditionType == "answer" && condDto.QuestionOrderPosition.HasValue)
                            {
                                if (!qByOrder.TryGetValue(condDto.QuestionOrderPosition.Value, out var matchQ)) continue;
                                var matchA = matchQ.AnswerOptions.FirstOrDefault(a => a.OrderPosition == condDto.AnswerOrderPosition);
                                if (matchA == null) continue;
                                cond.QuestionId = matchQ.QuestionId;
                                cond.AnswerOptionId = matchA.AnswerOptionId;
                            }

                            rule.Conditions.Add(cond);
                        }

                        _context.ScoringRules.Add(rule);
                    }
                    await _context.SaveChangesAsync();
                }

                // ── Roundtrip 3 (o 4 si había reglas): tiers ──
                var tierDtos = model.Tiers.Any() ? model.Tiers : new List<SetupTierDto>
                {
                    new() { Name = "Frío",     MinScore = 0,   MaxScore = 39,  Color = "#3498db" },
                    new() { Name = "Tibio",    MinScore = 40,  MaxScore = 69,  Color = "#f39c12" },
                    new() { Name = "Caliente", MinScore = 70,  MaxScore = null, Color = "#e74c3c" }
                };

                var createdTiers = tierDtos.Select(t => new LeadTier
                {
                    ScoringModelId = scoringModel.ScoringModelId,
                    Name = t.Name,
                    MinScore = t.MinScore,
                    MaxScore = t.MaxScore,
                    Color = t.Color
                }).ToList();

                _context.LeadTiers.AddRange(createdTiers);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // ── Armar response desde memoria — sin re-query ──
                var responseDto = new SetupScoringResponseDto
                {
                    ScoringModelId = scoringModel.ScoringModelId,
                    ModelName = scoringModel.Name,
                    OriginatingTemplateId = originatingTemplateId,
                    Questions = createdQuestions.OrderBy(q => q.OrderPosition).Select(q => new SetupScoringQuestionResponseDto
                    {
                        QuestionId    = q.QuestionId,
                        QuestionText  = q.QuestionText,
                        QuestionType  = q.QuestionType,
                        IsRequired    = q.IsRequired,
                        OrderPosition = q.OrderPosition,
                        AnswerOptions = q.AnswerOptions.OrderBy(a => a.OrderPosition).Select(a => new SetupAnswerOptionResponseDto
                        {
                            AnswerOptionId = a.AnswerOptionId,
                            AnswerText     = a.AnswerText,
                            Points         = a.Points,
                            OrderPosition  = a.OrderPosition
                        }).ToList()
                    }).ToList(),
                    Tiers = createdTiers.OrderBy(t => t.MinScore).Select(t => new SetupTierResponseDto
                    {
                        TierId    = t.TierId,
                        Name      = t.Name,
                        MinScore  = t.MinScore,
                        MaxScore  = t.MaxScore,
                        Color     = t.Color
                    }).ToList(),
                    Rules = new List<SetupScoringRuleResponseDto>() // reglas guardadas, sin Field.FieldName (no crítico en respuesta del save)
                };

                return Ok(responseDto);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                // Recorrer toda la cadena de inner exceptions hasta el error real
                var e = ex;
                var messages = new List<string>();
                while (e != null) { messages.Add(e.Message); e = e.InnerException; }
                return StatusCode(500, new { message = "Error al configurar scoring.", details = string.Join(" → ", messages) });
            }
        }

        // PUT /api/setup/accounts/{accountId}/scoring/questions?token=
        [HttpPut("accounts/{accountId}/scoring/questions")]
        [SwaggerOperation(
            Summary = "Reemplazar preguntas del modelo de calificación",
            Description = "Elimina todas las preguntas/respuestas actuales y las reemplaza con las enviadas. El modelo debe existir (usar PUT /scoring primero)."
        )]
        [SwaggerResponse(200, "Preguntas actualizadas", typeof(SetupScoringResponseDto))]
        [SwaggerResponse(404, "Cuenta o modelo no encontrado")]
        public async Task<IActionResult> UpdateScoringQuestions([FromQuery] string token, int accountId, [FromBody] UpdateSetupScoringQuestionsDto model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });
            if (!await AccountBelongsToCustomer(accountId, customer.Id))
                return NotFound(new { message = "Cuenta no encontrada." });

            var scoringModel = await _context.ScoringModels
                .Include(sm => sm.Questions).ThenInclude(q => q.AnswerOptions)
                .Include(sm => sm.Rules).ThenInclude(r => r.Conditions)
                .FirstOrDefaultAsync(sm => sm.AccountId == accountId);
            if (scoringModel == null)
                return NotFound(new { message = "La cuenta no tiene modelo de calificación. Usa PUT /scoring primero." });

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Limpiar reglas huérfanas al reemplazar preguntas
                foreach (var r in scoringModel.Rules)
                    _context.ScoringRuleConditions.RemoveRange(r.Conditions);
                _context.ScoringRules.RemoveRange(scoringModel.Rules);
                foreach (var q in scoringModel.Questions)
                    _context.ScoringAnswerOptions.RemoveRange(q.AnswerOptions);
                _context.ScoringQuestions.RemoveRange(scoringModel.Questions);
                await _context.SaveChangesAsync();

                foreach (var qDto in model.Questions)
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
                    {
                        _context.ScoringAnswerOptions.Add(new ScoringAnswerOption
                        {
                            QuestionId = question.QuestionId,
                            AnswerText = aDto.AnswerText,
                            Points = aDto.Points,
                            OrderPosition = aDto.OrderPosition
                        });
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                var result = await _context.ScoringModels
                    .Include(sm => sm.Questions).ThenInclude(q => q.AnswerOptions)
                    .Include(sm => sm.Rules).ThenInclude(r => r.Conditions)
                    .FirstAsync(sm => sm.ScoringModelId == scoringModel.ScoringModelId);
                var tiers = await _context.LeadTiers.Where(t => t.ScoringModelId == scoringModel.ScoringModelId).ToListAsync();

                return Ok(MapScoringToDto(result, tiers));
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { message = "Error al actualizar preguntas.", details = ex.Message });
            }
        }

        // ════════════════════════════════════════════════════════════
        // CATÁLOGOS (MOTIVOS DE PÉRDIDA, FUENTES, TAGS)
        // ════════════════════════════════════════════════════════════

        // GET /api/setup/accounts/{accountId}/catalogs?token=
        [HttpGet("accounts/{accountId}/catalogs")]
        [SwaggerOperation(Summary = "Catálogos activos de la cuenta (motivos pérdida, fuentes, tags)")]
        [SwaggerResponse(200, "Catálogos activos")]
        [SwaggerResponse(404, "Cuenta no encontrada")]
        public async Task<IActionResult> GetCatalogs([FromQuery] string token, int accountId)
        {
            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });

            var account = await _context.Accounts.FindAsync(accountId);
            if (account == null || account.CustomerId != customer.Id)
                return NotFound(new { message = "Cuenta no encontrada." });

            // Sugerencias globales (admin las precarga)
            var templates = await _context.LeadLostReasonTemplates
                .Where(t => t.IsActive)
                .OrderBy(t => t.TemplateId)
                .Select(t => new { t.TemplateId, t.Description, t.CountsForCharts })
                .ToListAsync();

            // Motivos reales ya configurados para esta cuenta
            var accountReasons = await _context.LeadLostReasons
                .Where(r => r.AccountId == accountId && r.IsActive)
                .OrderBy(r => r.LostReasonId)
                .Select(r => new { r.LostReasonId, r.Description, r.CountsForCharts })
                .ToListAsync();

            var tags = await _context.Tags
                .Where(t => t.CustomerId == customer.Id)
                .Select(t => new { t.TagId, t.Name, t.Color, t.FontColor })
                .ToListAsync();

            return Ok(new
            {
                Templates = templates,
                LostReasons = accountReasons,
                Tags = tags
            });
        }

        // PUT /api/setup/accounts/{accountId}/catalogs?token=
        [HttpPut("accounts/{accountId}/catalogs")]
        [SwaggerOperation(
            Summary = "Configurar catálogos de la cuenta",
            Description = "Asigna motivos de pérdida del catálogo global, fuentes de prospectos y crea etiquetas propias de la cuenta."
        )]
        [SwaggerResponse(200, "Catálogos configurados")]
        [SwaggerResponse(404, "Cuenta no encontrada")]
        public async Task<IActionResult> SetCatalogs([FromQuery] string token, int accountId, [FromBody] SetupCatalogsDto model)
        {
            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });

            var account = await _context.Accounts.FindAsync(accountId);
            if (account == null || account.CustomerId != customer.Id)
                return NotFound(new { message = "Cuenta no encontrada." });

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Resolver descripciones de los templates seleccionados
                var fromTemplates = model.LostReasonIds.Count > 0
                    ? await _context.LeadLostReasonTemplates
                        .Where(t => model.LostReasonIds.Contains(t.TemplateId))
                        .Select(t => t.Description)
                        .ToListAsync()
                    : new List<string>();

                // Todas las descripciones a guardar (templates + custom)
                var allDescriptions = fromTemplates
                    .Concat(model.CustomLostReasons.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()))
                    .ToList();

                // Insertar solo las que no existen ya en la cuenta
                var existingDescriptions = (await _context.LeadLostReasons
                    .Where(r => r.AccountId == accountId)
                    .Select(r => r.Description)
                    .ToListAsync())
                    .Select(d => d.ToLower())
                    .ToHashSet();

                foreach (var desc in allDescriptions.Where(d => !existingDescriptions.Contains(d.ToLower())))
                {
                    _context.LeadLostReasons.Add(new LeadLostReason
                    {
                        AccountId = accountId,
                        Description = desc,
                        CountsForCharts = true
                    });
                    existingDescriptions.Add(desc.ToLower());
                }

                // 6. Tags: agregar solo los nuevos
                foreach (var tagDto in model.Tags)
                {
                    var exists = await _context.Tags.AnyAsync(t => t.CustomerId == customer.Id && t.Name == tagDto.Name);
                    if (!exists)
                    {
                        _context.Tags.Add(new Tag
                        {
                            CustomerId = customer.Id,
                            Name = tagDto.Name,
                            Color = tagDto.Color,
                            FontColor = tagDto.FontColor
                        });
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new
                {
                    message = "Catálogos actualizados.",
                    lostReasonsCount = model.LostReasonIds.Count + model.CustomLostReasons.Count,
                    tagsAdded = model.Tags.Count
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { message = "Error al configurar catálogos.", details = ex.Message });
            }
        }

        // ════════════════════════════════════════════════════════════
        // USUARIOS
        // ════════════════════════════════════════════════════════════

        // GET /api/setup/users?token=
        [HttpGet("users")]
        [SwaggerOperation(Summary = "Listar usuarios del customer", Description = "Incluye sus asignaciones a cuentas.")]
        [SwaggerResponse(200, "Lista de usuarios", typeof(List<SetupUserResponseDto>))]
        [SwaggerResponse(401, "Token inválido")]
        public async Task<IActionResult> GetUsers([FromQuery] string token)
        {
            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });

            var users = await _context.Users
                .Include(u => u.UserProfile)
                .Where(u => u.CustomerId == customer.Id && u.Deleted == false)
                .ToListAsync();

            var assignments = await _context.AccountInternalUsers
                .Include(aiu => aiu.Account)
                .Where(aiu => aiu.Account.CustomerId == customer.Id)
                .ToListAsync();

            var result = new List<SetupUserResponseDto>();
            foreach (var u in users)
            {
                var roles = await _userManager.GetRolesAsync(u);
                result.Add(new SetupUserResponseDto
                {
                    UserId = u.Id,
                    Email = u.Email ?? "",
                    FullName = $"{u.UserProfile?.FirstName} {u.UserProfile?.LastName}".Trim(),
                    Role = roles.FirstOrDefault() ?? "SalesRep",
                    Active = u.Active ?? false,
                    Accounts = assignments
                        .Where(aiu => aiu.UserId == u.Id)
                        .Select(aiu => new SetupUserAccountAssignmentDto
                        {
                            AccountId = aiu.AccountId,
                            AccountName = aiu.Account.Name,
                            RoleInAccount = aiu.RoleInAccount
                        }).ToList()
                });
            }

            return Ok(result);
        }

        // POST /api/setup/users?token=
        [HttpPost("users")]
        [SwaggerOperation(
            Summary = "Crear usuario (estado borrador)",
            Description = "Crea el usuario con Active=false. Se activará al completar el setup. El email será su login."
        )]
        [SwaggerResponse(201, "Usuario creado", typeof(SetupUserResponseDto))]
        [SwaggerResponse(400, "Email ya registrado o datos inválidos")]
        [SwaggerResponse(401, "Token inválido")]
        public async Task<IActionResult> CreateUser([FromQuery] string token, [FromBody] CreateSetupUserDto model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });

            // Verificar email único
            var emailExists = await _userManager.FindByEmailAsync(model.Email);
            if (emailExists != null)
                return BadRequest(new { message = $"El email '{model.Email}' ya está registrado." });

            var user = new ApplicationUser
            {
                UserName = model.Email,
                Email = model.Email,
                CustomerId = customer.Id,
                Active = false,   // Draft hasta completar setup
                Deleted = false,
                UserType = "Client",
                CreatedOn = DateTime.UtcNow
            };

            var createResult = await _userManager.CreateAsync(user, model.Password);
            if (!createResult.Succeeded)
                return BadRequest(new { message = "Error al crear el usuario.", errors = createResult.Errors });

            await _userManager.AddToRoleAsync(user, model.Role);

            var profile = new UserProfile
            {
                UserId = user.Id,
                FirstName = model.FirstName,
                LastName = model.LastName,
                Phone = model.Phone
            };
            _context.UserProfiles.Add(profile);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetUsers), new { token },
                new SetupUserResponseDto
                {
                    UserId = user.Id,
                    Email = user.Email!,
                    FullName = $"{model.FirstName} {model.LastName}".Trim(),
                    Role = model.Role,
                    Active = false,
                    Accounts = new List<SetupUserAccountAssignmentDto>()
                });
        }

        // PUT /api/setup/users/{userId}?token=
        [HttpPut("users/{userId}")]
        [SwaggerOperation(Summary = "Actualizar datos del usuario")]
        [SwaggerResponse(200, "Usuario actualizado", typeof(SetupUserResponseDto))]
        [SwaggerResponse(401, "Token inválido")]
        [SwaggerResponse(404, "Usuario no encontrado")]
        public async Task<IActionResult> UpdateUser([FromQuery] string token, string userId, [FromBody] UpdateSetupUserDto model)
        {
            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });
            if (!await UserBelongsToCustomer(userId, customer.Id))
                return NotFound(new { message = "Usuario no encontrado." });

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound(new { message = "Usuario no encontrado." });

            var profile = await _context.UserProfiles.FindAsync(userId);
            if (profile == null)
            {
                profile = new UserProfile { UserId = userId };
                _context.UserProfiles.Add(profile);
            }

            if (!string.IsNullOrWhiteSpace(model.FirstName)) profile.FirstName = model.FirstName;
            if (!string.IsNullOrWhiteSpace(model.LastName)) profile.LastName = model.LastName;
            if (!string.IsNullOrWhiteSpace(model.Phone)) profile.Phone = model.Phone;

            if (!string.IsNullOrWhiteSpace(model.Role))
            {
                var currentRoles = await _userManager.GetRolesAsync(user);
                await _userManager.RemoveFromRolesAsync(user, currentRoles);
                await _userManager.AddToRoleAsync(user, model.Role);
            }

            await _context.SaveChangesAsync();

            var roles = await _userManager.GetRolesAsync(user);
            return Ok(new SetupUserResponseDto
            {
                UserId = user.Id,
                Email = user.Email!,
                FullName = $"{profile.FirstName} {profile.LastName}".Trim(),
                Role = roles.FirstOrDefault() ?? "SalesRep",
                Active = user.Active ?? false
            });
        }

        // DELETE /api/setup/users/{userId}?token=
        [HttpDelete("users/{userId}")]
        [SwaggerOperation(Summary = "Eliminar usuario (solo antes de activar)")]
        [SwaggerResponse(204, "Usuario eliminado")]
        [SwaggerResponse(400, "No se puede eliminar un usuario ya activo")]
        [SwaggerResponse(401, "Token inválido")]
        [SwaggerResponse(404, "Usuario no encontrado")]
        public async Task<IActionResult> DeleteUser([FromQuery] string token, string userId)
        {
            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });
            if (!await UserBelongsToCustomer(userId, customer.Id))
                return NotFound(new { message = "Usuario no encontrado." });

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound(new { message = "Usuario no encontrado." });
            if (user.Active == true)
                return BadRequest(new { message = "No se puede eliminar un usuario ya activo." });

            user.Deleted = true;
            await _userManager.UpdateAsync(user);
            return NoContent();
        }

        // ════════════════════════════════════════════════════════════
        // ASIGNACIÓN USUARIO → CUENTA
        // ════════════════════════════════════════════════════════════

        // POST /api/setup/accounts/{accountId}/users?token=
        [HttpPost("accounts/{accountId}/users")]
        [SwaggerOperation(
            Summary = "Asignar usuario a cuenta",
            Description = "Vincula un usuario (ya creado en el wizard) a una cuenta específica con un rol dentro de ella."
        )]
        [SwaggerResponse(200, "Usuario asignado")]
        [SwaggerResponse(400, "El usuario ya está asignado a esta cuenta")]
        [SwaggerResponse(401, "Token inválido")]
        [SwaggerResponse(404, "Cuenta o usuario no encontrado")]
        public async Task<IActionResult> AssignUserToAccount([FromQuery] string token, int accountId, [FromBody] AssignUserToAccountDto model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });
            if (!await AccountBelongsToCustomer(accountId, customer.Id))
                return NotFound(new { message = "Cuenta no encontrada." });
            if (!await UserBelongsToCustomer(model.UserId, customer.Id))
                return NotFound(new { message = "Usuario no encontrado o no pertenece a este cliente." });

            // Verificar que no exista ya
            var exists = await _context.AccountInternalUsers.AnyAsync(
                aiu => aiu.AccountId == accountId && aiu.UserId == model.UserId && aiu.RoleInAccount == model.RoleInAccount);
            if (exists)
                return BadRequest(new { message = "El usuario ya tiene este rol asignado en esta cuenta." });

            // Eliminar asignación previa (cualquier rol) y reemplazar
            var prev = await _context.AccountInternalUsers
                .FirstOrDefaultAsync(aiu => aiu.AccountId == accountId && aiu.UserId == model.UserId);
            if (prev != null) _context.AccountInternalUsers.Remove(prev);

            _context.AccountInternalUsers.Add(new AccountInternalUser
            {
                AccountId = accountId,
                UserId = model.UserId,
                RoleInAccount = model.RoleInAccount
            });

            await _context.SaveChangesAsync();
            return Ok(new { message = "Usuario asignado a la cuenta.", accountId, userId = model.UserId, role = model.RoleInAccount });
        }

        // DELETE /api/setup/accounts/{accountId}/users/{userId}?token=
        [HttpDelete("accounts/{accountId}/users/{userId}")]
        [SwaggerOperation(Summary = "Desasignar usuario de una cuenta")]
        [SwaggerResponse(204, "Usuario desasignado")]
        [SwaggerResponse(401, "Token inválido")]
        [SwaggerResponse(404, "Asignación no encontrada")]
        public async Task<IActionResult> UnassignUserFromAccount([FromQuery] string token, int accountId, string userId)
        {
            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });
            if (!await AccountBelongsToCustomer(accountId, customer.Id))
                return NotFound(new { message = "Cuenta no encontrada." });

            var assignment = await _context.AccountInternalUsers
                .FirstOrDefaultAsync(aiu => aiu.AccountId == accountId && aiu.UserId == userId);
            if (assignment == null)
                return NotFound(new { message = "El usuario no está asignado a esta cuenta." });

            _context.AccountInternalUsers.Remove(assignment);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // ════════════════════════════════════════════════════════════
        // EQUIPOS
        // ════════════════════════════════════════════════════════════

        // GET /api/setup/teams?token=
        [HttpGet("teams")]
        [SwaggerOperation(Summary = "Listar equipos del customer")]
        [SwaggerResponse(200, "Lista de equipos", typeof(List<SetupTeamResponseDto>))]
        [SwaggerResponse(401, "Token inválido")]
        public async Task<IActionResult> GetTeams([FromQuery] string token)
        {
            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });

            var teams = await _context.Teams
                .Include(t => t.Leader).ThenInclude(l => l.UserProfile)
                .Include(t => t.UserTeams).ThenInclude(ut => ut.User)
                    .ThenInclude(u => u.UserProfile)
                .Where(t => t.CustomerId == customer.Id)
                .ToListAsync();

            return Ok(teams.Select(t => new SetupTeamResponseDto
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
                    Email = ut.User.Email ?? ""
                }).ToList()
            }));
        }

        // POST /api/setup/teams?token=
        [HttpPost("teams")]
        [SwaggerOperation(Summary = "Crear equipo", Description = "Crea un equipo y asigna los usuarios indicados.")]
        [SwaggerResponse(201, "Equipo creado", typeof(SetupTeamResponseDto))]
        [SwaggerResponse(401, "Token inválido")]
        public async Task<IActionResult> CreateTeam([FromQuery] string token, [FromBody] CreateSetupTeamDto model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });

            // Validar que los users pertenezcan al customer
            foreach (var uid in model.UserIds)
            {
                if (!await UserBelongsToCustomer(uid, customer.Id))
                    return BadRequest(new { message = $"El usuario {uid} no pertenece a este cliente." });
            }
            if (model.LeaderId != null && !await UserBelongsToCustomer(model.LeaderId, customer.Id))
                return BadRequest(new { message = "El líder indicado no pertenece a este cliente." });

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var team = new Team { Name = model.Name, CustomerId = customer.Id, LeaderId = model.LeaderId };
                _context.Teams.Add(team);
                await _context.SaveChangesAsync();

                foreach (var uid in model.UserIds)
                    _context.UserTeams.Add(new UserTeam { TeamId = team.Id, UserId = uid });

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                var result = await _context.Teams
                    .Include(t => t.Leader).ThenInclude(l => l.UserProfile)
                    .Include(t => t.UserTeams).ThenInclude(ut => ut.User).ThenInclude(u => u.UserProfile)
                    .FirstAsync(t => t.Id == team.Id);

                return CreatedAtAction(nameof(GetTeams), new { token },
                    new SetupTeamResponseDto
                    {
                        TeamId = result.Id,
                        Name = result.Name,
                        LeaderId = result.LeaderId,
                        LeaderName = result.Leader != null
                            ? $"{result.Leader.UserProfile?.FirstName} {result.Leader.UserProfile?.LastName}".Trim()
                            : null,
                        Members = result.UserTeams.Select(ut => new SetupTeamMemberDto
                        {
                            UserId = ut.UserId,
                            FullName = $"{ut.User.UserProfile?.FirstName} {ut.User.UserProfile?.LastName}".Trim(),
                            Email = ut.User.Email ?? ""
                        }).ToList()
                    });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { message = "Error al crear el equipo.", details = ex.Message });
            }
        }

        // PUT /api/setup/teams/{teamId}?token=
        [HttpPut("teams/{teamId}")]
        [SwaggerOperation(Summary = "Actualizar equipo", Description = "Actualiza nombre y reemplaza la lista de miembros.")]
        [SwaggerResponse(200, "Equipo actualizado", typeof(SetupTeamResponseDto))]
        [SwaggerResponse(401, "Token inválido")]
        [SwaggerResponse(404, "Equipo no encontrado")]
        public async Task<IActionResult> UpdateTeam([FromQuery] string token, int teamId, [FromBody] UpdateSetupTeamDto model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });

            var team = await _context.Teams.Include(t => t.UserTeams)
                .FirstOrDefaultAsync(t => t.Id == teamId && t.CustomerId == customer.Id);
            if (team == null) return NotFound(new { message = "Equipo no encontrado." });

            foreach (var uid in model.UserIds)
            {
                if (!await UserBelongsToCustomer(uid, customer.Id))
                    return BadRequest(new { message = $"El usuario {uid} no pertenece a este cliente." });
            }
            if (model.LeaderId != null && !await UserBelongsToCustomer(model.LeaderId, customer.Id))
                return BadRequest(new { message = "El líder indicado no pertenece a este cliente." });

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                team.Name = model.Name;
                team.LeaderId = model.LeaderId;
                _context.UserTeams.RemoveRange(team.UserTeams);
                foreach (var uid in model.UserIds)
                    _context.UserTeams.Add(new UserTeam { TeamId = team.Id, UserId = uid });

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                var result = await _context.Teams
                    .Include(t => t.Leader).ThenInclude(l => l.UserProfile)
                    .Include(t => t.UserTeams).ThenInclude(ut => ut.User).ThenInclude(u => u.UserProfile)
                    .FirstAsync(t => t.Id == team.Id);

                return Ok(new SetupTeamResponseDto
                {
                    TeamId = result.Id,
                    Name = result.Name,
                    LeaderId = result.LeaderId,
                    LeaderName = result.Leader != null
                        ? $"{result.Leader.UserProfile?.FirstName} {result.Leader.UserProfile?.LastName}".Trim()
                        : null,
                    Members = result.UserTeams.Select(ut => new SetupTeamMemberDto
                    {
                        UserId = ut.UserId,
                        FullName = $"{ut.User.UserProfile?.FirstName} {ut.User.UserProfile?.LastName}".Trim(),
                        Email = ut.User.Email ?? ""
                    }).ToList()
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { message = "Error al actualizar equipo.", details = ex.Message });
            }
        }

        // DELETE /api/setup/teams/{teamId}?token=
        [HttpDelete("teams/{teamId}")]
        [SwaggerOperation(Summary = "Eliminar equipo")]
        [SwaggerResponse(204, "Equipo eliminado")]
        [SwaggerResponse(401, "Token inválido")]
        [SwaggerResponse(404, "Equipo no encontrado")]
        public async Task<IActionResult> DeleteTeam([FromQuery] string token, int teamId)
        {
            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });

            var team = await _context.Teams.Include(t => t.UserTeams)
                .FirstOrDefaultAsync(t => t.Id == teamId && t.CustomerId == customer.Id);
            if (team == null) return NotFound(new { message = "Equipo no encontrado." });

            _context.UserTeams.RemoveRange(team.UserTeams);
            _context.Teams.Remove(team);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // ════════════════════════════════════════════════════════════
        // PREVIEW
        // GET /api/setup/preview?token=
        // ════════════════════════════════════════════════════════════

        [HttpGet("preview")]
        [SwaggerOperation(
            Summary = "Preview completo antes de confirmar",
            Description = "Devuelve un resumen completo de toda la configuración del wizard y una validación indicando si puede completarse."
        )]
        [SwaggerResponse(200, "Preview completo", typeof(SetupPreviewDto))]
        [SwaggerResponse(401, "Token inválido")]
        public async Task<IActionResult> GetPreview([FromQuery] string token)
        {
            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });

            var subscription = await _context.Subscriptions
                .Include(s => s.Plan)
                .FirstOrDefaultAsync(s => s.CustomerId == customer.Id && s.Status != "Canceled");

            var customerDto = new SetupPreviewCustomerDto
            {
                Name = customer.Name,
                Email = customer.Email,
                PlanName = subscription?.Plan.Name ?? "Sin plan",
                PriceAgreed = subscription?.PriceAgreed ?? 0,
                BillingCycle = subscription?.BillingCycle ?? "-"
            };

            var accounts = await _context.Accounts
                .Where(a => a.CustomerId == customer.Id)
                .Include(a => a.Funnels).ThenInclude(f => f.Stages)
                .Include(a => a.InternalUsers)
                .ToListAsync();

            var accountsDto = new List<SetupPreviewAccountDto>();
            foreach (var a in accounts)
            {
                var industries = await _context.AccountIndustries
                    .Where(ai => ai.AccountId == a.AccountId)
                    .Include(ai => ai.Industry)
                    .Select(ai => ai.Industry.NameES)
                    .ToListAsync();

                var funnel = a.Funnels.FirstOrDefault();
                SetupFunnelResponseDto? funnelDto = funnel != null ? MapFunnelToDto(funnel) : null;

                var variables = await _context.AccountCustomFields
                    .Where(acf => acf.AccountId == a.AccountId)
                    .Include(acf => acf.CustomFieldDefinition)
                    .Select(acf => acf.CustomFieldDefinition.FieldName)
                    .ToListAsync();

                var scoringModel = await _context.ScoringModels
                    .Include(sm => sm.Questions).ThenInclude(q => q.AnswerOptions)
                    .Include(sm => sm.Rules).ThenInclude(r => r.Conditions).ThenInclude(c => c.Field)
                    .FirstOrDefaultAsync(sm => sm.AccountId == a.AccountId);
                var tiers = scoringModel != null
                    ? await _context.LeadTiers.Where(t => t.ScoringModelId == scoringModel.ScoringModelId).ToListAsync()
                    : new List<LeadTier>();
                SetupScoringResponseDto? scoringDto = scoringModel != null ? MapScoringToDto(scoringModel, tiers) : null;

                var lostReasons = await _context.LeadLostReasons
                    .Where(r => r.AccountId == a.AccountId && r.IsActive)
                    .Select(r => r.Description)
                    .ToListAsync();

                var tags = await _context.Tags
                    .Where(t => t.CustomerId == customer.Id)
                    .Select(t => t.Name)
                    .ToListAsync();

                var assignedUsers = a.InternalUsers.Select(iu => new SetupUserAccountAssignmentDto
                {
                    AccountId = a.AccountId,
                    AccountName = a.Name,
                    RoleInAccount = iu.RoleInAccount
                }).ToList();

                accountsDto.Add(new SetupPreviewAccountDto
                {
                    AccountId = a.AccountId,
                    Name = a.Name,
                    Industries = industries,
                    Funnel = funnelDto,
                    ActiveVariables = variables,
                    Scoring = scoringDto,
                    LostReasons = lostReasons,
                    Tags = tags,
                    AssignedUsers = assignedUsers
                });
            }

            // Usuarios
            var users = await _context.Users
                .Include(u => u.UserProfile)
                .Where(u => u.CustomerId == customer.Id && u.Deleted == false)
                .ToListAsync();
            var assignments = await _context.AccountInternalUsers
                .Include(aiu => aiu.Account)
                .Where(aiu => aiu.Account.CustomerId == customer.Id)
                .ToListAsync();
            var usersDto = new List<SetupUserResponseDto>();
            foreach (var u in users)
            {
                var roles = await _userManager.GetRolesAsync(u);
                usersDto.Add(new SetupUserResponseDto
                {
                    UserId = u.Id,
                    Email = u.Email ?? "",
                    FullName = $"{u.UserProfile?.FirstName} {u.UserProfile?.LastName}".Trim(),
                    Role = roles.FirstOrDefault() ?? "SalesRep",
                    Active = u.Active ?? false,
                    Accounts = assignments.Where(a => a.UserId == u.Id)
                        .Select(a => new SetupUserAccountAssignmentDto
                        {
                            AccountId = a.AccountId,
                            AccountName = a.Account.Name,
                            RoleInAccount = a.RoleInAccount
                        }).ToList()
                });
            }

            // Teams
            var teams = await _context.Teams
                .Include(t => t.UserTeams).ThenInclude(ut => ut.User).ThenInclude(u => u.UserProfile)
                .Where(t => t.CustomerId == customer.Id)
                .ToListAsync();
            var teamsDto = teams.Select(t => new SetupTeamResponseDto
            {
                TeamId = t.Id,
                Name = t.Name,
                Members = t.UserTeams.Select(ut => new SetupTeamMemberDto
                {
                    UserId = ut.UserId,
                    FullName = $"{ut.User.UserProfile?.FirstName} {ut.User.UserProfile?.LastName}".Trim(),
                    Email = ut.User.Email ?? ""
                }).ToList()
            }).ToList();

            // Validación
            var errors = new List<string>();
            var warnings = new List<string>();
            if (!accounts.Any()) errors.Add("Debes crear al menos una cuenta.");
            foreach (var a in accountsDto)
            {
                if (a.Funnel == null || !a.Funnel.Stages.Any())
                    errors.Add($"La cuenta '{a.Name}' no tiene embudo configurado.");
                if (a.Scoring == null)
                    warnings.Add($"La cuenta '{a.Name}' no tiene modelo de calificación.");
                if (!a.AssignedUsers.Any())
                    warnings.Add($"La cuenta '{a.Name}' no tiene usuarios asignados.");
            }

            return Ok(new SetupPreviewDto
            {
                Customer = customerDto,
                Accounts = accountsDto,
                Users = usersDto,
                Teams = teamsDto,
                Validation = new SetupValidationDto
                {
                    IsValid = !errors.Any(),
                    Errors = errors,
                    Warnings = warnings
                }
            });
        }

        // ════════════════════════════════════════════════════════════
        // COMPLETE — Activación final
        // POST /api/setup/complete?token=
        // ════════════════════════════════════════════════════════════

        [HttpPost("complete")]
        [SwaggerOperation(
            Summary = "Completar setup y activar",
            Description = "Activa todas las cuentas (Borrador → Activo), activa todos los usuarios (Active=false → true) y actualiza el estatus del cliente a 'Activo'. Operación atómica: si algo falla se hace rollback."
        )]
        [SwaggerResponse(200, "Setup completado", typeof(SetupCompleteResponseDto))]
        [SwaggerResponse(400, "Hay errores que impiden completar el setup")]
        [SwaggerResponse(401, "Token inválido")]
        public async Task<IActionResult> CompleteSetup([FromQuery] string token)
        {
            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });

            // Pre-validación
            var accounts = await _context.Accounts.Include(a => a.Funnels).ThenInclude(f => f.Stages)
                .Where(a => a.CustomerId == customer.Id).ToListAsync();

            var errors = new List<string>();
            if (!accounts.Any()) errors.Add("Debes crear al menos una cuenta.");
            foreach (var a in accounts)
            {
                var funnel = a.Funnels.FirstOrDefault();
                if (funnel == null || !funnel.Stages.Any())
                    errors.Add($"La cuenta '{a.Name}' no tiene embudo configurado.");
            }
            if (errors.Any())
                return BadRequest(new { message = "No se puede completar el setup.", errors });

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1. Activar cuentas
                int accountsActivated = 0;
                foreach (var a in accounts)
                {
                    if (a.Status == "Borrador")
                    {
                        a.Status = "Activo";
                        accountsActivated++;
                    }
                }

                // 2. Activar usuarios
                var users = await _context.Users
                    .Where(u => u.CustomerId == customer.Id && u.Active == false && u.Deleted == false)
                    .ToListAsync();
                int usersActivated = 0;
                foreach (var u in users)
                {
                    u.Active = true;
                    usersActivated++;
                }

                // 3. Actualizar estatus del customer
                customer.Status = "Activo";
                customer.SetupToken = null; // Invalidar token para seguridad

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // Email del primer admin/accountadmin
                var firstUser = await _context.Users
                    .Where(u => u.CustomerId == customer.Id && u.Active == true && u.Deleted == false)
                    .FirstOrDefaultAsync();

                return Ok(new SetupCompleteResponseDto
                {
                    Message = "¡Setup completado exitosamente! Ya puedes iniciar sesión.",
                    AdminEmail = firstUser?.Email ?? customer.Email ?? "",
                    LoginUrl = _frontendLoginUrl,
                    AccountsActivated = accountsActivated,
                    UsersActivated = usersActivated
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { message = "Error al completar el setup.", details = ex.Message });
            }
        }

        // ════════════════════════════════════════════════════════════
        // ACTUALIZAR PASO (UX helper)
        // PUT /api/setup/step?token=&step=
        // ════════════════════════════════════════════════════════════

        [HttpPut("step")]
        [SwaggerOperation(
            Summary = "Guardar último paso visitado",
            Description = "Solo actualiza SetupStep para que el frontend pueda retomar donde se quedó. No valida datos."
        )]
        [SwaggerResponse(200, "Paso guardado")]
        [SwaggerResponse(401, "Token inválido")]
        public async Task<IActionResult> UpdateStep([FromQuery] string token, [FromQuery] int step)
        {
            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });

            customer.SetupStep = step;
            await _context.SaveChangesAsync();
            return Ok(new { message = "Paso guardado.", step });
        }

        // ════════════════════════════════════════════════════════════
        // UPLOAD DE ARCHIVOS (logo, favicon)
        // ════════════════════════════════════════════════════════════

        // POST /api/setup/upload?token=&type=logo|favicon
        [HttpPost("upload")]
        [SwaggerOperation(
            Summary = "Subir imagen de branding (logo o favicon)",
            Description = "Sube un archivo de imagen (PNG, JPG, SVG, ICO, WebP — máx 2 MB) y devuelve la URL pública para usar en la configuración de marca."
        )]
        [SwaggerResponse(200, "Archivo subido", typeof(object))]
        [SwaggerResponse(400, "Archivo inválido o muy grande")]
        [SwaggerResponse(401, "Token inválido")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadBrandingFile(
            [FromQuery] string token,
            [FromQuery] string type,
            IFormFile file)
        {
            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });

            if (file == null || file.Length == 0)
                return BadRequest(new { message = "No se recibió ningún archivo." });

            // Validar tamaño (máx 2 MB)
            if (file.Length > 2 * 1024 * 1024)
                return BadRequest(new { message = "El archivo no puede superar 2 MB." });

            // Validar tipo MIME
            var allowedMimes = new[] { "image/png", "image/jpeg", "image/jpg", "image/svg+xml", "image/x-icon", "image/vnd.microsoft.icon", "image/webp" };
            if (!allowedMimes.Contains(file.ContentType.ToLower()))
                return BadRequest(new { message = "Solo se permiten imágenes (PNG, JPG, SVG, ICO, WebP)." });

            // Validar parámetro type
            var validTypes = new[] { "logo", "logo-large", "logo-small", "favicon" };
            if (!validTypes.Contains(type))
                return BadRequest(new { message = "El parámetro 'type' debe ser: logo, logo-large, logo-small o favicon." });

            // Construir ruta de destino
            var extension = Path.GetExtension(file.FileName).ToLower();
            var folder = Path.Combine("wwwroot", "uploads", "branding", customer.Id.ToString());
            Directory.CreateDirectory(folder);

            var fileName = $"{type}_{customer.Id}{extension}";
            var filePath = Path.Combine(folder, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
                await file.CopyToAsync(stream);

            // Construir URL pública
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var publicUrl = $"{baseUrl}/uploads/branding/{customer.Id}/{fileName}";

            return Ok(new { url = publicUrl, type });
        }

        // ════════════════════════════════════════════════════════════
        // BRANDING / WHITE-LABEL
        // ════════════════════════════════════════════════════════════

        // GET /api/setup/branding?token=
        [HttpGet("branding")]
        [SwaggerOperation(
            Summary = "Obtener configuración de branding",
            Description = "Retorna los valores actuales de marca (logo, colores, nombre de app, favicon). Si no se ha personalizado, los campos vienen en null."
        )]
        [SwaggerResponse(200, "Configuración de branding", typeof(SetupBrandingResponseDto))]
        [SwaggerResponse(401, "Token inválido")]
        public async Task<IActionResult> GetBranding([FromQuery] string token)
        {
            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });

            return Ok(new SetupBrandingResponseDto
            {
                BrandName = customer.BrandName,
                BrandLogoUrl = customer.BrandLogoUrl,
                BrandLogoSmallUrl = customer.BrandLogoSmallUrl,
                BrandPrimaryColor = customer.BrandPrimaryColor,
                BrandSecondaryColor = customer.BrandSecondaryColor,
                BrandFaviconUrl = customer.BrandFaviconUrl,
                IsCustomized = customer.BrandName != null || customer.BrandLogoUrl != null
                    || customer.BrandLogoSmallUrl != null || customer.BrandPrimaryColor != null
                    || customer.BrandSecondaryColor != null || customer.BrandFaviconUrl != null
            });
        }

        // PUT /api/setup/branding?token=
        [HttpPut("branding")]
        [SwaggerOperation(
            Summary = "Guardar configuración de branding",
            Description = "Actualiza los campos de marca. Envía null en un campo para eliminar la personalización de ese campo (vuelve al default de Profet). Operación idempotente."
        )]
        [SwaggerResponse(200, "Branding guardado", typeof(SetupBrandingResponseDto))]
        [SwaggerResponse(401, "Token inválido")]
        public async Task<IActionResult> SaveBranding([FromQuery] string token, [FromBody] SetupBrandingDto model)
        {
            var customer = await GetCustomerByToken(token);
            if (customer == null) return Unauthorized(new { message = "Token inválido." });

            customer.BrandName = model.BrandName?.Trim();
            customer.BrandLogoUrl = model.BrandLogoUrl?.Trim();
            customer.BrandLogoSmallUrl = model.BrandLogoSmallUrl?.Trim();
            customer.BrandPrimaryColor = model.BrandPrimaryColor?.Trim();
            customer.BrandSecondaryColor = model.BrandSecondaryColor?.Trim();
            customer.BrandFaviconUrl = model.BrandFaviconUrl?.Trim();

            await _context.SaveChangesAsync();

            return Ok(new SetupBrandingResponseDto
            {
                BrandName = customer.BrandName,
                BrandLogoUrl = customer.BrandLogoUrl,
                BrandLogoSmallUrl = customer.BrandLogoSmallUrl,
                BrandPrimaryColor = customer.BrandPrimaryColor,
                BrandSecondaryColor = customer.BrandSecondaryColor,
                BrandFaviconUrl = customer.BrandFaviconUrl,
                IsCustomized = customer.BrandName != null || customer.BrandLogoUrl != null
                    || customer.BrandLogoSmallUrl != null || customer.BrandPrimaryColor != null
                    || customer.BrandSecondaryColor != null || customer.BrandFaviconUrl != null
            });
        }

        // ════════════════════════════════════════════════════════════
        // HELPERS PRIVADOS — Mappers
        // ════════════════════════════════════════════════════════════

        private static SetupFunnelResponseDto MapFunnelToDto(Funnel funnel) =>
            new SetupFunnelResponseDto
            {
                FunnelId = funnel.FunnelId,
                Name = funnel.Name,
                OriginatingTemplateId = funnel.OriginatingTemplateId,
                Stages = funnel.Stages.OrderBy(s => s.Order).Select(s => new SetupStageResponseDto
                {
                    StageId = s.StageId,
                    Name = s.Name,
                    Order = s.Order,
                    Color = s.Color
                }).ToList()
            };

        private static SetupScoringResponseDto MapScoringToDto(ScoringModel model, List<LeadTier> tiers, int? originatingTemplateId = null)
        {
            var orderedQuestions = model.Questions.OrderBy(q => q.OrderPosition).ToList();
            return new SetupScoringResponseDto
            {
                ScoringModelId = model.ScoringModelId,
                ModelName = model.Name,
                OriginatingTemplateId = originatingTemplateId,
                Questions = orderedQuestions.Select(q => new SetupScoringQuestionResponseDto
                {
                    QuestionId = q.QuestionId,
                    QuestionText = q.QuestionText,
                    QuestionType = q.QuestionType,
                    IsRequired = q.IsRequired,
                    OrderPosition = q.OrderPosition,
                    AnswerOptions = q.AnswerOptions.OrderBy(a => a.OrderPosition).Select(a => new SetupAnswerOptionResponseDto
                    {
                        AnswerOptionId = a.AnswerOptionId,
                        AnswerText = a.AnswerText,
                        Points = a.Points,
                        OrderPosition = a.OrderPosition
                    }).ToList()
                }).ToList(),
                Tiers = tiers.OrderBy(t => t.MinScore).Select(t => new SetupTierResponseDto
                {
                    TierId = t.TierId,
                    Name = t.Name,
                    MinScore = t.MinScore,
                    MaxScore = t.MaxScore,
                    Color = t.Color
                }).ToList(),
                Rules = model.Rules.OrderBy(r => r.ExecutionOrder).Select(r => new SetupScoringRuleResponseDto
                {
                    RuleId = r.RuleId,
                    Name = r.Name,
                    BonusPoints = r.BonusPoints,
                    ExecutionOrder = r.ExecutionOrder,
                    Conditions = r.Conditions.Select(c =>
                    {
                        var q = orderedQuestions.FirstOrDefault(oq => oq.QuestionId == c.QuestionId);
                        var a = q?.AnswerOptions.FirstOrDefault(oa => oa.AnswerOptionId == c.AnswerOptionId);
                        return new SetupScoringRuleConditionResponseDto
                        {
                            ConditionId = c.ConditionId,
                            ConditionType = c.ConditionType,
                            QuestionId = c.QuestionId,
                            AnswerOptionId = c.AnswerOptionId,
                            LogicOperator = c.LogicOperator,
                            QuestionOrderPosition = q?.OrderPosition,
                            AnswerOrderPosition = a?.OrderPosition,
                            FieldId = c.FieldId,
                            FieldName = c.Field?.FieldName,
                            ConditionValue = c.ConditionValue
                        };
                    }).ToList()
                }).ToList()
            };
        }
    }
}
