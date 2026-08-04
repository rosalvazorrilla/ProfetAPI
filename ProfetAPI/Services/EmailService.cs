using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace ProfetAPI.Services;

/// <summary>Config SMTP para un envío concreto (global o de la cuenta)</summary>
public record SmtpConfig(
    string Host,
    int    Port,
    string User,
    string Password,
    string FromAddress,
    string FromName,
    bool   EnableSsl,
    bool   IsCustom      // true = viene de la cuenta, false = config global de Profet
);

public interface IEmailService
{
    Task<(bool success, string? error)> SendAsync(
        string     to,
        string     subject,
        string     bodyHtml,
        string?    cc       = null,
        string?    replyTo  = null,
        SmtpConfig? config  = null);   // null → usa la config global de Profet

    /// <summary>Construye la SmtpConfig global de Profet desde appsettings.</summary>
    SmtpConfig GlobalConfig { get; }
}

public class EmailService : IEmailService
{
    private readonly IConfiguration         _config;
    private readonly ILogger<EmailService>  _logger;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public SmtpConfig GlobalConfig => new(
        Host:         _config["Email:SmtpHost"]        ?? "smtp.sendgrid.net",
        Port:         int.Parse(_config["Email:SmtpPort"] ?? "587"),
        User:         _config["Email:SmtpUser"]        ?? "apikey",
        Password:     _config["Email:SmtpPassword"]    ?? "",
        FromAddress:  _config["Email:FromAddress"]     ?? "noreply@profet.mx",
        FromName:     _config["Email:FromName"]        ?? "Profet CRM",
        EnableSsl:    bool.Parse(_config["Email:EnableSsl"] ?? "true"),
        IsCustom:     false
    );

    public async Task<(bool success, string? error)> SendAsync(
        string     to,
        string     subject,
        string     bodyHtml,
        string?    cc       = null,
        string?    replyTo  = null,
        SmtpConfig? config  = null)
    {
        // Fallback a config global si no se pasa ninguna
        var cfg = config ?? GlobalConfig;

        try
        {
            var msg = new MimeMessage();
            msg.From.Add(new MailboxAddress(cfg.FromName, cfg.FromAddress));
            msg.To.Add(MailboxAddress.Parse(to));
            if (!string.IsNullOrWhiteSpace(cc))      msg.Cc.Add(MailboxAddress.Parse(cc));
            if (!string.IsNullOrWhiteSpace(replyTo)) msg.ReplyTo.Add(MailboxAddress.Parse(replyTo));
            msg.Subject = subject;
            msg.Body = new BodyBuilder { HtmlBody = bodyHtml }.ToMessageBody();

            using var smtp = new SmtpClient();
            var security = cfg.EnableSsl ? SecureSocketOptions.Auto : SecureSocketOptions.None;
            await smtp.ConnectAsync(cfg.Host, cfg.Port, security);
            await smtp.AuthenticateAsync(cfg.User, cfg.Password);
            await smtp.SendAsync(msg);
            await smtp.DisconnectAsync(true);

            _logger.LogInformation("Email enviado a {To} via {Source}", to, cfg.IsCustom ? "cuenta propia" : "Profet global");
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enviando email a {To}", to);
            var detail = ex.InnerException?.Message;
            var message = string.IsNullOrWhiteSpace(detail) || detail == ex.Message
                ? ex.Message
                : $"{ex.Message} — {detail}";
            return (false, message);
        }
    }
}
