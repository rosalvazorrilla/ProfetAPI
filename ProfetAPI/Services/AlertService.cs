using System.Net;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;

namespace ProfetAPI.Services;

public enum AlertType { DealWon, TaskDue }

public interface IAlertService
{
    /// <summary>Manda una alerta a un usuario respetando lo que eligió: dentro del sistema, por correo, ambos o ninguno.</summary>
    Task SendAsync(string userId, AlertType type, string message, string? url = null, string? entityType = null, long? entityId = null);
}

public class AlertService(ApplicationDbContext db, INotificationService notify, IEmailService email, ILogger<AlertService> logger) : IAlertService
{
    public async Task SendAsync(string userId, AlertType type, string message, string? url = null, string? entityType = null, long? entityId = null)
    {
        var s = await db.UserNotificationSettings.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId);
        var system = type == AlertType.DealWon ? s?.DealWonSystem ?? true : s?.TaskDueSystem ?? true;
        var byEmail = type == AlertType.DealWon ? s?.DealWonEmail ?? false : s?.TaskDueEmail ?? false;

        if (system) await notify.NotifyAsync(userId, message, url, entityType, entityId);

        if (byEmail)
        {
            try
            {
                var to = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync();
                if (!string.IsNullOrWhiteSpace(to))
                {
                    var subject = type == AlertType.DealWon ? "Profet · Trato ganado" : "Profet · Tarea por vencer";
                    await email.SendAsync(to, subject, $"<p>{WebUtility.HtmlEncode(message)}</p>");
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "No se pudo mandar el correo de alerta a {UserId}", userId);
            }
        }
    }
}
