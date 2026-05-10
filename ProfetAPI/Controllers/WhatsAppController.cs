using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Hubs;
using ProfetAPI.Models;
using ProfetAPI.Services;
using Swashbuckle.AspNetCore.Annotations;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProfetAPI.Controllers;

/// <summary>
/// Módulo de WhatsApp — integración con 2Chat API.
/// Gestiona contactos, conversaciones, mensajes y respuestas rápidas.
/// El webhook de 2Chat llama a POST /api/whatsapp/webhook (incoming) y
/// POST /api/whatsapp/webhook/sent (outgoing).
/// </summary>
[ApiController]
[Route("api/whatsapp")]
public class WhatsAppController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<WhatsAppController> _logger;
    private readonly IHubContext<WhatsAppHub> _hub;
    private readonly TwoChatService _twoChatService;

    // 2Chat endpoint para envío
    private const string TwoChatSendUrl = "https://api.p.2chat.io/open/whatsapp/send-message";

    public WhatsAppController(ApplicationDbContext db, IHttpClientFactory httpFactory,
        ILogger<WhatsAppController> logger, IHubContext<WhatsAppHub> hub, TwoChatService twoChatService)
    {
        _db = db;
        _httpFactory = httpFactory;
        _logger = logger;
        _hub = hub;
        _twoChatService = twoChatService;
    }

    // ====================================================================
    // CONTACTOS
    // ====================================================================

    /// <summary>
    /// Lista contactos de WhatsApp del tenant desde ContactsWhatsapps.
    /// accountId: filtra por Account. Si es 0 o no se envía → devuelve los SIN asignar (AccountId IS NULL).
    /// includeAll: si true, ignora el filtro de account y devuelve todos.
    /// </summary>
    [HttpGet("contacts")]
    [Authorize]
    [SwaggerOperation(Summary = "Listar contactos de WhatsApp", Tags = new[] { "WhatsApp" })]
    public async Task<IActionResult> GetContacts(
        [FromQuery] int  customerId,
        [FromQuery] int? accountId       = null,
        [FromQuery] bool includeArchived = false,
        [FromQuery] bool includeAll      = false)
    {
        var query = _db.ContactsWhatsapp
            .Where(c => c.CustomerId == customerId);

        // Filtro por account: si includeAll=false, filtramos
        if (!includeAll)
        {
            if (accountId.HasValue && accountId.Value > 0)
                query = query.Where(c => c.AccountId == accountId.Value);
            else
                query = query.Where(c => c.AccountId == null); // sin asignar
        }

        if (!includeArchived)
            query = query.Where(c => c.IsArchived == null || c.IsArchived == false);

        // Subconsultas de mensajes con manejo seguro si la tabla no existe aún
        var lastMsgByContact   = new Dictionary<int, DateTime>();
        var unreadByContact    = new Dictionary<int, int>();
        var lastTextByContact  = new Dictionary<int, string?>();

        try
        {
            lastMsgByContact = await _db.MessagesWhatsapp
                .GroupBy(m => m.ContactId)
                .Select(g => new { ContactId = g.Key, LastAt = g.Max(m => m.CreatedAt) })
                .ToDictionaryAsync(x => x.ContactId, x => x.LastAt);

            unreadByContact = await _db.MessagesWhatsapp
                .Where(m => !m.IsRead && m.Direction == "incoming")
                .GroupBy(m => m.ContactId)
                .Select(g => new { ContactId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ContactId, x => x.Count);

            lastTextByContact = await _db.MessagesWhatsapp
                .Where(m => m.MessageText != null)
                .GroupBy(m => m.ContactId)
                .Select(g => new { ContactId = g.Key, Text = g.OrderByDescending(m => m.CreatedAt).First().MessageText })
                .ToDictionaryAsync(x => x.ContactId, x => x.Text);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("MessagesWhatsapps table not ready yet: {Msg}. Run Querys.sql migration.", ex.Message);
        }

        var contacts = await query
            .Select(c => new
            {
                ContactId = c.Id,
                c.FirstName,
                c.LastName,
                FullName = (c.FirstName + " " + c.LastName).Trim(),
                c.PhoneNumber,
                c.AvatarUrl,
                IsArchived = c.IsArchived ?? false,
                c.Email,
                c.CustomerId,
                c.AccountId,
            })
            .ToListAsync();

        var result = contacts
            .Select(c => new
            {
                c.ContactId,
                c.FirstName,
                c.LastName,
                c.FullName,
                c.PhoneNumber,
                c.AvatarUrl,
                c.IsArchived,
                c.Email,
                c.CustomerId,
                c.AccountId,
                LastMessageAt   = lastMsgByContact.TryGetValue(c.ContactId, out var la) ? la : (DateTime?)null,
                UnreadCount     = unreadByContact.TryGetValue(c.ContactId, out var uc) ? uc : 0,
                LastMessageText = lastTextByContact.TryGetValue(c.ContactId, out var lt) ? lt : null,
            })
            .OrderByDescending(c => c.LastMessageAt)
            .ToList();

        return Ok(result);
    }

    /// <summary>Crea un contacto de WhatsApp manualmente en ContactsWhatsapps.</summary>
    [HttpPost("contacts")]
    [Authorize]
    [SwaggerOperation(Summary = "Crear contacto de WhatsApp", Tags = new[] { "WhatsApp" })]
    public async Task<IActionResult> CreateContact([FromBody] CreateWhatsAppContactDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.PhoneNumber))
            return BadRequest(new { message = "El número de teléfono es obligatorio." });

        // Verificar duplicado por teléfono + tenant
        var exists = await _db.ContactsWhatsapp
            .AnyAsync(c => c.PhoneNumber == dto.PhoneNumber && c.CustomerId == dto.CustomerId);

        if (exists)
            return Conflict(new { message = "Ya existe un contacto con ese número en este tenant." });

        var waContact = new ContactWhatsapp
        {
            FirstName  = dto.FirstName,
            LastName   = dto.LastName,
            PhoneNumber= dto.PhoneNumber,
            Email      = dto.Email,
            CustomerId = dto.CustomerId,
            AccountId  = dto.AccountId,
            CreatedAt  = DateTime.UtcNow,
        };

        _db.ContactsWhatsapp.Add(waContact);
        await _db.SaveChangesAsync();

        return Ok(new { contactId = waContact.Id, message = "Contacto creado." });
    }

    /// <summary>Asigna (o desasigna) el contacto WA a una Account.</summary>
    [HttpPatch("contacts/{contactId}/assign")]
    [Authorize]
    [SwaggerOperation(Summary = "Asignar contacto a Account", Tags = new[] { "WhatsApp" })]
    public async Task<IActionResult> AssignToAccount(int contactId, [FromBody] AssignAccountDto dto)
    {
        var contact = await _db.ContactsWhatsapp.FindAsync(contactId);
        if (contact == null) return NotFound();
        contact.AccountId = dto.AccountId; // null = desasignar
        await _db.SaveChangesAsync();
        return Ok(new { message = dto.AccountId.HasValue ? "Contacto asignado." : "Contacto desasignado." });
    }

    /// <summary>Archiva o desarchiva un contacto.</summary>
    [HttpPatch("contacts/{contactId}/archive")]
    [Authorize]
    [SwaggerOperation(Summary = "Archivar / desarchivar contacto", Tags = new[] { "WhatsApp" })]
    public async Task<IActionResult> ToggleArchive(int contactId, [FromBody] ToggleArchiveDto dto)
    {
        var contact = await _db.ContactsWhatsapp.FindAsync(contactId);
        if (contact == null) return NotFound();

        contact.IsArchived = dto.IsArchived;
        await _db.SaveChangesAsync();

        return Ok(new { message = dto.IsArchived ? "Contacto archivado." : "Contacto desarchivado." });
    }

    // ====================================================================
    // CONVERSACIÓN
    // ====================================================================

    /// <summary>Mensajes de la conversación con un contacto, ordenados cronológicamente.</summary>
    [HttpGet("conversation/{contactId}")]
    [Authorize]
    [SwaggerOperation(Summary = "Obtener conversación", Tags = new[] { "WhatsApp" })]
    public async Task<IActionResult> GetConversation(int contactId)
    {
        try
        {
            var messages = await _db.MessagesWhatsapp
                .Where(m => m.ContactId == contactId)
                .OrderBy(m => m.CreatedAt)
                .Select(m => new
                {
                    m.Id,
                    m.MessageText,
                    m.MediaUrl,
                    m.MediaType,
                    m.MimeType,
                    m.Direction,
                    m.CreatedAt,
                    m.IsRead,
                    IsMine = m.Direction == "outgoing",
                })
                .ToListAsync();

            // Marcar como leídos los incoming
            var unread = await _db.MessagesWhatsapp
                .Where(m => m.ContactId == contactId && !m.IsRead && m.Direction == "incoming")
                .ToListAsync();

            if (unread.Any())
            {
                unread.ForEach(m => m.IsRead = true);
                await _db.SaveChangesAsync();
            }

            return Ok(messages);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("MessagesWhatsapps table not ready: {Msg}", ex.Message);
            return Ok(Array.Empty<object>());
        }
    }

    // ====================================================================
    // ENVÍO DE MENSAJES (via 2Chat API)
    // ====================================================================

    /// <summary>
    /// Envía un mensaje de WhatsApp al contacto vía la API de 2Chat.
    /// El mensaje saliente también se guarda localmente vía el webhook /sent
    /// que 2Chat dispara automáticamente, pero como respaldo se guarda aquí también.
    /// </summary>
    [HttpPost("send")]
    [Authorize]
    [SwaggerOperation(Summary = "Enviar mensaje de WhatsApp", Tags = new[] { "WhatsApp" })]
    public async Task<IActionResult> SendMessage([FromBody] SendWhatsAppMessageDto dto)
    {
        // Obtener la configuración del tenant
        var customer = await _db.Customers.FindAsync(dto.CustomerId);
        if (customer == null)
            return NotFound(new { message = "Tenant no encontrado." });

        var apiKey     = customer.TwoChatApiKey ?? "UAK6e31c29a-c640-4877-81d9-ad67113ec7b5";
        var fromNumber = !string.IsNullOrEmpty(dto.FromNumber) ? dto.FromNumber : customer.WhatsappNumber;

        if (string.IsNullOrEmpty(fromNumber))
            return BadRequest(new { message = "No hay número de WhatsApp configurado para este tenant." });

        // Llamar a 2Chat
        var client  = _httpFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-User-API-Key", apiKey);

        var payload = new
        {
            to_number   = dto.ToNumber,
            from_number = "+" + fromNumber.TrimStart('+'),
            text        = dto.Text,
            url         = dto.MediaUrl,
        };

        var json    = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response= await client.PostAsync(TwoChatSendUrl, content);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            _logger.LogError("Error 2Chat: {Error}", err);
            return StatusCode(502, new { message = "Error al enviar mensaje por 2Chat.", detail = err });
        }

        // Guardar el mensaje saliente localmente (backup — 2Chat también dispara webhook /sent)
        var waContact = await _db.ContactsWhatsapp
            .FirstOrDefaultAsync(c => c.PhoneNumber == dto.ToNumber && c.CustomerId == dto.CustomerId);

        if (waContact != null)
        {
            var msg = new MessagesWhatsapp
            {
                ContactId   = waContact.Id,
                MessageId   = $"sent_{Guid.NewGuid():N}", // MessageId NOT NULL en la BD
                MessageText = dto.Text,
                MediaUrl    = dto.MediaUrl,
                Direction   = "outgoing",
                CreatedAt   = DateTime.UtcNow,
                IsRead      = true,
            };
            _db.MessagesWhatsapp.Add(msg);
            await _db.SaveChangesAsync();

            // Notificar en tiempo real a los agentes del tenant
            var msgPayload = BuildMessagePayload(msg);
            await _hub.Clients.Group($"wa_customer_{dto.CustomerId}")
                .SendAsync("NewMessage", waContact.Id, msgPayload);
        }

        return Ok(new { message = "Mensaje enviado correctamente." });
    }

    // ====================================================================
    // WEBHOOKS DE 2CHAT
    // ====================================================================

    /// <summary>
    /// Webhook de 2Chat para mensajes ENTRANTES.
    /// 2Chat lo llama con event_name = "whatsapp:message:new".
    /// NO requiere JWT (es una llamada externa de 2Chat).
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    [SwaggerOperation(Summary = "Webhook mensajes entrantes (2Chat)", Tags = new[] { "WhatsApp" })]
    public async Task<IActionResult> IncomingWebhook()
    {
        try
        {
            var jsonString = await new StreamReader(Request.Body).ReadToEndAsync();
            _logger.LogInformation("WA Webhook incoming: {Json}", jsonString);

            if (string.IsNullOrWhiteSpace(jsonString))
                return BadRequest("Cuerpo vacío.");

            using var doc = JsonDocument.Parse(jsonString);
            await ProcessWebhookPayload(doc.RootElement, "incoming");
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en IncomingWebhook");
            return StatusCode(500, ex.Message);
        }
    }

    /// <summary>
    /// Webhook de 2Chat para mensajes SALIENTES (confirmación de envío).
    /// NO requiere JWT (es una llamada externa de 2Chat).
    /// </summary>
    [HttpPost("webhook/sent")]
    [AllowAnonymous]
    [SwaggerOperation(Summary = "Webhook mensajes salientes (2Chat)", Tags = new[] { "WhatsApp" })]
    public async Task<IActionResult> SentWebhook()
    {
        try
        {
            var jsonString = await new StreamReader(Request.Body).ReadToEndAsync();
            _logger.LogInformation("WA Webhook sent: {Json}", jsonString);

            if (string.IsNullOrWhiteSpace(jsonString))
                return BadRequest("Cuerpo vacío.");

            using var doc = JsonDocument.Parse(jsonString);
            await ProcessWebhookPayload(doc.RootElement, "outgoing");
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en SentWebhook");
            return StatusCode(500, ex.Message);
        }
    }

    // ====================================================================
    // CLIENTES CON WHATSAPP HABILITADO
    // ====================================================================

    /// <summary>
    /// Lista los Customers que tienen WhatsApp habilitado (hasWhatsApp = true).
    /// Usado por el selector de cuenta en la página de WhatsApp.
    /// </summary>
    [HttpGet("customers")]
    [Authorize]
    [SwaggerOperation(Summary = "Clientes con WhatsApp habilitado", Tags = new[] { "WhatsApp" })]
    public async Task<IActionResult> GetWhatsAppCustomers()
    {
        var list = await _db.Customers
            .Where(c => c.HasWhatsApp && (c.Deleted == null || c.Deleted == false))
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name, c.WhatsappNumber, c.WhatsappChannel, c.Active })
            .ToListAsync();

        return Ok(list);
    }

    /// <summary>
    /// Habilita o deshabilita el canal de WhatsApp para un Customer.
    /// También permite actualizar las credenciales de 2Chat en el mismo request.
    /// </summary>
    [HttpPatch("customers/{customerId}/whatsapp")]
    [Authorize]
    [SwaggerOperation(Summary = "Configurar WhatsApp del tenant", Tags = new[] { "WhatsApp" })]
    public async Task<IActionResult> SetWhatsAppEnabled(int customerId, [FromBody] SetWhatsAppConfigDto dto)
    {
        var customer = await _db.Customers.FindAsync(customerId);
        if (customer == null) return NotFound(new { message = "Cliente no encontrado." });

        customer.HasWhatsApp = dto.Enabled;
        if (dto.WhatsappNumber  != null) customer.WhatsappNumber  = dto.WhatsappNumber;
        if (dto.WhatsappChannel != null) customer.WhatsappChannel = dto.WhatsappChannel;
        if (dto.TwoChatApiKey   != null) customer.TwoChatApiKey   = dto.TwoChatApiKey;

        await _db.SaveChangesAsync();
        return Ok(new { message = dto.Enabled ? "WhatsApp habilitado." : "WhatsApp deshabilitado." });
    }

    /// <summary>
    /// Configura el WhatsApp de un cliente: guarda número/canal,
    /// suscribe los webhooks en 2Chat (o actualiza si ya había uno previo),
    /// y activa hasWhatsApp = true.
    /// </summary>
    [HttpPost("customers/{customerId}/configure")]
    [Authorize]
    [SwaggerOperation(Summary = "Configurar WhatsApp del cliente (suscribe webhooks en 2Chat)", Tags = new[] { "WhatsApp" })]
    public async Task<IActionResult> ConfigureWhatsApp(int customerId, [FromBody] ConfigureWhatsAppDto dto)
    {
        var customer = await _db.Customers.FindAsync(customerId);
        if (customer == null) return NotFound(new { message = "Cliente no encontrado." });

        var numberChanged = customer.WhatsappNumber != dto.WhatsappNumber;

        // Si el número cambió, desuscribir los webhooks anteriores
        if (numberChanged)
        {
            if (!string.IsNullOrEmpty(customer.WebhookReceiveId))
                await _twoChatService.UnsubscribeWebhook(customer.WebhookReceiveId, customer.TwoChatApiKey);
            if (!string.IsNullOrEmpty(customer.WebhookSentId))
                await _twoChatService.UnsubscribeWebhook(customer.WebhookSentId, customer.TwoChatApiKey);
            customer.WebhookReceiveId = null;
            customer.WebhookSentId = null;
        }

        // Actualizar datos
        customer.WhatsappNumber  = dto.WhatsappNumber;
        if (dto.WhatsappChannel != null) customer.WhatsappChannel = dto.WhatsappChannel;
        if (dto.TwoChatApiKey   != null) customer.TwoChatApiKey   = dto.TwoChatApiKey;
        customer.HasWhatsApp = true;

        // Suscribir webhooks si es la primera vez o si el número cambió
        if (numberChanged || string.IsNullOrEmpty(customer.WebhookReceiveId))
        {
            try
            {
                var phone = "+" + dto.WhatsappNumber.TrimStart('+');
                var (receiveId, sentId) = await _twoChatService.SubscribeWebhooks(phone, customer.TwoChatApiKey);
                if (receiveId != null) customer.WebhookReceiveId = receiveId;
                if (sentId    != null) customer.WebhookSentId    = sentId;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("No se pudo suscribir webhooks en 2Chat: {Msg}", ex.Message);
                // No bloqueamos el guardado aunque falle 2Chat
            }
        }

        await _db.SaveChangesAsync();

        return Ok(new
        {
            message          = "WhatsApp configurado correctamente.",
            webhookReceiveId = customer.WebhookReceiveId,
            webhookSentId    = customer.WebhookSentId,
            hasWebhooks      = customer.WebhookReceiveId != null,
        });
    }

    // ====================================================================
    // RESPUESTAS RÁPIDAS (Saved Responses)
    // ====================================================================

    /// <summary>Lista de respuestas rápidas del tenant.</summary>
    [HttpGet("saved-responses")]
    [Authorize]
    [SwaggerOperation(Summary = "Obtener respuestas rápidas", Tags = new[] { "WhatsApp" })]
    public async Task<IActionResult> GetSavedResponses([FromQuery] int customerId)
    {
        try
        {
            var responses = await _db.SavedResponsesWhatsapp
                .Where(r => r.CustomerId == customerId)
                .OrderBy(r => r.Identifier)
                .Select(r => new { r.Id, r.Identifier, r.MessageTemplate, r.CreatedAt })
                .ToListAsync();

            return Ok(responses);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("SavedResponseWhatsapps table not ready yet: {Msg}. Run Querys.sql migration.", ex.Message);
            return Ok(Array.Empty<object>());
        }
    }

    /// <summary>Crea una respuesta rápida.</summary>
    [HttpPost("saved-responses")]
    [Authorize]
    [SwaggerOperation(Summary = "Crear respuesta rápida", Tags = new[] { "WhatsApp" })]
    public async Task<IActionResult> CreateSavedResponse([FromBody] SavedResponseDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Identifier) || string.IsNullOrWhiteSpace(dto.MessageTemplate))
            return BadRequest(new { message = "Identifier y MessageTemplate son obligatorios." });

        try
        {
            var entity = new SavedResponseWhatsapp
            {
                CustomerId      = dto.CustomerId,
                Identifier      = dto.Identifier,
                MessageTemplate = dto.MessageTemplate,
                CreatedAt       = DateTime.UtcNow,
            };

            _db.SavedResponsesWhatsapp.Add(entity);
            await _db.SaveChangesAsync();

            return Ok(new { id = entity.Id, message = "Respuesta guardada." });
        }
        catch (Exception ex)
        {
            _logger.LogWarning("SavedResponseWhatsapps table not ready: {Msg}", ex.Message);
            return StatusCode(503, new { message = "Tabla de respuestas aún no creada. Ejecuta la migración SQL." });
        }
    }

    /// <summary>Actualiza una respuesta rápida.</summary>
    [HttpPut("saved-responses/{id}")]
    [Authorize]
    [SwaggerOperation(Summary = "Actualizar respuesta rápida", Tags = new[] { "WhatsApp" })]
    public async Task<IActionResult> UpdateSavedResponse(int id, [FromBody] SavedResponseDto dto)
    {
        try
        {
            var entity = await _db.SavedResponsesWhatsapp.FindAsync(id);
            if (entity == null) return NotFound();

            entity.Identifier      = dto.Identifier;
            entity.MessageTemplate = dto.MessageTemplate;
            await _db.SaveChangesAsync();

            return Ok(new { message = "Respuesta actualizada." });
        }
        catch (Exception ex)
        {
            _logger.LogWarning("SavedResponseWhatsapps table not ready: {Msg}", ex.Message);
            return StatusCode(503, new { message = "Tabla aún no creada." });
        }
    }

    /// <summary>Elimina una respuesta rápida.</summary>
    [HttpDelete("saved-responses/{id}")]
    [Authorize]
    [SwaggerOperation(Summary = "Eliminar respuesta rápida", Tags = new[] { "WhatsApp" })]
    public async Task<IActionResult> DeleteSavedResponse(int id)
    {
        try
        {
            var entity = await _db.SavedResponsesWhatsapp.FindAsync(id);
            if (entity == null) return NotFound();

            _db.SavedResponsesWhatsapp.Remove(entity);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Respuesta eliminada." });
        }
        catch (Exception ex)
        {
            _logger.LogWarning("SavedResponseWhatsapps table not ready: {Msg}", ex.Message);
            return StatusCode(503, new { message = "Tabla aún no creada." });
        }
    }

    // ====================================================================
    // VINCULACIÓN: WA CONTACT ↔ CRM CONTACT
    // Los Leads y Deals se obtienen a través del CRM Contact (Contacts.ContactId)
    // al que está ligado el WA Contact (ContactsWhatsapps.LinkedContactId).
    // ====================================================================

    /// <summary>
    /// Retorna el Contact CRM ligado al WA Contact, y los Leads/Deals
    /// asociados a ese Contact CRM (a través de Lead.ContactId / Deal.PrimaryContactId).
    /// Si aún no está ligado a un Contact CRM, devuelve linkedContact en null.
    /// </summary>
    [HttpGet("contacts/{contactId}/linked-entities")]
    [Authorize]
    [SwaggerOperation(Summary = "Contact CRM y sus Leads/Deals vinculados", Tags = new[] { "WhatsApp" })]
    public async Task<IActionResult> GetLinkedEntities(int contactId)
    {
        // Cargar el contacto WA desde ContactsWhatsapps
        var waContact = await _db.ContactsWhatsapp
            .Where(c => c.Id == contactId)
            .Select(c => new { c.Id, c.LinkedContactId, c.FirstName, c.LastName, c.PhoneNumber, c.Email })
            .FirstOrDefaultAsync();

        if (waContact == null) return NotFound();

        // El ID efectivo para buscar Leads/Deals: el CRM Contact si está ligado
        var effectiveId = waContact.LinkedContactId;

        object? linkedContact = null;
        if (waContact.LinkedContactId.HasValue)
        {
            linkedContact = await _db.Contacts
                .Where(c => c.ContactId == waContact.LinkedContactId.Value)
                .Select(c => new {
                    c.ContactId, c.FirstName, c.LastName, c.Email, c.PhoneNumber, c.Position,
                    FullName = (c.FirstName + " " + c.LastName).Trim(),
                    Company  = c.Company != null ? c.Company.Name : null,
                })
                .FirstOrDefaultAsync();
        }

        var leads = effectiveId.HasValue
            ? await _db.Leads
                .Where(l => l.ContactId == effectiveId.Value)
                .Select(l => new { l.LeadId, Name = l.Name ?? $"Lead #{l.LeadId}", l.Status, l.CreatedOn })
                .ToListAsync()
            : new List<object>() as dynamic;

        var deals = effectiveId.HasValue
            ? await _db.Deals
                .Where(d => d.PrimaryContactId == effectiveId.Value)
                .Select(d => new { d.DealId, d.DealName, d.Status, d.CreatedOn })
                .ToListAsync()
            : new List<object>() as dynamic;

        return Ok(new { linkedContact, leads, deals });
    }

    /// <summary>
    /// Busca Contacts CRM existentes por nombre, teléfono o email dentro de una Account.
    /// Usado para identificar a qué Contact CRM pertenece el WA Contact.
    /// </summary>
    [HttpGet("crm-contacts/search")]
    [Authorize]
    [SwaggerOperation(Summary = "Buscar contactos CRM para vincular", Tags = new[] { "WhatsApp" })]
    public async Task<IActionResult> SearchCrmContacts([FromQuery] int accountId, [FromQuery] string? q)
    {
        // Buscamos Contacts que tengan Leads o Deals en esa Account
        var leadsContactIds = _db.Leads
            .Where(l => l.AccountId == accountId && l.ContactId != null)
            .Select(l => l.ContactId!.Value);

        var dealsContactIds = _db.Deals
            .Where(d => d.AccountId == accountId && d.PrimaryContactId != null)
            .Select(d => d.PrimaryContactId!.Value);

        var contactIds = leadsContactIds.Union(dealsContactIds).Distinct();

        var query = _db.Contacts.Where(c => contactIds.Contains(c.ContactId) && !c.IsWhatsappContact);

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(c =>
                (c.FirstName  != null && c.FirstName.Contains(q))  ||
                (c.LastName   != null && c.LastName.Contains(q))   ||
                (c.Email      != null && c.Email.Contains(q))      ||
                (c.PhoneNumber!= null && c.PhoneNumber.Contains(q)));
        }

        var results = await query
            .Take(20)
            .Select(c => new {
                c.ContactId,
                FullName = (c.FirstName + " " + c.LastName).Trim(),
                c.Email,
                c.PhoneNumber,
                Company  = c.Company != null ? c.Company.Name : null,
            })
            .ToListAsync();

        return Ok(results);
    }

    /// <summary>
    /// Vincula el WA Contact (ContactsWhatsapps) a un Contact CRM (Contacts).
    /// A partir de aquí, los Leads y Deals del Contact CRM aparecen en el panel WA.
    /// También auto-popula AccountId desde el Lead o Deal de ese CRM Contact.
    /// </summary>
    [HttpPost("contacts/{contactId}/link-crm-contact/{crmContactId}")]
    [Authorize]
    [SwaggerOperation(Summary = "Vincular WA Contact a Contact CRM", Tags = new[] { "WhatsApp" })]
    public async Task<IActionResult> LinkToCrmContact(int contactId, int crmContactId)
    {
        var waContact  = await _db.ContactsWhatsapp.FindAsync(contactId);
        if (waContact  == null) return NotFound(new { message = "Contacto WA no encontrado." });

        var crmContact = await _db.Contacts.FindAsync(crmContactId);
        if (crmContact == null) return NotFound(new { message = "Contacto CRM no encontrado." });

        waContact.LinkedContactId = crmContactId;

        // Auto-poblar AccountId desde el Lead o Deal del contacto CRM
        var accountId = await _db.Leads
            .Where(l => l.ContactId == crmContactId && l.AccountId != null)
            .Select(l => l.AccountId)
            .FirstOrDefaultAsync()
            ?? await _db.Deals
            .Where(d => d.PrimaryContactId == crmContactId)
            .Select(d => (int?)d.AccountId)
            .FirstOrDefaultAsync();

        if (accountId.HasValue)
            waContact.AccountId = accountId.Value;

        await _db.SaveChangesAsync();

        return Ok(new { message = "Contacto WA vinculado al contacto CRM correctamente.", accountId });
    }

    /// <summary>
    /// Desvincula el WA Contact del Contact CRM.
    /// </summary>
    [HttpDelete("contacts/{contactId}/unlink-crm-contact")]
    [Authorize]
    [SwaggerOperation(Summary = "Desvincular WA Contact de Contact CRM", Tags = new[] { "WhatsApp" })]
    public async Task<IActionResult> UnlinkFromCrmContact(int contactId)
    {
        var waContact = await _db.ContactsWhatsapp.FindAsync(contactId);
        if (waContact == null) return NotFound();

        waContact.LinkedContactId = null;
        await _db.SaveChangesAsync();

        return Ok(new { message = "Vínculo eliminado." });
    }

    // ====================================================================
    // HELPERS PRIVADOS
    // ====================================================================

    private async Task ProcessWebhookPayload(JsonElement root, string direction)
    {
        // Extraer campos del payload de 2Chat
        var uuid         = root.TryGetProperty("uuid",               out var uuidEl) ? uuidEl.GetString()  : null;
        var sessionKey   = root.TryGetProperty("session_key",        out var skEl)   ? skEl.GetString()    : null;
        var remotePhone  = root.TryGetProperty("remote_phone_number",out var rpEl)   ? rpEl.GetString()    : null;
        var createdAtStr = root.TryGetProperty("created_at",         out var caEl)   ? caEl.GetString()    : null;

        var createdAt = DateTime.TryParseExact(
            createdAtStr, "yyyy-MM-ddTHH:mm:ss",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate)
            ? parsedDate
            : DateTime.UtcNow;

        // Mensaje
        string? messageText = null;
        string? mediaUrl    = null;
        string? mediaType   = null;
        string? mimeType    = null;

        if (root.TryGetProperty("message", out var msgEl))
        {
            if (msgEl.TryGetProperty("text",  out var txtEl)) messageText = txtEl.GetString();
            if (msgEl.TryGetProperty("media", out var medEl))
            {
                if (medEl.TryGetProperty("url",       out var u)) mediaUrl  = u.GetString();
                if (medEl.TryGetProperty("type",      out var t)) mediaType = t.GetString();
                if (medEl.TryGetProperty("mime_type", out var m)) mimeType  = m.GetString();
            }
        }

        // Contacto del webhook
        string? firstName = null;
        string? lastName  = null;
        string? avatarUrl = null;
        if (root.TryGetProperty("contact", out var contactEl))
        {
            if (contactEl.TryGetProperty("first_name",      out var fn)) firstName = fn.GetString();
            if (contactEl.TryGetProperty("last_name",       out var ln)) lastName  = ln.GetString();
            if (contactEl.TryGetProperty("profile_pic_url", out var av)) avatarUrl = av.GetString();
        }

        // Identificar el tenant por el WhatsappChannel embebido en la session_key
        // Formato: WW-WPN{channel}-{phone}@c.us
        var channel  = ExtractChannel(sessionKey);
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.WhatsappChannel == channel);
        if (customer == null)
        {
            _logger.LogWarning("No se encontró tenant para channel={Channel}, sessionKey={SK}", channel, sessionKey);
            return;
        }

        // Buscar o crear contacto en ContactsWhatsapps
        var waContact = await _db.ContactsWhatsapp.FirstOrDefaultAsync(
            c => c.PhoneNumber == remotePhone && c.CustomerId == customer.Id);

        if (waContact == null)
        {
            waContact = new ContactWhatsapp
            {
                PhoneNumber = remotePhone ?? "",
                FirstName   = firstName,
                LastName    = lastName,
                AvatarUrl   = avatarUrl,
                CustomerId  = customer.Id,
                CreatedAt   = DateTime.UtcNow,
            };
            _db.ContactsWhatsapp.Add(waContact);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Nuevo contacto WA creado: {Phone} para customer {CId}", remotePhone, customer.Id);
        }

        // Evitar duplicados por uuid
        if (!string.IsNullOrEmpty(uuid))
        {
            var exists = await _db.MessagesWhatsapp.AnyAsync(m => m.MessageId == uuid);
            if (exists)
            {
                _logger.LogInformation("Mensaje duplicado ignorado: uuid={Uuid}", uuid);
                return;
            }
        }

        // Guardar mensaje — ContactId apunta a ContactsWhatsapps.Id
        var msg = new MessagesWhatsapp
        {
            ContactId   = waContact.Id,
            MessageId   = uuid,
            MessageText = messageText,
            MediaUrl    = mediaUrl,
            MediaType   = mediaType,
            MimeType    = mimeType,
            Direction   = direction,
            SessionKey  = sessionKey,
            IsRead      = direction == "outgoing", // Los salientes ya están "leídos"
            CreatedAt   = createdAt,
        };

        _db.MessagesWhatsapp.Add(msg);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Mensaje {Dir} guardado: contactId={CId}, uuid={Uuid}", direction, waContact.Id, uuid);

        // Emitir notificación SignalR a los agentes del tenant
        var payload = BuildMessagePayload(msg);
        await _hub.Clients.Group($"wa_customer_{customer.Id}")
            .SendAsync("NewMessage", waContact.Id, payload);
    }

    /// <summary>Construye el payload de mensaje para enviar por SignalR / respuesta REST.</summary>
    private static object BuildMessagePayload(MessagesWhatsapp m) => new
    {
        id          = m.Id,
        messageText = m.MessageText,
        mediaUrl    = m.MediaUrl,
        mediaType   = m.MediaType,
        mimeType    = m.MimeType,
        direction   = m.Direction,
        createdAt   = m.CreatedAt,
        isRead      = m.IsRead,
        isMine      = m.Direction == "outgoing",
    };

    /// <summary>
    /// Extrae el WhatsappChannel de la session_key.
    /// Formato: WW-WPN{channel}-{phone}@c.us → devuelve "WPN{channel}"
    /// </summary>
    private static string ExtractChannel(string? sessionKey)
    {
        if (string.IsNullOrEmpty(sessionKey)) return "";
        var start = sessionKey.IndexOf('-') + 1;
        var end   = sessionKey.LastIndexOf('-');
        if (start <= 0 || end <= start) return "";
        return sessionKey[start..end];
    }
}

// ====================================================================
// DTOs
// ====================================================================

public record CreateWhatsAppContactDto(
    string? FirstName,
    string? LastName,
    string PhoneNumber,
    string? Email,
    int CustomerId,
    int? AccountId = null
);

public record SendWhatsAppMessageDto(
    int    CustomerId,
    string ToNumber,
    string? FromNumber,
    string? Text,
    string? MediaUrl
);

public record ToggleArchiveDto(bool IsArchived);
public record AssignAccountDto(int? AccountId);

public record SavedResponseDto(
    int    CustomerId,
    string Identifier,
    string MessageTemplate
);

public record SetWhatsAppConfigDto(
    bool    Enabled,
    string? WhatsappNumber  = null,
    string? WhatsappChannel = null,
    string? TwoChatApiKey   = null
);

public record ConfigureWhatsAppDto(
    string WhatsappNumber,
    string? WhatsappChannel,
    string? TwoChatApiKey
);
