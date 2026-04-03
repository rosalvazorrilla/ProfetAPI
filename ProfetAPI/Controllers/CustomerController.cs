using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Dtos;
using ProfetAPI.Models;
using Swashbuckle.AspNetCore.Annotations;

namespace ProfetAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "AdminGlobal")]
    [SwaggerTag("Gestión de Clientes (Admin Global)")]
    public class CustomersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly string _frontendBaseUrl = "http://localhost:3000";

        public CustomersController(ApplicationDbContext context)
        {
            _context = context;
        }

        // ── GET api/customers ────────────────────────────────────────────────

        [HttpGet]
        [SwaggerOperation(
            Summary = "Listar todos los clientes activos",
            Description = "Devuelve todos los customers donde Deleted == false."
        )]
        [SwaggerResponse(200, "Lista de clientes", typeof(List<CustomerResponseDto>))]
        public async Task<IActionResult> GetAll()
        {
            var customers = await _context.Customers
                .Where(c => c.Deleted == false)
                .Select(c => new CustomerResponseDto(
                    c.Id, c.Name, c.Contact, c.Email, c.Status,
                    $"{_frontendBaseUrl}/setup?token={c.SetupToken}"
                ))
                .ToListAsync();

            return Ok(customers);
        }

        // ── GET api/customers/5 ──────────────────────────────────────────────

        [HttpGet("{id}")]
        [SwaggerOperation(Summary = "Obtener detalle de un cliente")]
        [SwaggerResponse(200, "Detalle del cliente", typeof(CustomerResponseDto))]
        [SwaggerResponse(404, "No encontrado")]
        public async Task<IActionResult> GetById(int id)
        {
            var customer = await _context.Customers
                .Where(c => c.Id == id && c.Deleted == false)
                .Select(c => new CustomerResponseDto(
                    c.Id, c.Name, c.Contact, c.Email, c.Status,
                    $"{_frontendBaseUrl}/setup?token={c.SetupToken}"
                ))
                .FirstOrDefaultAsync();

            if (customer == null)
                return NotFound(new { message = "El cliente no existe o fue eliminado." });

            return Ok(customer);
        }

        // ── POST api/customers ───────────────────────────────────────────────

        [HttpPost]
        [SwaggerOperation(
            Summary = "Crear un nuevo cliente",
            Description = "Crea el Customer, su Subscription, los overrides de features negociados y los AddOns contratados — todo en una sola transacción atómica. Si PriceAgreed se omite, usa el precio vigente del plan."
        )]
        [SwaggerResponse(201, "Cliente creado", typeof(CustomerResponseDto))]
        [SwaggerResponse(400, "Datos inválidos")]
        [SwaggerResponse(404, "Plan o AddOn no encontrado")]
        public async Task<IActionResult> Create([FromBody] CreateCustomerDto model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            // Verificar que el plan exista y esté activo
            var plan = await _context.Plans
                .Include(p => p.PlanPriceHistories)
                .FirstOrDefaultAsync(p => p.PlanId == model.Subscription.PlanId && p.IsActive);

            if (plan == null)
                return NotFound(new { message = $"El plan con ID {model.Subscription.PlanId} no existe o está inactivo." });

            // Precio vigente del plan como default
            var currentPrice = plan.PlanPriceHistories
                .OrderByDescending(ph => ph.EffectiveDate)
                .FirstOrDefault();

            var defaultPrice = model.Subscription.BillingCycle == "Annually"
                ? (currentPrice?.AnnualPrice ?? 0)
                : (currentPrice?.MonthlyPrice ?? 0);

            // Verificar que los AddOns existan
            var addOnIds = model.Subscription.AddOns.Select(a => a.AddOnId).ToList();
            if (addOnIds.Any())
            {
                var foundAddOns = await _context.AddOns
                    .Where(a => addOnIds.Contains(a.AddOnId))
                    .ToListAsync();

                var missing = addOnIds.Except(foundAddOns.Select(a => a.AddOnId)).ToList();
                if (missing.Any())
                    return NotFound(new { message = $"AddOn(s) no encontrados: {string.Join(", ", missing)}" });
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1. Crear Customer
                var customer = new Customer
                {
                    Name = model.Name,
                    Contact = model.Contact,
                    Email = model.Email,
                    Phone = model.Phone,
                    InitialDate = DateTime.UtcNow,
                    Active = true,
                    Deleted = false,
                    SetupToken = Guid.NewGuid().ToString("N"),
                    SetupStep = 1,
                    Status = "Pendiente de Setup"
                };
                _context.Customers.Add(customer);
                await _context.SaveChangesAsync();

                // 2. Crear Subscription
                var subscription = new Subscription
                {
                    CustomerId = customer.Id,
                    PlanId = model.Subscription.PlanId,
                    Status = model.Subscription.Status,
                    BillingCycle = model.Subscription.BillingCycle,
                    PriceAgreed = model.Subscription.PriceAgreed ?? defaultPrice,
                    DiscountAmount = model.Subscription.DiscountAmount,
                    SubscriptionStartDate = DateTime.UtcNow,
                    TrialEndDate = model.Subscription.TrialEndDate
                };
                _context.Subscriptions.Add(subscription);
                await _context.SaveChangesAsync();

                // 3. Feature Overrides (límites negociados distintos al plan base)
                foreach (var fo in model.Subscription.FeatureOverrides)
                {
                    _context.SubscriptionFeatureOverrides.Add(new SubscriptionFeatureOverride
                    {
                        SubscriptionId = subscription.SubscriptionId,
                        FeatureId = fo.FeatureId,
                        CustomLimit = fo.CustomLimit
                    });
                }

                // 4. AddOns contratados
                foreach (var addOnDto in model.Subscription.AddOns)
                {
                    var baseAddOn = await _context.AddOns.FindAsync(addOnDto.AddOnId);
                    _context.CustomerPurchasedAddOns.Add(new CustomerPurchasedAddOn
                    {
                        SubscriptionId = subscription.SubscriptionId,
                        AddOnId = addOnDto.AddOnId,
                        Quantity = addOnDto.Quantity,
                        PricePaid = addOnDto.PricePaid ?? baseAddOn!.Price,
                        PurchaseDate = DateTime.UtcNow
                    });
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                var setupUrl = $"{_frontendBaseUrl}/setup?token={customer.SetupToken}";
                return CreatedAtAction(nameof(GetById), new { id = customer.Id },
                    new CustomerResponseDto(customer.Id, customer.Name, customer.Contact, customer.Email, customer.Status, setupUrl));
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { message = "Error al crear el cliente.", details = ex.Message });
            }
        }

        // ── PUT api/customers/5 ──────────────────────────────────────────────

        [HttpPut("{id}")]
        [SwaggerOperation(
            Summary = "Actualizar datos básicos del cliente",
            Description = "Modifica Nombre, Contacto y Teléfono. No afecta el token ni el estatus."
        )]
        [SwaggerResponse(200, "Cliente actualizado", typeof(CustomerResponseDto))]
        [SwaggerResponse(404, "No encontrado")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateCustomerDto model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Id == id && c.Deleted == false);
            if (customer == null)
                return NotFound(new { message = "El cliente no existe o fue eliminado." });

            customer.Name = model.Name;
            customer.Contact = model.Contact;
            customer.Phone = model.Phone;
            await _context.SaveChangesAsync();

            return Ok(new CustomerResponseDto(customer.Id, customer.Name, customer.Contact, customer.Email, customer.Status,
                $"{_frontendBaseUrl}/setup?token={customer.SetupToken}"));
        }

        // ── GET api/customers/5/subscription ────────────────────────────────

        [HttpGet("{id}/subscription")]
        [SwaggerOperation(
            Summary = "Ver suscripción activa del cliente",
            Description = "Devuelve el plan, precio acordado, overrides de features (con el límite efectivo calculado) y AddOns contratados."
        )]
        [SwaggerResponse(200, "Suscripción activa", typeof(SubscriptionDetailDto))]
        [SwaggerResponse(404, "Cliente o suscripción no encontrados")]
        public async Task<IActionResult> GetSubscription(int id)
        {
            var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Id == id && c.Deleted == false);
            if (customer == null)
                return NotFound(new { message = "Cliente no encontrado." });

            var subscription = await _context.Subscriptions
                .Include(s => s.Plan).ThenInclude(p => p.PlanFeatures).ThenInclude(pf => pf.Feature)
                .Include(s => s.FeatureOverrides).ThenInclude(fo => fo.Feature)
                .Include(s => s.PurchasedAddOns).ThenInclude(pa => pa.AddOn).ThenInclude(a => a.Feature)
                .FirstOrDefaultAsync(s => s.CustomerId == id && s.Status != "Canceled");

            if (subscription == null)
                return NotFound(new { message = "El cliente no tiene suscripción activa." });

            // Construir features con límite efectivo
            var overrideMap = subscription.FeatureOverrides.ToDictionary(fo => fo.FeatureId, fo => fo.CustomLimit);

            var features = subscription.Plan.PlanFeatures.Select(pf => new SubscriptionFeatureDto
            {
                FeatureId = pf.FeatureId,
                FeatureCode = pf.Feature.FeatureCode,
                FeatureName = pf.Feature.Name,
                BaseLimit = pf.Limit,
                CustomLimit = overrideMap.TryGetValue(pf.FeatureId, out var cl) ? cl : null
            }).ToList();

            var dto = new SubscriptionDetailDto
            {
                SubscriptionId = subscription.SubscriptionId,
                CustomerId = customer.Id,
                CustomerName = customer.Name,
                PlanId = subscription.PlanId,
                PlanName = subscription.Plan.Name,
                Status = subscription.Status,
                BillingCycle = subscription.BillingCycle,
                PriceAgreed = subscription.PriceAgreed,
                DiscountAmount = subscription.DiscountAmount,
                SubscriptionStartDate = subscription.SubscriptionStartDate,
                TrialEndDate = subscription.TrialEndDate,
                Features = features,
                AddOns = subscription.PurchasedAddOns.Select(pa => new SubscriptionAddOnDto
                {
                    PurchasedAddOnId = pa.PurchasedAddOnId,
                    AddOnId = pa.AddOnId,
                    AddOnName = pa.AddOn.Name,
                    FeatureCode = pa.AddOn.Feature.FeatureCode,
                    Quantity = pa.Quantity,
                    PricePaid = pa.PricePaid,
                    PurchaseDate = pa.PurchaseDate
                }).ToList()
            };

            return Ok(dto);
        }

        // ── PUT api/customers/5/subscription ────────────────────────────────

        [HttpPut("{id}/subscription")]
        [SwaggerOperation(
            Summary = "Modificar suscripción del cliente",
            Description = "Permite cambiar plan, precio, ciclo, overrides y AddOns. Los overrides y AddOns se reemplazan completamente si se envían (omitir para no modificarlos)."
        )]
        [SwaggerResponse(200, "Suscripción actualizada", typeof(SubscriptionDetailDto))]
        [SwaggerResponse(404, "Cliente o suscripción no encontrados")]
        public async Task<IActionResult> UpdateSubscription(int id, [FromBody] UpdateSubscriptionDto model)
        {
            var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Id == id && c.Deleted == false);
            if (customer == null)
                return NotFound(new { message = "Cliente no encontrado." });

            var subscription = await _context.Subscriptions
                .Include(s => s.FeatureOverrides)
                .Include(s => s.PurchasedAddOns)
                .FirstOrDefaultAsync(s => s.CustomerId == id && s.Status != "Canceled");

            if (subscription == null)
                return NotFound(new { message = "El cliente no tiene suscripción activa." });

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Actualizar campos básicos
                if (model.PlanId.HasValue) subscription.PlanId = model.PlanId.Value;
                if (!string.IsNullOrEmpty(model.BillingCycle)) subscription.BillingCycle = model.BillingCycle;
                if (model.PriceAgreed.HasValue) subscription.PriceAgreed = model.PriceAgreed.Value;
                if (model.DiscountAmount.HasValue) subscription.DiscountAmount = model.DiscountAmount.Value;
                if (!string.IsNullOrEmpty(model.Status)) subscription.Status = model.Status;

                // Reemplazar overrides si se envían
                if (model.FeatureOverrides != null)
                {
                    _context.SubscriptionFeatureOverrides.RemoveRange(subscription.FeatureOverrides);
                    foreach (var fo in model.FeatureOverrides)
                    {
                        _context.SubscriptionFeatureOverrides.Add(new SubscriptionFeatureOverride
                        {
                            SubscriptionId = subscription.SubscriptionId,
                            FeatureId = fo.FeatureId,
                            CustomLimit = fo.CustomLimit
                        });
                    }
                }

                // Reemplazar AddOns si se envían
                if (model.AddOns != null)
                {
                    _context.CustomerPurchasedAddOns.RemoveRange(subscription.PurchasedAddOns);
                    foreach (var addOnDto in model.AddOns)
                    {
                        var baseAddOn = await _context.AddOns.FindAsync(addOnDto.AddOnId);
                        if (baseAddOn == null)
                            return NotFound(new { message = $"AddOn {addOnDto.AddOnId} no encontrado." });

                        _context.CustomerPurchasedAddOns.Add(new CustomerPurchasedAddOn
                        {
                            SubscriptionId = subscription.SubscriptionId,
                            AddOnId = addOnDto.AddOnId,
                            Quantity = addOnDto.Quantity,
                            PricePaid = addOnDto.PricePaid ?? baseAddOn.Price,
                            PurchaseDate = DateTime.UtcNow
                        });
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return await GetSubscription(id);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { message = "Error al actualizar la suscripción.", details = ex.Message });
            }
        }
    }
}
