using Microsoft.AspNetCore.DataProtection;

namespace ProfetAPI.Services;

/// <summary>
/// Cifra/descifra secretos por cuenta (tokens de Meta, SMTP, telefonía, etc.) usando
/// el Data Protection API de ASP.NET Core. Las llaves las gestiona el framework.
/// TODO producción: persistir el key ring en Azure Blob + protegerlo con Azure Key Vault.
/// </summary>
public class SecretProtector
{
    private readonly IDataProtector _protector;

    public SecretProtector(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector("ProfetAPI.Secrets.v1");

    /// <summary>Cifra un valor en claro. Devuelve null/empty tal cual.</summary>
    public string? Protect(string? plain)
        => string.IsNullOrEmpty(plain) ? plain : _protector.Protect(plain);

    /// <summary>Descifra. Si el valor no está cifrado o la llave no coincide, devuelve null.</summary>
    public string? Unprotect(string? cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return cipher;
        try { return _protector.Unprotect(cipher); }
        catch { return null; }
    }
}
