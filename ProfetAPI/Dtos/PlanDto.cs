using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Dtos
{
    // --- LECTURA ---
    public record PlanCatalogDto(
        int PlanId,
        string Name,
        string? Description,
        decimal MonthlyPrice, 
        decimal AnnualPrice,
        List<PlanFeatureDto> Features
    );

    public record PlanFeatureDto(
        string FeatureCode,
        string Name,
        string? Limit
    );

    public record AddOnCatalogDto(
        int AddOnId,
        string Name,
        string? Description,
        decimal Price,
        string BillingCycle,
        string FeatureCode
    );

    // --- ESCRITURA ---
    public record CreatePlanDto(
        [Required] string Name,
        string? Description,
        [Required] decimal InitialMonthlyPrice,
        [Required] decimal InitialAnnualPrice,
        bool IsPublic = true
    );

    public record UpdatePlanPriceDto(
        [Required] decimal MonthlyPrice,
        [Required] decimal AnnualPrice
    );

    public record CreateFeatureDto(
        [Required] string FeatureCode,
        [Required] string Name,
        string? Description
    );

    public record SetPlanFeatureDto(
        [Required] int FeatureId,
        string? Limit
    );

    public record CreateAddOnDto(
        [Required] string Name,
        string? Description,
        [Required] decimal Price,
        [Required] string BillingCycle,
        [Required] int FeatureId
    );
}