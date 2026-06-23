using System.Net;
using System.Net.Mail;

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
            using var smtp = new SmtpClient(cfg.Host, cfg.Port)
            {
                Credentials           = new NetworkCredential(cfg.User, cfg.Password),
                EnableSsl             = cfg.EnableSsl,
                DeliveryMethod        = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
            };

            using var msg = new MailMessage
            {
                From       = new MailAddress(cfg.FromAddress, cfg.FromName),
                Subject    = subject,
                Body       = bodyHtml,
                IsBodyHtml = true,
            };

            msg.To.Add(to);
            if (!string.IsNullOrWhiteSpace(cc))      msg.CC.Add(cc);
            if (!string.IsNullOrWhiteSpace(replyTo)) msg.ReplyToList.Add(new MailAddress(replyTo));

            await smtp.SendMailAsync(msg);
            _logger.LogInformation("Email enviado a {To} via {Source}", to, cfg.IsCustom ? "cuenta propia" : "Profet global");
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enviando email a {To}", to);
            return (false, ex.Message);
        }
    }
}
