namespace ProfetAPI.Services;

/// <summary>Envoltura HTML con la identidad visual de Profet para correos transaccionales (pruebas, avisos del sistema).</summary>
public static class EmailTemplates
{
    public static string Wrap(string title, string bodyHtml, string? badgeText = null, string? logoUrl = null)
    {
        var badgeHtml = badgeText != null
            ? $"<span style=\"float:right;background:rgba(255,255,255,0.2);color:#ffffff;font-size:11px;font-weight:600;padding:5px 10px;border-radius:999px;\">{badgeText}</span>"
            : "";

        var brandHtml = !string.IsNullOrWhiteSpace(logoUrl)
            ? $"<img src=\"{logoUrl}\" alt=\"Profet\" height=\"28\" style=\"height:28px;max-width:160px;display:inline-block;vertical-align:middle;\" />"
            : "<span style=\"font-size:20px;font-weight:700;color:#ffffff;letter-spacing:-0.02em;\">Profet</span>";

        return $@"
<!DOCTYPE html>
<html lang=""es"">
<body style=""margin:0;padding:0;background-color:#f4f5f7;font-family:-apple-system,Segoe UI,Helvetica,Arial,sans-serif;"">
  <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background-color:#f4f5f7;padding:32px 16px;"">
    <tr>
      <td align=""center"">
        <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""max-width:520px;background-color:#ffffff;border-radius:16px;overflow:hidden;box-shadow:0 1px 3px rgba(20,20,30,0.08);"">
          <tr>
            <td style=""background:linear-gradient(135deg,#1CAF9A,#5F6CAF);padding:28px 32px;"">
              {brandHtml}
              {badgeHtml}
            </td>
          </tr>
          <tr>
            <td style=""padding:32px;"">
              <h1 style=""margin:0 0 16px;font-size:18px;font-weight:700;color:#16161d;"">{title}</h1>
              <div style=""font-size:14px;line-height:1.6;color:#4a4a57;"">
                {bodyHtml}
              </div>
            </td>
          </tr>
          <tr>
            <td style=""padding:20px 32px;background-color:#fafafb;border-top:1px solid #eeeef1;"">
              <p style=""margin:0;font-size:12px;color:#9a9aa5;"">Enviado desde Profet CRM &middot; No respondas directamente a este correo si es una notificación automática.</p>
            </td>
          </tr>
        </table>
      </td>
    </tr>
  </table>
</body>
</html>";
    }
}
