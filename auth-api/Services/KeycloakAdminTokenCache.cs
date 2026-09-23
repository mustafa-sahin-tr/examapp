using System;
using System.Threading;
using System.Threading.Tasks;

namespace ExamApp.Api.Services;

/// <summary>
/// Keycloak admin (client_credentials) token'ı için süreç ömürlü önbellek (singleton). Eskiden token
/// <see cref="KeycloakService"/> örneğinde (scoped → HTTP isteği başına) tutuluyordu; her istek ayrı token
/// alıyordu. Şimdi tüm istekler paylaşır (issue #152 review). Süre: Keycloak'ın expires_in'i - 10 sn.
/// Anahtar (token URL + client id) değişirse önbellek yenilenir. Eşzamanlı ilk istekler tek token isteğinde birleşir.
/// </summary>
public sealed class KeycloakAdminTokenCache
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly TimeProvider _time;
    private string? _key;
    private string? _token;
    private DateTimeOffset _expiresAt;

    public KeycloakAdminTokenCache() : this(TimeProvider.System) { }

    public KeycloakAdminTokenCache(TimeProvider time) => _time = time;

    public async Task<string> GetOrCreateAsync(
        string key,
        Func<CancellationToken, Task<(string Token, int ExpiresInSeconds)>> factory,
        CancellationToken ct = default)
    {
        if (TryGet(key, out var cached))
            return cached;

        await _lock.WaitAsync(ct);
        try
        {
            if (TryGet(key, out cached))
                return cached;

            var (token, expiresInSeconds) = await factory(ct);
            _key = key;
            _token = token;
            _expiresAt = _time.GetUtcNow().AddSeconds(Math.Max(1, expiresInSeconds - 10));
            return token;
        }
        finally
        {
            _lock.Release();
        }
    }

    private bool TryGet(string key, out string token)
    {
        var current = _token;
        if (current is not null && string.Equals(_key, key, StringComparison.Ordinal) && _expiresAt > _time.GetUtcNow())
        {
            token = current;
            return true;
        }

        token = string.Empty;
        return false;
    }
}
