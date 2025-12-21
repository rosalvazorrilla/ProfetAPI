using System.ComponentModel.DataAnnotations;
using Swashbuckle.AspNetCore.Annotations; // <--- Importante para que funcionen las notas

namespace ProfetAPI.Dtos
{
    [SwaggerSchema(Title = "Credenciales de Acceso", Description = "Paquete de datos necesario para iniciar sesión y obtener el Token JWT.")]
    public class LoginDto
    {
        [Required(ErrorMessage = "El correo es obligatorio.")]
        [EmailAddress(ErrorMessage = "Formato de correo inválido.")]
        [SwaggerSchema("El correo electrónico registrado del usuario (ej. usuario@empresa.com).")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "La contraseña es obligatoria.")]
        [SwaggerSchema("La contraseña en texto plano (Se enviará cifrada vía HTTPS).")]
        public string Password { get; set; } = string.Empty;
    }
}