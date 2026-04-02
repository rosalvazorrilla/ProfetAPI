using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Dtos;
using ProfetAPI.Models;
using Swashbuckle.AspNetCore.Annotations; // <--- AGREGADO

namespace ProfetAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "AdminGlobal")] // Solo accesible para los administradores del sistema
    [SwaggerTag("Gestión de Clientes (Admin Global)")] // <--- TÍTULO DE SECCIÓN
    public class CustomersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        // La URL idealmente viene de _configuration["FrontendUrl"], aquí la dejo fija por el momento
        private readonly string _frontendBaseUrl = "http://localhost:3000";

        public CustomersController(ApplicationDbContext context)
        {
            _context = context;
        }

        // POST: api/customers
        [HttpPost]
        // --- DOCUMENTACIÓN DEL MÉTODO ---
        [SwaggerOperation(
            Summary = "Crear un nuevo cliente (Empresa)",
            Description = "Crea un registro de cliente, autogenera el token de Setup y prepara la cuenta."
        )]
        [SwaggerResponse(201, "Cliente creado exitosamente", typeof(CustomerResponseDto))]
        [SwaggerResponse(400, "Faltan datos obligatorios")]
        [SwaggerResponse(401, "No autorizado")]
        public async Task<IActionResult> Create([FromBody] CreateCustomerDto model)
        {
            // 1. Validar el modelo
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (string.IsNullOrWhiteSpace(model.Email) || string.IsNullOrWhiteSpace(model.Contact))
                return BadRequest(new { message = "El correo y el contacto son obligatorios." });

            // 2. Preparar el objeto Customer
            Customer customer = new Customer()
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

            // 3. Guardar en BD
            _context.Customers.Add(customer);
            await _context.SaveChangesAsync();

            // 4. Preparar respuesta
            var setupUrl = $"{_frontendBaseUrl}/setup?token={customer.SetupToken}";

            var responseDto = new CustomerResponseDto(
                customer.Id,
                customer.Name,
                customer.Contact,
                customer.Email,
                customer.Status,
                setupUrl
            );

            return CreatedAtAction(nameof(GetById), new { id = customer.Id }, responseDto);
        }

        // GET: api/customers
        [HttpGet]
        [SwaggerOperation(
            Summary = "Listar todos los clientes activos",
            Description = "Obtiene la lista de todos los clientes donde Deleted == false."
        )]
        [SwaggerResponse(200, "Lista de clientes devuelta exitosamente", typeof(List<CustomerResponseDto>))]
        [SwaggerResponse(401, "No autorizado")]
        public async Task<IActionResult> GetAll()
        {
            var customers = await _context.Customers
                .Where(c => c.Deleted == false)
                .Select(c => new CustomerResponseDto(
                    c.Id,
                    c.Name,
                    c.Contact,
                    c.Email,
                    c.Status,
                    $"{_frontendBaseUrl}/setup?token={c.SetupToken}"
                ))
                .ToListAsync();

            return Ok(customers);
        }

        // GET: api/customers/{id}
        [HttpGet("{id}")]
        [SwaggerOperation(
            Summary = "Obtener el detalle de un cliente",
            Description = "Busca un cliente por su ID. Retorna 404 si no existe o está eliminado."
        )]
        [SwaggerResponse(200, "Detalle del cliente", typeof(CustomerResponseDto))]
        [SwaggerResponse(404, "El cliente no existe o fue eliminado")]
        public async Task<IActionResult> GetById(int id)
        {
            var customer = await _context.Customers
                .Where(c => c.Id == id && c.Deleted == false)
                .Select(c => new CustomerResponseDto(
                    c.Id,
                    c.Name,
                    c.Contact,
                    c.Email,
                    c.Status,
                    $"{_frontendBaseUrl}/setup?token={c.SetupToken}"
                ))
                .FirstOrDefaultAsync();

            if (customer == null)
                return NotFound(new { message = "El cliente no existe o fue eliminado." });

            return Ok(customer);
        }

        // PUT: api/customers/{id}
        [HttpPut("{id}")]
        [SwaggerOperation(
            Summary = "Actualizar datos básicos de un cliente",
            Description = "Permite la edición de Nombre, Contacto y Teléfono. No afecta el token ni el estatus."
        )]
        [SwaggerResponse(200, "Cliente actualizado exitosamente", typeof(CustomerResponseDto))]
        [SwaggerResponse(400, "Faltan datos obligatorios")]
        [SwaggerResponse(404, "El cliente no existe o fue eliminado")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateCustomerDto model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Id == id && c.Deleted == false);

            if (customer == null)
                return NotFound(new { message = "El cliente no existe o fue eliminado." });

            // Actualizar datos
            customer.Name = model.Name;
            customer.Contact = model.Contact;
            customer.Phone = model.Phone;

            await _context.SaveChangesAsync();

            var responseDto = new CustomerResponseDto(
                customer.Id,
                customer.Name,
                customer.Contact,
                customer.Email,
                customer.Status,
                $"{_frontendBaseUrl}/setup?token={customer.SetupToken}"
            );

            return Ok(responseDto);
        }
    }
}