using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Models;

namespace ProfetAPI.Services;

/// <summary>
/// Registro de cada intento de crear un prospecto desde un canal externo
/// (API Key, compat de landing pages viejas, webhooks) — éxito o error, con
/// el motivo. Reusa la tabla dbo.Logs ya existente y visible en
/// Admin Global → Logs (filtrable por Type="LeadIngestion" / "LeadIngestionError"),
/// en vez de crear una tabla y una pantalla nuevas.
///
/// Se le pasa su propio scope de DI (igual que LeadAssignmentService/
/// AutomationExecutorService.FireAsync) porque normalmente se llama desde un
/// bloque catch — el DbContext de la request puede haber quedado en mal
/// estado tras la excepción original, y esto NUNCA debe tapar o reemplazar
/// ese error con uno propio.
/// </summary>
public class IngestionLogger(IServiceScopeFactory scopeFactory, ILogger<IngestionLogger> logger)
{
    public async Task LogAsync(string channel, string message, bool success = true)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Logs.Add(new Log
            {
                Date    = DateTime.UtcNow,
                Name    = channel,
                Message = message.Length > 2000 ? message[..2000] : message,
                Type    = success ? "LeadIngestion" : "LeadIngestionError",
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // El log es best-effort — si falla, no debe tumbar la ingesta real.
            logger.LogWarning(ex, "No se pudo escribir el log de ingesta de leads.");
        }
    }
}
