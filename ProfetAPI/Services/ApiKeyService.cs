using System.Security.Cryptography;
using System.Text;

namespace ProfetAPI.Services;

/// <summary>
/// Genera y valida API Keys de integración externa (una por Account). La key en
/// claro solo existe en el momento de crearla — de ahí en adelante se valida por
/// hash (SHA-256), igual que una contraseña, pero además se guarda una copia
/// cifrada reversible (vía SecretProtector) para que Admin Global pueda volver
/// a mostrarla si el cliente la perdió.
/// </summary>
public class ApiKeyService
{
    private const string Prefix = "pfk_live_";

    /// <summary>Genera una key nueva en claro, ej. "pfk_live_9f2a1c...".</summary>
    public string GenerateRawKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        var token = Convert.ToHexString(bytes).ToLowerInvariant();
        return Prefix + token;
    }

    /// <summary>Los primeros caracteres, seguros de mostrar sin descifrar nada (para identificar la key en una lista).</summary>
    public string ToDisplayPrefix(string rawKey) =>
        rawKey.Length <= 14 ? rawKey : rawKey[..14] + "…";

    public string Hash(string rawKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
