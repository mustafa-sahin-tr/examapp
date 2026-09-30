using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.IntegrationTests.Infrastructure;

/// <summary>
/// auth-api <c>GET /api/auth/user-profile</c> ucunun bellek içi karşılığı: kayıtlı Bearer token → profil. Gerçek
/// <c>AuthApiClient</c>'ın hangi token'ı ilettiğini uçtan uca doğrulamak için (header vs. SignalR <c>?access_token=</c>).
/// Boşken hiçbir davranışı değiştirmez; kayıtlı olmayan token'lı istekler gerçek ağ katmanına geçer.
/// </summary>
public sealed class FakeAuthApiProfiles
{
    private readonly ConcurrentDictionary<string, UserProfileDto> _byToken = new();
    private readonly ConcurrentQueue<string> _servedTokens = new();

    private readonly ConcurrentDictionary<string, byte> _rejected = new();

    public void Register(string bearerToken, UserProfileDto profile) => _byToken[bearerToken] = profile;

    /// <summary>auth-api'nin tanımadığı / süresi dolmuş token: 401 döner.</summary>
    public void Reject(string bearerToken) => _rejected[bearerToken] = 0;

    internal bool IsRejected(string? bearerToken) => bearerToken is not null && _rejected.ContainsKey(bearerToken);

    /// <summary>Profil döndürülen isteklerin Bearer token'ları (sırasıyla).</summary>
    public IReadOnlyCollection<string> ServedTokens => _servedTokens.ToArray();

    internal bool TryServe(string? bearerToken, out UserProfileDto profile)
    {
        profile = null!;
        if (bearerToken is null || !_byToken.TryGetValue(bearerToken, out var found))
            return false;
        _servedTokens.Enqueue(bearerToken);
        profile = found;
        return true;
    }
}

/// <summary>Tüm HttpClient'lara eklenen handler: yalnızca kayıtlı / reddedilen token'lı user-profile çağrısını keser.</summary>
public sealed class FakeAuthApiProfilesHandler(FakeAuthApiProfiles profiles) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var auth = request.Headers.Authorization;
        if (request.RequestUri?.AbsolutePath == "/api/auth/user-profile" && auth?.Scheme == "Bearer"
            && profiles.IsRejected(auth.Parameter))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }

        if (request.Method == HttpMethod.Get
            && request.RequestUri?.AbsolutePath == "/api/auth/user-profile"
            && auth?.Scheme == "Bearer"
            && profiles.TryServe(auth.Parameter, out var profile))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(profile), Encoding.UTF8, "application/json")
            });
        }

        return base.SendAsync(request, cancellationToken);
    }
}
