using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Models;
using Swashbuckle.AspNetCore.Annotations;

namespace ProfetAPI.Controllers
{
    /// <summary>
    /// Gestión de branding global de la plataforma (logo grande, logo pequeño, colores).
    /// Los endpoints públicos sirven los valores por defecto; los de admin requieren JWT.
    /// </summary>
    [Route("api/branding")]
    [ApiController]
    [SwaggerTag("Branding — Marca global de la plataforma")]
    public class BrandingController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public BrandingController(ApplicationDbContext context)
        {
            _context = context;
        }

        // ── Helper — garantiza que siempre exista la fila única ──────────────

        private async Task<GlobalBranding> GetOrCreateGlobal()
        {
            var row = await _context.GlobalBranding.FirstOrDefaultAsync();
            if (row == null)
            {
                row = new GlobalBranding { Id = 1 };
                _context.GlobalBranding.Add(row);
                await _context.SaveChangesAsync();
            }
            return row;
        }

        // ════════════════════════════════════════════════════════════
        // PÚBLICO — Sin autenticación (usado por la app para cargar defaults)
        // ════════════════════════════════════════════════════════════

        // GET /api/branding/defaults
        [HttpGet("defaults")]
        [AllowAnonymous]
        [SwaggerOperation(
            Summary = "Obtener branding global de la plataforma (público)",
            Description = "Sin autenticación. Devuelve los logos y colores por defecto del sistema. Usar como fallback cuando el tenant no tiene marca configurada."
        )]
        [SwaggerResponse(200, "Branding global")]
        public async Task<IActionResult> GetDefaults()
        {
            var row = await GetOrCreateGlobal();
            return Ok(MapToResponse(row));
        }

        // ════════════════════════════════════════════════════════════
        // ME — Branding resuelto para el usuario autenticado
        // GET /api/branding/me  (requiere JWT)
        // Retorna: brand del customer del usuario, con fallback a global
        // AdminGlobal/sin customer → siempre devuelve global
        // ════════════════════════════════════════════════════════════

        [HttpGet("me")]
        [Authorize]
        [SwaggerOperation(
            Summary = "Branding resuelto para el usuario actual",
            Description = "Devuelve la marca del customer al que pertenece el usuario. Si el customer no tiene marca configurada (o el usuario es AdminGlobal), devuelve el branding global como fallback."
        )]
        [SwaggerResponse(200, "Branding resuelto")]
        [SwaggerResponse(401, "No autenticado")]
        public async Task<IActionResult> GetMe()
        {
            var global = await GetOrCreateGlobal();

            // Obtener customerId del usuario autenticado
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                      ?? User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userId))
                return Ok(MapToResponse(global));

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user?.CustomerId == null)
                return Ok(MapToResponse(global)); // AdminGlobal u otro sin customer

            var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Id == user.CustomerId);
            if (customer == null)
                return Ok(MapToResponse(global));

            // Resolver: usa customer si tiene algo, si no usa global como fallback campo a campo
            return Ok(new
            {
                appName        = customer.BrandName        ?? global.AppName,
                logoLargeUrl   = customer.BrandLogoUrl     ?? global.LogoLargeUrl,
                logoSmallUrl   = customer.BrandLogoSmallUrl?? global.LogoSmallUrl,
                primaryColor   = customer.BrandPrimaryColor?? global.PrimaryColor,
                secondaryColor = customer.BrandSecondaryColor ?? global.SecondaryColor,
                faviconUrl     = customer.BrandFaviconUrl  ?? global.FaviconUrl,
                source         = customer.BrandLogoUrl != null ? "customer" : "global",
            });
        }

        // ════════════════════════════════════════════════════════════
        // ADMIN — Requiere JWT (AdminGlobal)
        // ════════════════════════════════════════════════════════════

        // GET /api/branding/admin
        [HttpGet("admin")]
        [Authorize]
        [SwaggerOperation(Summary = "Obtener branding global (admin)", Description = "Requiere JWT de AdminGlobal.")]
        [SwaggerResponse(200, "Branding global")]
        [SwaggerResponse(401, "No autenticado")]
        public async Task<IActionResult> GetAdmin()
        {
            var row = await GetOrCreateGlobal();
            return Ok(MapToResponse(row));
        }

        // PUT /api/branding/admin
        [HttpPut("admin")]
        [Authorize]
        [SwaggerOperation(
            Summary = "Actualizar branding global (admin)",
            Description = "Actualiza nombre, colores y URLs de logo. Los campos null eliminan la personalización."
        )]
        [SwaggerResponse(200, "Branding actualizado")]
        [SwaggerResponse(401, "No autenticado")]
        public async Task<IActionResult> UpdateAdmin([FromBody] GlobalBrandingDto model)
        {
            var row = await GetOrCreateGlobal();

            row.AppName        = model.AppName?.Trim();
            row.LogoLargeUrl   = model.LogoLargeUrl?.Trim();
            row.LogoSmallUrl   = model.LogoSmallUrl?.Trim();
            row.PrimaryColor   = model.PrimaryColor?.Trim();
            row.SecondaryColor = model.SecondaryColor?.Trim();
            row.FaviconUrl     = model.FaviconUrl?.Trim();

            await _context.SaveChangesAsync();
            return Ok(MapToResponse(row));
        }

        // POST /api/branding/admin/upload?type=logo-large|logo-small|favicon
        [HttpPost("admin/upload")]
        [Authorize]
        [Consumes("multipart/form-data")]
        [SwaggerOperation(
            Summary = "Subir imagen de branding global (admin)",
            Description = "Sube logo grande, logo pequeño o favicon del sistema. Máx 2 MB. Tipos: logo-large, logo-small, favicon."
        )]
        [SwaggerResponse(200, "URL pública del archivo subido")]
        [SwaggerResponse(400, "Archivo inválido")]
        [SwaggerResponse(401, "No autenticado")]
        public async Task<IActionResult> UploadAdmin([FromQuery] string type, IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "No se recibió ningún archivo." });

            if (file.Length > 2 * 1024 * 1024)
                return BadRequest(new { message = "El archivo no puede superar 2 MB." });

            var allowedMimes = new[] {
                "image/png", "image/jpeg", "image/jpg", "image/svg+xml",
                "image/x-icon", "image/vnd.microsoft.icon", "image/webp"
            };
            if (!allowedMimes.Contains(file.ContentType.ToLower()))
                return BadRequest(new { message = "Solo se permiten imágenes (PNG, JPG, SVG, ICO, WebP)." });

            var validTypes = new[] { "logo-large", "logo-small", "favicon" };
            if (!validTypes.Contains(type))
                return BadRequest(new { message = "El parámetro 'type' debe ser: logo-large, logo-small o favicon." });

            var extension = Path.GetExtension(file.FileName).ToLower();
            var folder = Path.Combine("wwwroot", "uploads", "branding", "global");
            Directory.CreateDirectory(folder);

            var fileName = $"{type}{extension}";
            var filePath = Path.Combine(folder, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
                await file.CopyToAsync(stream);

            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var publicUrl = $"{baseUrl}/uploads/branding/global/{fileName}";

            // Actualizar la fila automáticamente
            var row = await GetOrCreateGlobal();
            if (type == "logo-large")   row.LogoLargeUrl  = publicUrl;
            if (type == "logo-small")   row.LogoSmallUrl  = publicUrl;
            if (type == "favicon")      row.FaviconUrl    = publicUrl;
            await _context.SaveChangesAsync();

            return Ok(new { url = publicUrl, type });
        }

        // ── Mapper ───────────────────────────────────────────────────────────

        private static object MapToResponse(GlobalBranding row) => new
        {
            appName        = row.AppName,
            logoLargeUrl   = row.LogoLargeUrl,
            logoSmallUrl   = row.LogoSmallUrl,
            primaryColor   = row.PrimaryColor,
            secondaryColor = row.SecondaryColor,
            faviconUrl     = row.FaviconUrl,
        };
    }

    // ── DTO ──────────────────────────────────────────────────────────────────

    public class GlobalBrandingDto
    {
        public string? AppName        { get; set; }
        public string? LogoLargeUrl   { get; set; }
        public string? LogoSmallUrl   { get; set; }
        public string? PrimaryColor   { get; set; }
        public string? SecondaryColor { get; set; }
        public string? FaviconUrl     { get; set; }
    }
}
