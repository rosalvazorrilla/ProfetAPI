using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ProfetAPI.Hubs;

/// <summary>
/// Hub de SignalR para notificaciones en tiempo real del módulo WhatsApp.
/// El cliente se conecta y se une al grupo de su tenant (customerId).
/// Cuando llega un mensaje entrante vía webhook, el servidor emite "NewMessage"
/// a todos los agentes conectados de ese tenant.
/// </summary>
[Authorize]
public class WhatsAppHub : Hub
{
    /// <summary>El agente se une al grupo del tenant para recibir notificaciones.</summary>
    public async Task JoinCustomerGroup(int customerId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"wa_customer_{customerId}");
    }

    /// <summary>El agente sale del grupo (al cambiar de tenant o desconectarse).</summary>
    public async Task LeaveCustomerGroup(int customerId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"wa_customer_{customerId}");
    }
}
