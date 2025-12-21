using System.ComponentModel.DataAnnotations;
using Swashbuckle.AspNetCore.Annotations; // <--- NO OLVIDAR ESTE USING

namespace ProfetAPI.Dtos
{
    [SwaggerSchema(Title = "Formulario de Creación de Usuario", Description = "Datos requeridos para registrar un nuevo usuario en el sistema.")]
    public class CreateUserDto
    {
        // --- CAMPOS OBLIGATORIOS ---

        [Required(ErrorMessage = "El correo es obligatorio")]
        [EmailAddress(ErrorMessage = "Formato de correo inválido")]
        [SwaggerSchema("Correo electrónico corporativo o personal.")]
        public string Email { get; set; } = null!;

        [Required(ErrorMessage = "La contraseña es obligatoria")]
        [MinLength(6, ErrorMessage = "La contraseña debe tener al menos 6 caracteres")]
        [SwaggerSchema("Clave de acceso. Mínimo 6 caracteres.")]
        public string Password { get; set; } = null!;

        [Required(ErrorMessage = "El nombre es obligatorio")]
        public string FirstName { get; set; } = null!;

        [Required(ErrorMessage = "El teléfono es obligatorio")]
        [SwaggerSchema("Teléfono a 10 dígitos (Móvil o Fijo).")]
        public string Phone { get; set; } = null!;


        // --- CAMPOS OPCIONALES ---

        [SwaggerSchema("Apellido del usuario (Opcional).")]
        public string? LastName { get; set; }

        [SwaggerSchema("Rol asignado. Si se omite, el sistema asignará 'User' o 'Vendedor' por defecto.")]
        public string? Role { get; set; }

        [SwaggerSchema("ID de la empresa cliente. OBLIGATORIO para usuarios normales. Dejar NULL solo si es un SuperAdmin del sistema.")]
        public int? CustomerId { get; set; }
    }
}