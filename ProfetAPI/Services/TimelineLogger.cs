using System.Text.Json;
using ProfetAPI.Data;
using ProfetAPI.Models;

namespace ProfetAPI.Services;

/// <summary>Registra eventos en el hilo cronológico (timeline) de un lead/deal.</summary>
public interface ITimelineLogger
{
    Task LogAsync(int accountId, string entityType, long entityId, string type, string title,
        string? detail = null, object? meta = null, string? userId = null);
}

public class TimelineLogger(ApplicationDbContext db, ILogger<TimelineLogger> logger) : ITimelineLogger
{
    public async Task LogAsync(int accountId, string entityType, long entityId, string type, string title,
        string? detail = null, object? meta = null, string? userId = null)
    {
        try
        {
            db.TimelineEvents.Add(new TimelineEvent
            {
                AccountId       = accountId,
                EntityType      = entityType,
                EntityId        = entityId,
                Type            = type,
                Title           = title.Length > 200 ? title[..200] : title,
                Detail          = detail,
                MetaJson        = meta != null ? JsonSerializer.Serialize(meta) : null,
                CreatedByUserId = userId,
                CreatedOn       = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // El timeline nunca debe romper la operación principal
            logger.LogWarning(ex, "No se pudo registrar evento de timeline {Type} para {EntityType} {EntityId}",
                type, entityType, entityId);
        }
    }
}
