using Swashbuckle.AspNetCore.Annotations; // <--- AGREGAR ESTO SIEMPRE

namespace ProfetAPI.Dtos
{
    [SwaggerSchema(Title = "Respuesta de Login", Description = "Contiene el token de acceso y datos básicos del usuario.")]
    public class LoginResponseDto
    {
        [SwaggerSchema("Token JWT (Bearer). Debe enviarse en el Header 'Authorization' de futuras peticiones.")]
        public string Token { get; set; } = string.Empty;

        [SwaggerSchema("Fecha y hora exacta en la que el token dejará de funcionar.")]
        public DateTime Expiration { get; set; }

        public string UserId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        [SwaggerSchema("El rol principal del usuario (ej. 'Admin', 'Vendedor').")]
        public string Role { get; set; } = string.Empty;
    }
}