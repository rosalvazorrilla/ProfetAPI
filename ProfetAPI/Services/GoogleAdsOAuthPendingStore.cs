using System.Collections.Concurrent;

namespace ProfetAPI.Services;

public class GoogleAdsPendingLink
{
    public required int AccountId { get; init; }
    public required string RefreshTokenEncrypted { get; init; }
    public required List<GoogleAdsAccountOption> Accounts { get; init; }
    public required DateTime ExpiresAt { get; init; }
}

public record GoogleAdsAccountOption(string CustomerId, string Name);

/// <summary>
/// Cache en memoria de vínculos Google Ads pendientes de confirmar (entre "traer cuentas
/// accesibles" y "el usuario elige una"). TTL corto, un solo uso. Igual que el resto de
/// procesos fire-and-forget de este proyecto, no sobrevive un reinicio/escalado — aceptable
/// porque el usuario simplemente reintenta la conexión si el flujo expira.
/// </summary>
public class GoogleAdsOAuthPendingStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, GoogleAdsPendingLink> _pending = new();

    public string Create(int accountId, string refreshTokenEncrypted, List<GoogleAdsAccountOption> accounts)
    {
        PurgeExpired();
        var nonce = Guid.NewGuid().ToString("N");
        _pending[nonce] = new GoogleAdsPendingLink
        {
            AccountId = accountId,
            RefreshTokenEncrypted = refreshTokenEncrypted,
            Accounts = accounts,
            ExpiresAt = DateTime.UtcNow.Add(Ttl),
        };
        return nonce;
    }

    public GoogleAdsPendingLink? Consume(string nonce, int accountId)
    {
        if (!_pending.TryRemove(nonce, out var link)) return null;
        if (link.ExpiresAt < DateTime.UtcNow) return null;
        if (link.AccountId != accountId) return null;
        return link;
    }

    private void PurgeExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _pending)
            if (kvp.Value.ExpiresAt < now)
                _pending.TryRemove(kvp.Key, out _);
    }
}
