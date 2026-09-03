using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel.DataAnnotations;

namespace ProfetAPI.Dtos
{
    public class CreateCustomerDto
    {
        [Required]
        [SwaggerSchema("Nombre de la empresa cliente.", Nullable = false)]
        public string Name { get; set; } = null!;

        [Required]
        [SwaggerSchema("Nombre del contacto al que se envía el link de setup.", Nullable = false)]
        public string Contact { get; set; } = null!;

        [Required]
        [EmailAddress]
        [SwaggerSchema("Correo del contacto (recibe el link de setup).", Nullable = false)]
        public string Email { get; set; } = null!;

        [SwaggerSchema("Teléfono de contacto.")]
        public string? Phone { get; set; }

        [Required]
        [SwaggerSchema("Suscripción que se contrata al crear el cliente.", Nullable = false)]
        public CreateSubscriptionDto Subscription { get; set; } = null!;

        [SwaggerSchema("IDs de los usuarios internos (rol PM) a asignar como responsables de este cliente.")]
        public List<string>? PmUserIds { get; set; }
    }

    public record PmSummaryDto(string Id, string Name);

    public class SetCustomerPmsDto
    {
        public List<string> PmUserIds { get; set; } = new();
    }

    public record CustomerResponseDto(
        int Id,
        string Name,
        string? Contact,
        string? Email,
        string Status,
        string SetupUrl,
        string? SetupToken = null,
        string? SetupAccessCode = null,
        List<PmSummaryDto>? Pms = null,
        int SetupStep = 0,
        string? PlanName = null,
        DateTime? CreatedAt = null
    );

    public record UpdateCustomerDto(
        string Name,
        string Contact,
        string? Phone
    );
}
