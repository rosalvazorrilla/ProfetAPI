using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Dtos.Inbox;
using ProfetAPI.Hubs;
using ProfetAPI.Models;
using ProfetAPI.Services;
using Swashbuckle.AspNetCore.Annotations;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace ProfetAPI.Controllers;

/// <summary>
/// P1: Bandeja unificada — WhatsApp + Email en una sola vista por contacto/lead.
/// Reutiliza la infraestructura existente (2Chat, IEmailService, WhatsAppHub) sin duplicar el envío real.
/// </summary>
[Route("api/inbox")]
[ApiController]
[Authorize]
[SwaggerTag("Bandeja unificada (WhatsApp + Email)")]
public class InboxController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IEmailService _emailService;
    private readonly IInboxAiService _ai;
    private readonly ITimelineLogger _timeline;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IHubContext<WhatsAppHub> _hub;

    private const string TwoChatSendUrl = "https://api.p.2chat.io/open/whatsapp/send-message";

    public InboxController(
        ApplicationDbContext db, IEmailService emailService, IInboxAiService ai,
        ITimelineLogger timeline, IHttpClientFactory httpFactory, IHubContext<WhatsAppHub> hub)
    {
        _db = db; _emailService = emailService; _ai = ai;
        _timeline = timeline; _httpFactory = httpFactory; _hub = hub;
    }

    private string? UserId  => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    private bool    IsAdmin => User.FindFirst(ClaimTypes.Role)?.Value == "AdminGlobal";

    private async Task<int?> ResolveAccountId(int? accountId)
    {
        if (accountId.HasValue)
        {
            if (IsAdmin) return accountId;
            var ok = await _db.AccountInternalUsers.AnyAsync(u => u.AccountId == accountId && u.UserId == UserId);
            return ok ? accountId : null;
        }
        if (IsAdmin) return null;
        return await _db.AccountInternalUsers.Where(u => u.UserId == UserId)
            .Select(u => (int?)u.AccountId).FirstOrDefaultAsync();
    }

    // GET /api/inbox?accountId=&filter=all|unread
    [HttpGet]
    [SwaggerOperation(Summary = "Listar conversaciones (WhatsApp + última actividad de email)")]
    public async Task<IActionResult> List([FromQuery] int? accountId, [FromQuery] string filter = "all")
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound(new { message = "Sin cuenta." });

        var contacts = await _db.ContactsWhatsapp.AsNoTracking()
            .Where(c => c.AccountId == acId && (c.IsArchived == null || c.IsArchived == false))
            .Select(c => new { c.Id, c.FirstName, c.LastName, c.PhoneNumber, c.Email, c.AvatarUrl, c.LeadId })
            .ToListAsync();

        if (contacts.Count == 0) return Ok(Array.Empty<InboxConversationDto>());
        var contactIds = contacts.Select(c => c.Id).ToList();

        var lastWa = await _db.MessagesWhatsapp.AsNoTracking()
            .Where(m => contactIds.Contains(m.ContactId))
            .GroupBy(m => m.ContactId)
            .Select(g => new { ContactId = g.Key, At = g.Max(m => m.CreatedAt), Text = g.OrderByDescending(m => m.CreatedAt).First().MessageText })
            .ToDictionaryAsync(x => x.ContactId, x => x);

        var unreadWa = await _db.MessagesWhatsapp.AsNoTracking()
            .Where(m => contactIds.Contains(m.ContactId) && !m.IsRead && m.Direction == "incoming")
            .GroupBy(m => m.ContactId)
            .Select(g => new { ContactId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ContactId, x => x.Count);

        var leadIds = contacts.Where(c => c.LeadId.HasValue).Select(c => c.LeadId!.Value).ToList();
        var leadIdsInt = leadIds.Select(id => (int)id).ToList();
        var lastEmailByLead = leadIdsInt.Count == 0 ? new Dictionary<int, DateTime>() : await _db.EmailLogs.AsNoTracking()
            .Where(e => e.LeadId != null && leadIdsInt.Contains(e.LeadId.Value))
            .GroupBy(e => e.LeadId!.Value)
            .Select(g => new { LeadId = g.Key, At = g.Max(e => e.SentAt) })
            .ToDictionaryAsync(x => x.LeadId, x => x.At);

        var tierByLead = leadIds.Count == 0 ? new Dictionary<long, (string?, string?)>() : (await _db.Leads.AsNoTracking()
            .Where(l => leadIds.Contains(l.LeadId))
            .Select(l => new { l.LeadId, TierName = l.Tier != null ? l.Tier.Name : null, TierColor = l.Tier != null ? l.Tier.Color : null })
            .ToListAsync()).ToDictionary(x => x.LeadId, x => (x.TierName, x.TierColor));

        var result = contacts.Select(c =>
        {
            lastWa.TryGetValue(c.Id, out var wa);
            var waAt = wa?.At;
            DateTime? emailAt = c.LeadId.HasValue && lastEmailByLead.TryGetValue((int)c.LeadId.Value, out var ea) ? ea : null;
            var lastAt = new[] { waAt, emailAt }.Where(d => d.HasValue).Select(d => d!.Value).DefaultIfEmpty().Max();
            var lastChannel = emailAt.HasValue && (!waAt.HasValue || emailAt > waAt) ? "email" : "whatsapp";
            tierByLead.TryGetValue(c.LeadId ?? -1, out var tier);

            return new InboxConversationDto
            {
                WhatsappContactId  = c.Id,
                LeadId             = c.LeadId,
                Name               = $"{c.FirstName} {c.LastName}".Trim(),
                Phone              = c.PhoneNumber,
                Email              = c.Email,
                AvatarUrl          = c.AvatarUrl,
                LastChannel        = lastChannel,
                LastMessagePreview = wa?.Text,
                LastAt             = lastAt == default ? null : lastAt,
                UnreadCount        = unreadWa.TryGetValue(c.Id, out var uc) ? uc : 0,
                TierName           = tier.Item1,
                TierColor          = tier.Item2,
            };
        })
        .Where(c => filter != "unread" || c.UnreadCount > 0)
        .OrderByDescending(c => c.LastAt)
        .ToList();

        return Ok(result);
    }

    // GET /api/inbox/{contactId}/messages
    [HttpGet("{contactId:int}/messages")]
    [SwaggerOperation(Summary = "Hilo completo (WhatsApp + Email) de una conversación")]
    public async Task<IActionResult> GetMessages(int contactId, [FromQuery] int? accountId)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound();

        var contact = await _db.ContactsWhatsapp.FirstOrDefaultAsync(c => c.Id == contactId && c.AccountId == acId);
        if (contact == null) return NotFound(new { message = "Conversación no encontrada." });

        var waMessages = await _db.MessagesWhatsapp
            .Where(m => m.ContactId == contactId)
            .Select(m => new InboxMessageDto
            {
                Channel = "whatsapp", Direction = m.Direction == "outgoing" ? "out" : "in",
                Body = m.MessageText, MediaUrl = m.MediaUrl, At = m.CreatedAt,
            })
            .ToListAsync();

        var emailMessages = contact.LeadId.HasValue
            ? await _db.EmailLogs.AsNoTracking()
                .Where(e => e.LeadId == contact.LeadId.Value)
                .Select(e => new InboxMessageDto
                {
                    Channel = "email", Direction = "out", Subject = e.Subject, Body = e.BodyHtml, At = e.SentAt,
                })
                .ToListAsync()
            : new List<InboxMessageDto>();

        var merged = waMessages.Concat(emailMessages).OrderBy(m => m.At).ToList();

        // Marcar entrantes de WhatsApp como leídos
        var unread = await _db.MessagesWhatsapp
            .Where(m => m.ContactId == contactId && !m.IsRead && m.Direction == "incoming").ToListAsync();
        if (unread.Count > 0) { unread.ForEach(m => m.IsRead = true); await _db.SaveChangesAsync(); }

        return Ok(merged);
    }

    // POST /api/inbox/{contactId}/reply
    [HttpPost("{contactId:int}/reply")]
    [SwaggerOperation(Summary = "Responder por WhatsApp o Email (reutiliza los canales existentes)")]
    public async Task<IActionResult> Reply(int contactId, [FromQuery] int? accountId, [FromBody] InboxReplyRequestDto dto)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound();

        var contact = await _db.ContactsWhatsapp.FirstOrDefaultAsync(c => c.Id == contactId && c.AccountId == acId);
        if (contact == null) return NotFound(new { message = "Conversación no encontrada." });

        if (dto.Channel == "whatsapp")
        {
            if (string.IsNullOrWhiteSpace(dto.Text)) return BadRequest(new { message = "Escribe un mensaje." });

            var customer = await _db.Customers.FindAsync(contact.CustomerId);
            var apiKey     = customer?.TwoChatApiKey ?? "UAK6e31c29a-c640-4877-81d9-ad67113ec7b5";
            var fromNumber = customer?.WhatsappNumber;
            if (string.IsNullOrEmpty(fromNumber))
                return BadRequest(new { message = "No hay número de WhatsApp configurado para este tenant." });

            var client = _httpFactory.CreateClient();
            client.DefaultRequestHeaders.Add("X-User-API-Key", apiKey);
            var payload = new { to_number = contact.PhoneNumber, from_number = "+" + fromNumber.TrimStart('+'), text = dto.Text };
            var resp = await client.PostAsync(TwoChatSendUrl,
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
            if (!resp.IsSuccessStatusCode)
                return StatusCode(502, new { message = "Error al enviar por WhatsApp." });

            var msg = new MessagesWhatsapp
            {
                ContactId = contact.Id, MessageId = $"sent_{Guid.NewGuid():N}",
                MessageText = dto.Text, Direction = "outgoing", CreatedAt = DateTime.UtcNow, IsRead = true,
            };
            _db.MessagesWhatsapp.Add(msg);
            await _db.SaveChangesAsync();
            await _hub.Clients.Group($"wa_customer_{contact.CustomerId}").SendAsync("NewMessage", contact.Id,
                new { msg.Id, msg.MessageText, msg.Direction, msg.CreatedAt, IsMine = true });
        }
        else if (dto.Channel == "email")
        {
            var to = contact.Email;
            if (string.IsNullOrWhiteSpace(to) && contact.LeadId.HasValue)
                to = await _db.Leads.Where(l => l.LeadId == contact.LeadId.Value).Select(l => l.Email).FirstOrDefaultAsync();
            if (string.IsNullOrWhiteSpace(to)) return BadRequest(new { message = "El contacto no tiene correo." });
            if (string.IsNullOrWhiteSpace(dto.Subject) || string.IsNullOrWhiteSpace(dto.BodyHtml))
                return BadRequest(new { message = "Asunto y mensaje son obligatorios." });

            var (success, error) = await _emailService.SendAsync(to, dto.Subject, dto.BodyHtml);
            _db.EmailLogs.Add(new EmailLog
            {
                AccountId = acId, LeadId = contact.LeadId.HasValue ? (int)contact.LeadId.Value : null,
                SentByUserId = UserId, ToAddress = to, Subject = dto.Subject, BodyHtml = dto.BodyHtml,
                SentAt = DateTime.UtcNow, IsSuccess = success, ErrorMessage = error,
            });
            await _db.SaveChangesAsync();
            if (!success) return StatusCode(502, new { message = error ?? "Error al enviar el correo." });
        }
        else return BadRequest(new { message = "Canal inválido." });

        if (contact.LeadId.HasValue)
            await _timeline.LogAsync(acId.Value, "Lead", contact.LeadId.Value, "message_out",
                dto.Channel == "email" ? "Email enviado" : "WhatsApp enviado", userId: UserId);

        return Ok(new { sent = true });
    }

    // POST /api/inbox/{contactId}/summarize
    [HttpPost("{contactId:int}/summarize")]
    [SwaggerOperation(Summary = "Resumen de la conversación (IA)")]
    public async Task<IActionResult> Summarize(int contactId, [FromQuery] int? accountId)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound();
        if (!_ai.IsConfigured) return Ok(new InboxSummaryDto { Available = false });

        var thread = await BuildThreadTextAsync(contactId, acId.Value);
        var summary = await _ai.SummarizeAsync(thread);
        return Ok(new InboxSummaryDto { Available = !string.IsNullOrWhiteSpace(summary), Summary = summary });
    }

    // POST /api/inbox/{contactId}/suggest-reply
    [HttpPost("{contactId:int}/suggest-reply")]
    [SwaggerOperation(Summary = "Sugerencia de respuesta editable (IA) — no se envía sola")]
    public async Task<IActionResult> SuggestReply(int contactId, [FromQuery] int? accountId)
    {
        var acId = await ResolveAccountId(accountId);
        if (acId == null) return NotFound();
        if (!_ai.IsConfigured) return Ok(new InboxSuggestedReplyDto { Available = false });

        var contact = await _db.ContactsWhatsapp.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contactId && c.AccountId == acId);
        if (contact == null) return NotFound();

        var thread = await BuildThreadTextAsync(contactId, acId.Value);
        var reply = await _ai.SuggestReplyAsync(thread, $"{contact.FirstName} {contact.LastName}".Trim());
        return Ok(new InboxSuggestedReplyDto { Available = !string.IsNullOrWhiteSpace(reply), Reply = reply });
    }

    private async Task<string> BuildThreadTextAsync(int contactId, int accountId)
    {
        var contact = await _db.ContactsWhatsapp.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contactId && c.AccountId == accountId);
        if (contact == null) return "";

        var waMessages = await _db.MessagesWhatsapp.AsNoTracking()
            .Where(m => m.ContactId == contactId).OrderByDescending(m => m.CreatedAt).Take(30)
            .Select(m => new { m.Direction, m.MessageText, m.CreatedAt }).ToListAsync();

        var emailMessages = contact.LeadId.HasValue
            ? await _db.EmailLogs.AsNoTracking().Where(e => e.LeadId == contact.LeadId.Value)
                .OrderByDescending(e => e.SentAt).Take(10)
                .Select(e => new { e.Subject, e.BodyHtml, e.SentAt }).ToListAsync()
            : new();

        var sb = new StringBuilder();
        foreach (var m in waMessages.OrderBy(m => m.CreatedAt))
            sb.AppendLine($"[WhatsApp {(m.Direction == "outgoing" ? "Agente" : "Cliente")} {m.CreatedAt:yyyy-MM-dd HH:mm}] {m.MessageText}");
        foreach (var e in emailMessages.OrderBy(e => e.SentAt))
            sb.AppendLine($"[Email Agente {e.SentAt:yyyy-MM-dd HH:mm}] {e.Subject}: {System.Text.RegularExpressions.Regex.Replace(e.BodyHtml ?? "", "<[^>]+>", " ")}");
        return sb.ToString();
    }
}
