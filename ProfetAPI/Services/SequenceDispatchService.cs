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

public class SequenceDispatchService(
    ApplicationDbContext db,
    IEmailService emailService,
    IWhatsAppService whatsAppService,
    ITimelineLogger timeline,
    PlaybookService playbooks,
    ILogger<SequenceDispatchService> logger) : ISequenceDispatchService
{
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
            result = await SendToLeadAsync(lead, template);
        }
        else if (task.EntityType == "Deal" && task.EntityId.HasValue)
        {
            var deal = await db.Deals.Include(d => d.PrimaryContact).Include(d => d.Company)
                .FirstOrDefaultAsync(d => d.DealId == task.EntityId.Value);
            if (deal == null) return false;
            result = await SendToDealAsync(deal, template);
        }
        else
        {
            return false;
        }

        if (!result.success)
        {
            logger.LogWarning("Envío automático de secuencia falló (tarea {TaskId}): {Error}", task.ActivityId, result.error);
            return false;
        }

        task.TaskStatus     = "Completada";
        task.IsCompleted    = true;
        task.ResolutionNote = "Enviado automáticamente por la secuencia";
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

    public async Task<(bool success, string? error)> SendToLeadAsync(Lead lead, MessageTemplate template)
    {
        var fields = new Dictionary<string, string>
        {
            ["nombre"]   = lead.Name ?? "",
            ["empresa"]  = lead.Company ?? "",
            ["email"]    = lead.Email ?? "",
            ["telefono"] = lead.Phone ?? "",
            ["ciudad"]   = lead.City ?? "",
            ["cargo"]    = lead.Position ?? "",
        };
        return await SendAsync(lead.AccountId, template, fields, lead.Email, lead.Phone);
    }

    private async Task<(bool success, string? error)> SendToDealAsync(Deal deal, MessageTemplate template)
    {
        var contactName = $"{deal.PrimaryContact?.FirstName} {deal.PrimaryContact?.LastName}".Trim();
        var fields = new Dictionary<string, string>
        {
            ["nombre"]   = string.IsNullOrWhiteSpace(contactName) ? deal.DealName : contactName,
            ["empresa"]  = deal.Company?.Name ?? "",
            ["email"]    = deal.PrimaryContact?.Email ?? "",
            ["telefono"] = deal.PrimaryContact?.PhoneNumber ?? "",
            ["ciudad"]   = "",
            ["cargo"]    = deal.PrimaryContact?.Position ?? "",
        };
        return await SendAsync(deal.AccountId, template, fields, deal.PrimaryContact?.Email, deal.PrimaryContact?.PhoneNumber);
    }

    private async Task<(bool success, string? error)> SendAsync(
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
