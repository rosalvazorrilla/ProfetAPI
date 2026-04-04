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
                .ToListAsync();

            var result = plans.Select(p => new PlanCatalogDto(
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
            )).ToList();

            return Ok(result);
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

        [HttpPut("{id}")]
        [SwaggerOperation(Summary = "Actualizar Plan", Description = "Modifica nombre y descripción del plan. Los precios se actualizan via POST /{id}/prices.")]
        [SwaggerResponse(200, "Plan actualizado")]
        [SwaggerResponse(404, "Plan no encontrado")]
        public async Task<IActionResult> UpdatePlan(int id, [FromBody] UpdatePlanDto model)
        {
            var plan = await _context.Plans.FindAsync(id);
            if (plan == null) return NotFound(new { message = "Plan no encontrado." });

            plan.Name = model.Name;
            plan.Description = model.Description;
            plan.IsPublic = model.IsPublic;
            await _context.SaveChangesAsync();
            return Ok(new { plan.PlanId, plan.Name, plan.Description, plan.IsPublic, plan.IsActive });
        }

        [HttpDelete("{id}")]
        [SwaggerOperation(Summary = "Desactivar Plan", Description = "Marca el plan como inactivo (soft delete). No afecta suscripciones existentes.")]
        [SwaggerResponse(200, "Plan desactivado")]
        [SwaggerResponse(404, "Plan no encontrado")]
        public async Task<IActionResult> DeactivatePlan(int id)
        {
            var plan = await _context.Plans.FindAsync(id);
            if (plan == null) return NotFound(new { message = "Plan no encontrado." });

            plan.IsActive = false;
            await _context.SaveChangesAsync();
            return Ok(new { message = "Plan desactivado." });
        }

        [HttpGet("{id}/prices")]
        [SwaggerOperation(Summary = "Historial de precios del plan", Description = "Devuelve todos los precios del plan ordenados por fecha efectiva descendente.")]
        [SwaggerResponse(200, "Historial de precios", typeof(List<PlanPriceResponseDto>))]
        [SwaggerResponse(404, "Plan no encontrado")]
        public async Task<IActionResult> GetPriceHistory(int id)
        {
            var planExists = await _context.Plans.AnyAsync(p => p.PlanId == id);
            if (!planExists) return NotFound(new { message = "Plan no encontrado." });

            var prices = await _context.PlanPriceHistories
                .Where(p => p.PlanId == id)
                .OrderByDescending(p => p.EffectiveDate)
                .Select(p => new PlanPriceResponseDto(p.PriceHistoryId, p.MonthlyPrice, p.AnnualPrice, p.EffectiveDate, p.CreatedAt))
                .ToListAsync();

            return Ok(prices);
        }

        [HttpPost("{id}/prices")]
        [SwaggerOperation(Summary = "Agregar precio al historial", Description = "Inserta un nuevo precio vigente. GET /api/plans devolverá este precio como el actual.")]
        [SwaggerResponse(201, "Precio agregado", typeof(PlanPriceResponseDto))]
        [SwaggerResponse(404, "Plan no encontrado")]
        public async Task<IActionResult> AddPrice(int id, [FromBody] AddPlanPriceDto model)
        {
            var planExists = await _context.Plans.AnyAsync(p => p.PlanId == id);
            if (!planExists) return NotFound(new { message = "Plan no encontrado." });

            var newPrice = new PlanPriceHistory
            {
                PlanId = id,
                MonthlyPrice = model.MonthlyPrice,
                AnnualPrice = model.AnnualPrice,
                EffectiveDate = model.EffectiveDate ?? DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            _context.PlanPriceHistories.Add(newPrice);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetPriceHistory), new { id },
                new PlanPriceResponseDto(newPrice.PriceHistoryId, newPrice.MonthlyPrice, newPrice.AnnualPrice, newPrice.EffectiveDate, newPrice.CreatedAt));
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

        [HttpPut("features/{featureId}")]
        [SwaggerOperation(Summary = "Actualizar Feature", Description = "Modifica nombre y descripción de una feature global.")]
        [SwaggerResponse(200, "Feature actualizada")]
        [SwaggerResponse(404, "Feature no encontrada")]
        public async Task<IActionResult> UpdateFeature(int featureId, [FromBody] UpdateFeatureDto model)
        {
            var feature = await _context.Features.FindAsync(featureId);
            if (feature == null) return NotFound(new { message = "Feature no encontrada." });

            feature.Name = model.Name;
            feature.Description = model.Description;
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

        [HttpPut("addons/{addonId}")]
        [SwaggerOperation(Summary = "Actualizar AddOn", Description = "Modifica nombre, descripción, precio y ciclo de facturación del addon.")]
        [SwaggerResponse(200, "AddOn actualizado")]
        [SwaggerResponse(404, "AddOn no encontrado")]
        public async Task<IActionResult> UpdateAddOn(int addonId, [FromBody] UpdateAddOnDto model)
        {
            var addon = await _context.AddOns.FindAsync(addonId);
            if (addon == null) return NotFound(new { message = "AddOn no encontrado." });

            addon.Name = model.Name;
            addon.Description = model.Description;
            addon.Price = model.Price;
            addon.BillingCycle = model.BillingCycle;
            await _context.SaveChangesAsync();
            return Ok(addon);
        }

        [HttpDelete("addons/{addonId}")]
        [SwaggerOperation(Summary = "Eliminar AddOn", Description = "Elimina el addon del catálogo. Solo si no tiene instancias contratadas.")]
        [SwaggerResponse(204, "AddOn eliminado")]
        [SwaggerResponse(400, "El addon tiene instancias contratadas activas")]
        [SwaggerResponse(404, "AddOn no encontrado")]
        public async Task<IActionResult> DeleteAddOn(int addonId)
        {
            var addon = await _context.AddOns.Include(a => a.PurchasedInstances).FirstOrDefaultAsync(a => a.AddOnId == addonId);
            if (addon == null) return NotFound(new { message = "AddOn no encontrado." });

            if (addon.PurchasedInstances.Any())
                return BadRequest(new { message = "No se puede eliminar un addon con instancias contratadas activas." });

            _context.AddOns.Remove(addon);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        #endregion
    }
}