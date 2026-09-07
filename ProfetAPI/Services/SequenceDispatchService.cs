using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Models;

namespace ProfetAPI.Services;

/// <summary>
/// Envía de verdad el mensaje de un paso de secuencia marcado como "Automatico" —
/// pieza compartida entre el despachador diario (SequenceAutomationJob) y el envío
/// masivo manual (POST /api/leads/bulk-message). Nunca decide SI hay que mandar algo,
/// solo CÓMO: arma las variables, interpola la plantilla, manda por el canal correcto,
/// y deja el rastro (Activity + timeline).
/// </summary>
public interface ISequenceDispatchService
{
    /// <summary>Manda el mensaje de una tarea generada por playbook y, si tuvo éxito,
    /// la marca completada y avanza la secuencia. Retorna false si no se pudo enviar
    /// (la tarea queda abierta para reintentar en la siguiente corrida).</summary>
    Task<bool> SendTaskMessageAsync(Activity task, PlaybookTask step);

    /// <summary>Envío ad-hoc a un lead puntual (seguimiento comercial masivo) — no viene
    /// de un paso de secuencia, así que no toca ninguna Activity.</summary>
    Task<(bool success, string? error)> SendToLeadAsync(Lead lead, MessageTemplate template);
}

/// <summary>Cuántas fallas seguidas del envío automático de una misma tarea se toleran
/// antes de dejar de reintentarla sola y avisarle a la cuenta.</summary>
public class SequenceDispatchService(
    ApplicationDbContext db,
    IEmailService emailService,
    IWhatsAppService whatsAppService,
    ITimelineLogger timeline,
    INotificationService notify,
    PlaybookService playbooks,
    ILogger<SequenceDispatchService> logger) : ISequenceDispatchService
{
    public const int MaxAutomationFailures = 3;

    public async Task<bool> SendTaskMessageAsync(Activity task, PlaybookTask step)
    {
        if (step.TemplateId == null) return false;
        var template = await db.MessageTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.TemplateId == step.TemplateId);
        if (template == null || !template.IsActive) return false;

        (bool success, string? error) result;

        if (task.EntityType == "Lead" && task.EntityId.HasValue)
        {
            var lead = await db.Leads.FirstOrDefaultAsync(l => l.LeadId == task.EntityId.Value);
            if (lead == null) return false;
            result = await SendAsync(lead.AccountId, template, BuildLeadFields(lead), lead.Email, lead.Phone, "Lead", lead.LeadId);
        }
        else if (task.EntityType == "Deal" && task.EntityId.HasValue)
        {
            var deal = await db.Deals.Include(d => d.PrimaryContact).Include(d => d.Company)
                .FirstOrDefaultAsync(d => d.DealId == task.EntityId.Value);
            if (deal == null) return false;
            result = await SendAsync(deal.AccountId, template, BuildDealFields(deal), deal.PrimaryContact?.Email, deal.PrimaryContact?.PhoneNumber, "Deal", deal.DealId);
        }
        else
        {
            return false;
        }

        if (!result.success)
        {
            logger.LogWarning("Envío automático de secuencia falló (tarea {TaskId}): {Error}", task.ActivityId, result.error);
            task.AutomationFailCount++;
            if (task.AutomationFailCount >= MaxAutomationFailures)
            {
                task.ResolutionNote = $"Envío automático falló {task.AutomationFailCount} veces seguidas ({result.error}) — requiere revisión manual.";
                if (task.AccountId.HasValue)
                    await notify.NotifyAccountAsync(task.AccountId.Value,
                        $"El envío automático de \"{task.Subject}\" falló y ya no se reintentará solo — revísalo.",
                        entityType: task.EntityType, entityId: task.EntityId);
            }
            await db.SaveChangesAsync();
            return false;
        }

        task.TaskStatus          = "Completada";
        task.IsCompleted         = true;
        task.ResolutionNote      = "Enviado automáticamente por la secuencia";
        task.AutomationFailCount = 0;
        await db.SaveChangesAsync();
        await playbooks.AdvanceNextDueDateAsync(task);

        if (task.AccountId.HasValue && task.EntityType != null && task.EntityId.HasValue)
        {
            await timeline.LogAsync(task.AccountId.Value, task.EntityType, task.EntityId.Value,
                "sequence_auto_send", $"Enviado automáticamente: \"{task.Subject}\"",
                detail: $"Plantilla: {template.Name}");
        }
        return true;
    }

    public async Task<(bool success, string? error)> SendToLeadAsync(Lead lead, MessageTemplate template) =>
        await SendAsync(lead.AccountId, template, BuildLeadFields(lead), lead.Email, lead.Phone, "Lead", lead.LeadId);

    private static Dictionary<string, string> BuildLeadFields(Lead lead) => new()
    {
        ["nombre"]   = lead.Name ?? "",
        ["empresa"]  = lead.Company ?? "",
        ["email"]    = lead.Email ?? "",
        ["telefono"] = lead.Phone ?? "",
        ["ciudad"]   = lead.City ?? "",
        ["cargo"]    = lead.Position ?? "",
    };

    private static Dictionary<string, string> BuildDealFields(Deal deal)
    {
        var contactName = $"{deal.PrimaryContact?.FirstName} {deal.PrimaryContact?.LastName}".Trim();
        return new Dictionary<string, string>
        {
            ["nombre"]   = string.IsNullOrWhiteSpace(contactName) ? deal.DealName : contactName,
            ["empresa"]  = deal.Company?.Name ?? "",
            ["email"]    = deal.PrimaryContact?.Email ?? "",
            ["telefono"] = deal.PrimaryContact?.PhoneNumber ?? "",
            ["ciudad"]   = "",
            ["cargo"]    = deal.PrimaryContact?.Position ?? "",
        };
    }

    private async Task<(bool success, string? error)> SendAsync(
        int? accountId, MessageTemplate template, Dictionary<string, string> fields, string? email, string? phone,
        string entityType, long entityId)
    {
        var result = await SendCoreAsync(accountId, template, fields, email, phone);
        if (accountId.HasValue)
        {
            db.AutomationSendLogs.Add(new AutomationSendLog
            {
                AccountId    = accountId.Value,
                EntityType   = entityType,
                EntityId     = entityId,
                Channel      = template.Channel,
                TemplateId   = template.TemplateId,
                TemplateName = template.Name,
                Success      = result.success,
                Error        = result.error,
            });
            await db.SaveChangesAsync();
        }
        return result;
    }

    private async Task<(bool success, string? error)> SendCoreAsync(
        int? accountId, MessageTemplate template, Dictionary<string, string> fields, string? email, string? phone)
    {
        if (accountId == null) return (false, "Sin cuenta.");

        if (template.Channel == "Email")
        {
            if (string.IsNullOrWhiteSpace(email)) return (false, "El prospecto no tiene correo.");

            var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.AccountId == accountId);
            SmtpConfig? config = null;
            if (account?.SmtpEnabled == true && account.SmtpIsVerified == true && !string.IsNullOrWhiteSpace(account.SmtpHost))
            {
                config = new SmtpConfig(
                    Host: account.SmtpHost!, Port: account.SmtpPort ?? 587,
                    User: account.SmtpUser ?? "", Password: account.SmtpPassword ?? "",
                    FromAddress: account.SmtpFromAddress ?? "", FromName: account.SmtpFromName ?? "CRM",
                    EnableSsl: account.SmtpEnableSsl ?? true, IsCustom: true);
            }

            return await emailService.SendAsync(email, Interpolate(template.Subject ?? "", fields), Interpolate(template.Body, fields), config: config);
        }

        if (template.Channel == "WhatsApp")
        {
            if (string.IsNullOrWhiteSpace(phone)) return (false, "El prospecto no tiene teléfono.");
            var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.AccountId == accountId);
            if (account == null) return (false, "Cuenta no encontrada.");
            return await whatsAppService.SendAsync(account.CustomerId, phone, Interpolate(template.Body, fields));
        }

        return (false, "Canal de plantilla desconocido.");
    }

    private static string Interpolate(string template, Dictionary<string, string> fields) =>
        Regex.Replace(template, @"\{\{(\w+)\}\}", m => fields.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
}
