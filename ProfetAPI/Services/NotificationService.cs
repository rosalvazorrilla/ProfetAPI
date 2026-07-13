using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Models;

namespace ProfetAPI.Services;

/// <summary>Emite notificaciones in-app por usuario, sobre el modelo Notification existente.</summary>
public interface INotificationService
{
    Task NotifyAsync(string userId, string message, string? url = null,
        string? entityType = null, long? entityId = null);

    /// <summary>Notifica a todos los usuarios internos de una cuenta.</summary>
    Task NotifyAccountAsync(int accountId, string message, string? url = null,
        string? entityType = null, long? entityId = null, string? excludeUserId = null);
}

public class NotificationService(ApplicationDbContext db, ILogger<NotificationService> logger) : INotificationService
{
    public async Task NotifyAsync(string userId, string message, string? url = null,
        string? entityType = null, long? entityId = null)
    {
        if (string.IsNullOrWhiteSpace(userId)) return;
        try
        {
            db.Notifications.Add(new Notification
            {
                UserId     = userId,
                Message    = message.Length > 500 ? message[..500] : message,
                URL        = url,
                Status     = false,           // false = no leída
                Date       = DateTime.UtcNow,
                EntityType = entityType,
                EntityId   = entityId,
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo crear notificación para {UserId}", userId);
        }
    }

    public async Task NotifyAccountAsync(int accountId, string message, string? url = null,
        string? entityType = null, long? entityId = null, string? excludeUserId = null)
    {
        var userIds = await db.AccountInternalUsers.AsNoTracking()
            .Where(u => u.AccountId == accountId && u.UserId != excludeUserId)
            .Select(u => u.UserId).Distinct().ToListAsync();

        foreach (var uid in userIds)
            db.Notifications.Add(new Notification
            {
                UserId = uid, Message = message, URL = url, Status = false,
                Date = DateTime.UtcNow, EntityType = entityType, EntityId = entityId,
            });

        if (userIds.Count > 0)
            try { await db.SaveChangesAsync(); }
            catch (Exception ex) { logger.LogWarning(ex, "No se pudieron crear notificaciones de cuenta {AccountId}", accountId); }
    }
}
