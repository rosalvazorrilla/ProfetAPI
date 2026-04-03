using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Dtos
{
    // ── Preview del plan antes de contratar ─────────────────────────────────

    /// <summary>Vista completa del plan para mostrar al admin antes de crear el customer.</summary>
    public class PlanPreviewDto
    {
        [SwaggerSchema("ID del plan.")]
        public int PlanId { get; set; }

        [SwaggerSchema("Nombre del plan.")]
        public string Name { get; set; } = null!;

        [SwaggerSchema("Descripción del plan.")]
        public string? Description { get; set; }

        [SwaggerSchema("Precio mensual vigente (del historial más reciente).")]
        public decimal CurrentMonthlyPrice { get; set; }

        [SwaggerSchema("Precio anual vigente.")]
        public decimal CurrentAnnualPrice { get; set; }

        [SwaggerSchema("Fecha desde la que aplica el precio vigente.")]
        public DateTime PriceEffectiveDate { get; set; }

        [SwaggerSchema("Features incluidas en el plan con sus límites base.")]
        public List<PlanPreviewFeatureDto> Features { get; set; } = new();

        [SwaggerSchema("AddOns disponibles para contratar junto al plan.")]
        public List<PlanPreviewAddOnDto> AvailableAddOns { get; set; } = new();
    }

    public class PlanPreviewFeatureDto
    {
        public int FeatureId { get; set; }
        public string FeatureCode { get; set; } = null!;
        public string Name { get; set; } = null!;

        [SwaggerSchema("Límite base del plan. Se puede sobreescribir al crear el customer.")]
        public string? BaseLimit { get; set; }
    }

    public class PlanPreviewAddOnDto
    {
        public int AddOnId { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public string FeatureCode { get; set; } = null!;

        [SwaggerSchema("Precio base del AddOn. Se puede negociar al crear el customer.")]
        public decimal BasePrice { get; set; }

        public string BillingCycle { get; set; } = null!;
    }

    // ── DTOs para crear/actualizar la suscripción de un customer ────────────

    /// <summary>Datos de la suscripción al crear un customer.</summary>
    public class CreateSubscriptionDto
    {
        [Required]
        [SwaggerSchema("ID del plan base que se contrata.", Nullable = false)]
        public int PlanId { get; set; }

        [Required]
        [SwaggerSchema("Ciclo de facturación: 'Monthly' | 'Annually'.", Nullable = false)]
        public string BillingCycle { get; set; } = "Monthly";

        [SwaggerSchema("Precio acordado con el cliente. Si se omite, usa el precio vigente del plan.")]
        public decimal? PriceAgreed { get; set; }

        [SwaggerSchema("Descuento fijo aplicado al precio acordado.")]
        public decimal DiscountAmount { get; set; } = 0;

        [SwaggerSchema("Estatus inicial: 'Trialing' | 'Active'. Default: Active.")]
        public string Status { get; set; } = "Active";

        [SwaggerSchema("Fecha fin del periodo de prueba (solo si Status = Trialing).")]
        public DateTime? TrialEndDate { get; set; }

        [SwaggerSchema("Overrides de features: límites negociados que difieren del plan base.")]
        public List<FeatureOverrideDto> FeatureOverrides { get; set; } = new();

        [SwaggerSchema("AddOns contratados con precio y cantidad negociados.")]
        public List<PurchasedAddOnDto> AddOns { get; set; } = new();
    }

    public class FeatureOverrideDto
    {
        [Required]
        /// <example>2</example>
        [SwaggerSchema("ID de la feature a sobreescribir.", Nullable = false)]
        public int FeatureId { get; set; }

        [Required]
        /// <example>1000</example>
        [SwaggerSchema("Límite personalizado para este cliente (sobreescribe el del plan).", Nullable = false)]
        public string CustomLimit { get; set; } = null!;
    }

    public class PurchasedAddOnDto
    {
        [Required]
        [SwaggerSchema("ID del AddOn.", Nullable = false)]
        public int AddOnId { get; set; }

        [SwaggerSchema("Cantidad contratada del AddOn.")]
        public int Quantity { get; set; } = 1;

        [SwaggerSchema("Precio negociado por unidad. Si se omite, usa el precio base del AddOn.")]
        public decimal? PricePaid { get; set; }
    }

    // ── Respuesta de la suscripción activa de un customer ───────────────────

    public class SubscriptionDetailDto
    {
        public int SubscriptionId { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; } = null!;
        public int PlanId { get; set; }
        public string PlanName { get; set; } = null!;
        public string Status { get; set; } = null!;
        public string BillingCycle { get; set; } = null!;
        public decimal PriceAgreed { get; set; }
        public decimal DiscountAmount { get; set; }
        public DateTime SubscriptionStartDate { get; set; }
        public DateTime? TrialEndDate { get; set; }

        [SwaggerSchema("Features con el límite efectivo (override si existe, base del plan si no).")]
        public List<SubscriptionFeatureDto> Features { get; set; } = new();

        [SwaggerSchema("AddOns contratados con el precio y cantidad acordados.")]
        public List<SubscriptionAddOnDto> AddOns { get; set; } = new();
    }

    public class SubscriptionFeatureDto
    {
        public int FeatureId { get; set; }
        public string FeatureCode { get; set; } = null!;
        public string FeatureName { get; set; } = null!;
        public string? BaseLimit { get; set; }
        public string? CustomLimit { get; set; }

        [SwaggerSchema("Límite que realmente aplica: CustomLimit si existe, BaseLimit si no.")]
        public string? EffectiveLimit => CustomLimit ?? BaseLimit;

        [SwaggerSchema("True si este customer tiene un límite distinto al del plan.")]
        public bool HasOverride => CustomLimit != null;
    }

    public class SubscriptionAddOnDto
    {
        public int PurchasedAddOnId { get; set; }
        public int AddOnId { get; set; }
        public string AddOnName { get; set; } = null!;
        public string FeatureCode { get; set; } = null!;
        public int Quantity { get; set; }
        public decimal PricePaid { get; set; }
        public DateTime PurchaseDate { get; set; }
    }

    // ── DTO para modificar una suscripción existente ─────────────────────────

    public class UpdateSubscriptionDto
    {
        [SwaggerSchema("Nuevo plan. Si cambia, se actualiza el PlanId en la suscripción.")]
        public int? PlanId { get; set; }

        [SwaggerSchema("Nuevo ciclo de facturación.")]
        public string? BillingCycle { get; set; }

        [SwaggerSchema("Nuevo precio acordado.")]
        public decimal? PriceAgreed { get; set; }

        [SwaggerSchema("Nuevo descuento.")]
        public decimal? DiscountAmount { get; set; }

        [SwaggerSchema("Nuevo estatus: 'Active' | 'Trialing' | 'Canceled'.")]
        public string? Status { get; set; }

        [SwaggerSchema("Reemplaza TODOS los overrides actuales con esta lista.")]
        public List<FeatureOverrideDto>? FeatureOverrides { get; set; }

        [SwaggerSchema("Reemplaza TODOS los AddOns actuales con esta lista.")]
        public List<PurchasedAddOnDto>? AddOns { get; set; }
    }
}
