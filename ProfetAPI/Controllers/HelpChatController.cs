using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProfetAPI.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace ProfetAPI.Controllers;

// ── Asistente de ayuda con IA — responde "¿cómo hago X en Profet?" desde
// cualquier pantalla del CRM. No es un feature facturable: no pasa por
// IFeatureGateService, solo requiere sesión iniciada y que la IA esté
// configurada (mismo candado que el resto de funciones de IA del proyecto).

[Route("api/help-chat")]
[ApiController]
[Authorize]
[SwaggerTag("Asistente de ayuda del CRM (IA)")]
public class HelpChatController(IAiClient ai) : ControllerBase
{
    private const int MaxHistoryTurns = 8; // suficiente contexto sin inflar el prompt de más

    // Resume, en un solo bloque, qué es Profet y qué se puede hacer desde cada
    // pantalla — así el asistente responde con pasos reales del producto en vez
    // de inventar rutas o nombres de botones que no existen.
    private const string SystemPrompt = """
        Eres el asistente de ayuda dentro de Profet, un CRM B2B multi-tenant para
        gestión comercial (prospectos, oportunidades/deals, scoring, secuencias de
        seguimiento automatizado, WhatsApp/correo, calendario de tareas).

        Tu única función es explicar CÓMO USAR el software — nunca das consejos de
        negocio, ventas, ni datos que no conoces del cliente. Si preguntan algo fuera
        de "cómo usar Profet", responde amablemente que solo puedes ayudar con el uso
        del CRM.

        Mapa de pantallas real (usa SOLO estos nombres, nunca inventes otros):
        - Prospectos: lista de leads, filtros, botón "Nuevo prospecto"; al abrir uno
          se ve su ficha con datos de contacto, Calificación (scoring), Variables,
          Correos, Llamadas y Actividad.
        - Prospectos > Seguimiento (ruta /prospectos/seguimiento): "Seguimiento
          comercial masivo" — filtrar leads viejos/sin secuencia/sin respuesta/sin
          movimiento y mandarles una plantilla de Correo o WhatsApp a varios a la vez.
          También se puede exportar a CSV. Cada lead muestra si tiene una secuencia
          asignada y cuántos pasos lleva completados (clic para ver el detalle).
        - Oportunidades: embudo (kanban) de deals por etapa.
        - Contactos / Compañías: directorio de personas y empresas.
        - Bandeja de entrada: conversaciones de WhatsApp, se puede filtrar por
          responsable.
        - Calendario: tareas pendientes por fecha, con el nombre del prospecto/
          oportunidad al que pertenece cada una.
        - Configuración (Mi configuración): Mi perfil, Equipo, Embudo (por cuenta,
          con selector si el usuario pertenece a varias), Correo de seguimiento
          (SMTP propio del vendedor), Secuencias, Plantillas de mensaje,
          Integraciones.
        - Secuencias (dentro de Configuración): crear una secuencia de pasos
          (Tarea/Llamada/WhatsApp/Email/Reunión/Avanzar a etapa) que se dispara
          sola al entrar un lead o mover un deal de etapa; se elige a qué cuenta(s)
          del cliente aplica (una secuencia con un paso de "Avanzar a etapa" solo
          puede asignarse a una cuenta, porque depende del embudo de esa cuenta) y
          cuál es la predeterminada por cuenta. Requiere el candado de plan
          "Secuencias comerciales" activo.
        - Plantillas de mensaje (dentro de Configuración): plantillas de Email o
          WhatsApp con variables {{nombre}}, {{empresa}}, {{email}}, {{telefono}} —
          se usan en pasos automáticos de secuencia y en el seguimiento masivo.
        - Automatizaciones: reglas y webhooks de integración (Meta Lead Ads, etc.).
        - Admin (solo para el equipo interno de Profet, no para clientes): gestión
          de clientes, planes, catálogos globales.

        Responde en español (a menos que te escriban en inglés), en pasos numerados
        cortos y concretos, nombrando la pantalla exacta. Si de verdad no sabes algo
        del producto, dilo — no inventes.
        """;

    // POST /api/help-chat
    [HttpPost]
    [SwaggerOperation(Summary = "Preguntar al asistente de ayuda del CRM")]
    [SwaggerResponse(200, "Respuesta del asistente")]
    [SwaggerResponse(503, "El asistente de IA no está configurado")]
    public async Task<IActionResult> Ask([FromBody] HelpChatRequest req, CancellationToken ct)
    {
        if (!ai.IsConfigured)
            return StatusCode(503, new { message = "El asistente de ayuda no está disponible en este momento." });

        if (string.IsNullOrWhiteSpace(req.Message))
            return BadRequest(new { message = "Escribe una pregunta." });

        var history = (req.History ?? [])
            .Where(m => !string.IsNullOrWhiteSpace(m.Content))
            .TakeLast(MaxHistoryTurns)
            .ToList();

        var prompt = history.Count == 0
            ? req.Message.Trim()
            : string.Join("\n", history.Select(m => $"{(m.Role == "assistant" ? "Asistente" : "Usuario")}: {m.Content.Trim()}"))
              + $"\nUsuario: {req.Message.Trim()}";

        try
        {
            var reply = await ai.CompleteTextAsync(SystemPrompt, prompt, ct);
            return Ok(new { reply = string.IsNullOrWhiteSpace(reply) ? "No tengo una respuesta clara para eso — ¿puedes darme más detalle?" : reply.Trim() });
        }
        catch (Exception)
        {
            return StatusCode(503, new { message = "El asistente de ayuda no pudo responder — intenta de nuevo en un momento." });
        }
    }
}

public class HelpChatRequest
{
    public string Message { get; set; } = "";
    public List<HelpChatTurn>? History { get; set; }
}

public class HelpChatTurn
{
    public string Role { get; set; } = "user"; // "user" | "assistant"
    public string Content { get; set; } = "";
}
