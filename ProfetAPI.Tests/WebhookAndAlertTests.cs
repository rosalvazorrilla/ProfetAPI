using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProfetAPI.Data;
using ProfetAPI.Models;
using ProfetAPI.Services;
using Xunit;

namespace ProfetAPI.Tests;

/// <summary>Reintentos de webhooks salientes (hasta 3 intentos) y alertas que respetan lo que eligió cada usuario.</summary>
public class WebhookAndAlertTests
{
    // ── Utilidades de prueba ─────────────────────────────────────────────────────

    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase("wh-" + Guid.NewGuid()).Options);

    /// <summary>Servidor "del cliente" simulado: devuelve las respuestas en orden y cuenta los intentos.</summary>
    private sealed class FakeHandler(params object[] script) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var step = script[Math.Min(Calls, script.Length - 1)];
            Calls++;
            if (step is Exception ex) throw ex;
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)(int)step));
        }
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FakeGate(bool allowed) : IFeatureGateService
    {
        public Task<bool> HasFeatureAsync(int customerId, string featureCode) => Task.FromResult(allowed);
        public Task<bool> IsAllowedAsync(int customerId, string featureCode) => Task.FromResult(allowed);
    }

    private static async Task<(ApplicationDbContext db, AccountWebhook wh)> SeedWebhook()
    {
        var db = NewDb();
        db.Accounts.Add(new Account { AccountId = 10, CustomerId = 1, Name = "Cuenta" });
        var wh = new AccountWebhook
        {
            AccountId = 10, Name = "Hook", Direction = "Outgoing", TriggerEvent = "DealWon",
            TargetUrl = "https://cliente.example/hook", IsActive = true,
        };
        db.AccountWebhooks.Add(wh);
        await db.SaveChangesAsync();
        return (db, wh);
    }

    private static WebhookDispatcherService Dispatcher(ApplicationDbContext db, HttpMessageHandler handler, bool allowed = true) =>
        new(db, new FakeFactory(handler), new FakeGate(allowed), NullLogger<WebhookDispatcherService>.Instance,
            retryDelays: new[] { TimeSpan.Zero, TimeSpan.Zero }); // sin esperas reales en las pruebas

    // ── Reintentos de webhook ────────────────────────────────────────────────────

    [Fact]
    public async Task Si_el_servidor_responde_bien_se_manda_una_sola_vez()
    {
        var (db, wh) = await SeedWebhook();
        var handler = new FakeHandler(200);
        await Dispatcher(db, handler).DispatchAsync(10, "DealWon", new { id = 1 });
        Assert.Equal(1, handler.Calls);
        Assert.Null((await db.AccountWebhooks.FindAsync(wh.WebhookId))!.LastError);
    }

    [Fact]
    public async Task Si_el_servidor_falla_dos_veces_y_luego_responde_se_entrega_al_tercer_intento()
    {
        var (db, wh) = await SeedWebhook();
        var handler = new FakeHandler(500, 503, 200);
        await Dispatcher(db, handler).DispatchAsync(10, "DealWon", new { id = 1 });
        Assert.Equal(3, handler.Calls);
        Assert.Null((await db.AccountWebhooks.FindAsync(wh.WebhookId))!.LastError);
    }

    [Fact]
    public async Task Si_el_servidor_nunca_responde_bien_se_intenta_3_veces_y_se_deja_el_error()
    {
        var (db, wh) = await SeedWebhook();
        var handler = new FakeHandler(500);
        await Dispatcher(db, handler).DispatchAsync(10, "DealWon", new { id = 1 });
        Assert.Equal(3, handler.Calls);
        var saved = await db.AccountWebhooks.FindAsync(wh.WebhookId);
        Assert.Contains("HTTP 500", saved!.LastError);
        Assert.Contains("3 intentos", saved.LastError);
    }

    [Fact]
    public async Task Un_error_de_conexion_tambien_se_reintenta()
    {
        var (db, _) = await SeedWebhook();
        var handler = new FakeHandler(new HttpRequestException("sin conexión"), 200);
        await Dispatcher(db, handler).DispatchAsync(10, "DealWon", new { id = 1 });
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Un_rechazo_4xx_no_se_reintenta()
    {
        var (db, _) = await SeedWebhook();
        var handler = new FakeHandler(400);
        await Dispatcher(db, handler).DispatchAsync(10, "DealWon", new { id = 1 });
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Sin_el_derecho_de_plan_no_se_manda_nada()
    {
        var (db, _) = await SeedWebhook();
        var handler = new FakeHandler(200);
        await Dispatcher(db, handler, allowed: false).DispatchAsync(10, "DealWon", new { id = 1 });
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Un_evento_distinto_no_dispara_el_webhook()
    {
        var (db, _) = await SeedWebhook();
        var handler = new FakeHandler(200);
        await Dispatcher(db, handler).DispatchAsync(10, "DealLost", new { id = 1 });
        Assert.Equal(0, handler.Calls);
    }

    // ── Alertas por usuario ──────────────────────────────────────────────────────

    private sealed class CapturingNotifier : INotificationService
    {
        public List<string> Sent = new();
        public Task NotifyAsync(string userId, string message, string? url = null, string? entityType = null, long? entityId = null)
        { Sent.Add($"{userId}:{message}"); return Task.CompletedTask; }
        public Task NotifyAccountAsync(int accountId, string message, string? url = null, string? entityType = null, long? entityId = null, string? excludeUserId = null)
            => Task.CompletedTask;
    }

    private sealed class CapturingEmail : IEmailService
    {
        public List<string> Sent = new();
        public SmtpConfig GlobalConfig => new("h", 25, "u", "p", "f@x.com", "n", false, false);
        public Task<(bool success, string? error)> SendAsync(string to, string subject, string bodyHtml, string? cc = null, string? replyTo = null, SmtpConfig? config = null)
        { Sent.Add(to); return Task.FromResult((true, (string?)null)); }
    }

    private static async Task<(AlertService svc, CapturingNotifier notifier, CapturingEmail email, ApplicationDbContext db)> AlertSetup()
    {
        var db = NewDb();
        db.Users.Add(new ApplicationUser { Id = "u1", UserName = "u1", Email = "u1@x.com" });
        await db.SaveChangesAsync();
        var n = new CapturingNotifier(); var e = new CapturingEmail();
        return (new AlertService(db, n, e, NullLogger<AlertService>.Instance), n, e, db);
    }

    [Fact]
    public async Task Sin_preferencias_la_alerta_llega_al_sistema_y_no_por_correo()
    {
        var (svc, notifier, email, _) = await AlertSetup();
        await svc.SendAsync("u1", AlertType.DealWon, "Se ganó un trato");
        Assert.Single(notifier.Sent);
        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task Si_apaga_el_sistema_y_enciende_el_correo_solo_llega_correo()
    {
        var (svc, notifier, email, db) = await AlertSetup();
        db.UserNotificationSettings.Add(new UserNotificationSetting { UserId = "u1", DealWonSystem = false, DealWonEmail = true });
        await db.SaveChangesAsync();
        await svc.SendAsync("u1", AlertType.DealWon, "Se ganó un trato");
        Assert.Empty(notifier.Sent);
        Assert.Equal(new[] { "u1@x.com" }, email.Sent);
    }

    [Fact]
    public async Task Cada_tipo_de_alerta_se_configura_por_separado()
    {
        var (svc, notifier, _, db) = await AlertSetup();
        db.UserNotificationSettings.Add(new UserNotificationSetting { UserId = "u1", DealWonSystem = false, TaskDueSystem = true });
        await db.SaveChangesAsync();
        await svc.SendAsync("u1", AlertType.DealWon, "trato");   // apagada
        await svc.SendAsync("u1", AlertType.TaskDue, "tarea");   // encendida
        Assert.Equal(new[] { "u1:tarea" }, notifier.Sent);
    }
}
