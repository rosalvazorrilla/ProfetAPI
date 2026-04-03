using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Dtos;
using ProfetAPI.Models;
using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "AdminGlobal")]
    [SwaggerTag("Administración Maestra de Suscripciones (Profet_new)")]
    public class PlansController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public PlansController(ApplicationDbContext context)
        {
            _context = context;
        }

        #region GESTIÓN DE PLANES Y PRECIOS

        [HttpGet]
        [SwaggerOperation(Summary = "Catálogo de Planes", Description = "Lista planes con precios actuales y sus límites técnicos.")]
        [SwaggerResponse(200, "Catálogo recuperado", typeof(List<PlanCatalogDto>))]
        public async Task<IActionResult> GetCatalog()
        {
            var plans = await _context.Plans
                .Include(p => p.PlanFeatures).ThenInclude(pf => pf.Feature)
                .Include(p => p.PlanPriceHistories)
                .Where(p => p.IsActive)
                .Select(p => new PlanCatalogDto(
                    p.PlanId,
                    p.Name,
                    p.Description,
                    p.PlanPriceHistories.OrderByDescending(ph => ph.EffectiveDate).Select(ph => ph.MonthlyPrice).FirstOrDefault(),
                    p.PlanPriceHistories.OrderByDescending(ph => ph.EffectiveDate).Select(ph => ph.AnnualPrice).FirstOrDefault(),
                    p.PlanFeatures.Select(pf => new PlanFeatureDto(
                        pf.Feature.FeatureCode,
                        pf.Feature.Name,
                        pf.Limit
                    )).ToList()
                ))
                .ToListAsync();

            return Ok(plans);
        }

        [HttpPost]
        [SwaggerOperation(Summary = "Crear Plan", Description = "Registra un plan y su primer precio en el historial.")]
        [SwaggerResponse(201, "Plan creado")]
        public async Task<IActionResult> CreatePlan([FromBody] CreatePlanDto model)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var plan = new Plan {
                    Name = model.Name,
                    Description = model.Description,
                    IsPublic = model.IsPublic,
                    IsActive = true
                };
                _context.Plans.Add(plan);
                await _context.SaveChangesAsync();

                var price = new PlanPriceHistory {
                    PlanId = plan.PlanId,
                    MonthlyPrice = model.InitialMonthlyPrice,
                    AnnualPrice = model.InitialAnnualPrice,
                    EffectiveDate = DateTime.UtcNow
                };
                _context.PlanPriceHistories.Add(price);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
                return CreatedAtAction(nameof(GetCatalog), new { id = plan.PlanId }, plan);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { message = "Error en la transacción", details = ex.Message });
            }
        }

        [HttpPost("{id}/prices")]
        [SwaggerOperation(Summary = "Actualizar Precios del Plan", Description = "Añade una nueva entrada al historial de precios.")]
        public async Task<IActionResult> UpdatePrice(int id, [FromBody] UpdatePlanPriceDto model)
        {
            var planExists = await _context.Plans.AnyAsync(p => p.PlanId == id);
            if (!planExists) return NotFound(new { message = "Plan no encontrado" });

            var newPrice = new PlanPriceHistory {
                PlanId = id,
                MonthlyPrice = model.MonthlyPrice,
                AnnualPrice = model.AnnualPrice,
                EffectiveDate = DateTime.UtcNow
            };

            _context.PlanPriceHistories.Add(newPrice);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Historial de precios actualizado" });
        }

        #endregion

        // GET: api/plans/5/preview
        [HttpGet("{id}/preview")]
        [SwaggerOperation(
            Summary = "Preview completo del plan",
            Description = "Devuelve precio vigente, features con límites base y addons disponibles. Usar antes de crear un customer para mostrar los valores por defecto que el admin puede personalizar."
        )]
        [SwaggerResponse(200, "Preview del plan", typeof(PlanPreviewDto))]
        [SwaggerResponse(404, "Plan no encontrado")]
        public async Task<IActionResult> GetPreview(int id)
        {
            var plan = await _context.Plans
                .Include(p => p.PlanPriceHistories)
                .Include(p => p.PlanFeatures).ThenInclude(pf => pf.Feature)
                .FirstOrDefaultAsync(p => p.PlanId == id && p.IsActive);

            if (plan == null)
                return NotFound(new { message = "Plan no encontrado o inactivo." });

            // Precio vigente = el más reciente en el historial
            var currentPrice = plan.PlanPriceHistories
                .OrderByDescending(ph => ph.EffectiveDate)
                .FirstOrDefault();

            // AddOns disponibles: todos los que están vinculados a las features del plan
            var featureIds = plan.PlanFeatures.Select(pf => pf.FeatureId).ToList();
            var addOns = await _context.AddOns
                .Include(a => a.Feature)
                .Where(a => featureIds.Contains(a.FeatureId))
                .ToListAsync();

            var preview = new PlanPreviewDto
            {
                PlanId = plan.PlanId,
                Name = plan.Name,
                Description = plan.Description,
                CurrentMonthlyPrice = currentPrice?.MonthlyPrice ?? 0,
                CurrentAnnualPrice = currentPrice?.AnnualPrice ?? 0,
                PriceEffectiveDate = currentPrice?.EffectiveDate ?? DateTime.UtcNow,
                Features = plan.PlanFeatures.Select(pf => new PlanPreviewFeatureDto
                {
                    FeatureId = pf.FeatureId,
                    FeatureCode = pf.Feature.FeatureCode,
                    Name = pf.Feature.Name,
                    BaseLimit = pf.Limit
                }).ToList(),
                AvailableAddOns = addOns.Select(a => new PlanPreviewAddOnDto
                {
                    AddOnId = a.AddOnId,
                    Name = a.Name,
                    Description = a.Description,
                    FeatureCode = a.Feature.FeatureCode,
                    BasePrice = a.Price,
                    BillingCycle = a.BillingCycle
                }).ToList()
            };

            return Ok(preview);
        }

        #region GESTIÓN DE CARACTERÍSTICAS (FEATURES)

        [HttpGet("features")]
        [SwaggerOperation(Summary = "Catálogo de Features Globales")]
        public async Task<IActionResult> GetFeatures() => Ok(await _context.Features.ToListAsync());

        [HttpPost("features")]
        [SwaggerOperation(Summary = "Crear Feature", Description = "Define una nueva capacidad del sistema (ej: MAX_LEADS).")]
        public async Task<IActionResult> CreateFeature([FromBody] CreateFeatureDto model)
        {
            var feature = new Feature { 
                FeatureCode = model.FeatureCode, 
                Name = model.Name, 
                Description = model.Description 
            };
            _context.Features.Add(feature);
            await _context.SaveChangesAsync();
            return Ok(feature);
        }

        [HttpPost("{planId}/features")]
        [SwaggerOperation(Summary = "Configurar Límite en Plan", Description = "Asigna o actualiza el límite de una Feature para un Plan específico.")]
        public async Task<IActionResult> SetPlanFeature(int planId, [FromBody] SetPlanFeatureDto model)
        {
            var planFeature = await _context.PlanFeatures
                .FirstOrDefaultAsync(pf => pf.PlanId == planId && pf.FeatureId == model.FeatureId);

            if (planFeature == null) {
                planFeature = new PlanFeature { 
                    PlanId = planId, 
                    FeatureId = model.FeatureId, 
                    Limit = model.Limit 
                };
                _context.PlanFeatures.Add(planFeature);
            } else {
                planFeature.Limit = model.Limit;
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Matriz de límites actualizada" });
        }

        #endregion

        #region GESTIÓN DE ADDONS

        [HttpGet("addons")]
        [SwaggerOperation(Summary = "Listar AddOns")]
        public async Task<IActionResult> GetAddOns()
        {
            var addons = await _context.AddOns
                .Include(a => a.Feature)
                .Select(a => new AddOnCatalogDto(
                    a.AddOnId, a.Name, a.Description, a.Price, a.BillingCycle, a.Feature.FeatureCode
                ))
                .ToListAsync();
            return Ok(addons);
        }

        [HttpPost("addons")]
        [SwaggerOperation(Summary = "Crear AddOn", Description = "Registra un servicio extra vinculado a una Feature.")]
        public async Task<IActionResult> CreateAddOn([FromBody] CreateAddOnDto model)
        {
            var addon = new AddOn {
                Name = model.Name,
                Description = model.Description,
                Price = model.Price,
                BillingCycle = model.BillingCycle,
                FeatureId = model.FeatureId
            };
            _context.AddOns.Add(addon);
            await _context.SaveChangesAsync();
            return Ok(addon);
        }

        #endregion
    }
}