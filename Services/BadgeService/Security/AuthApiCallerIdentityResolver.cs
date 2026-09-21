using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;

namespace BadgeService.Security;

/// <summary>
/// <see cref="ICallerIdentityResolver"/>'ı auth-api'nin <c>GET /api/auth/user-profile</c> ucuna
/// giderek uygular: çağıranın kendi Authorization header'ı olduğu gibi iletilir, dönen profildeki
/// sayısal <c>id</c> kullanılır. (exam API'deki <c>AuthApiClient</c> ile aynı desen; burada
/// <see cref="IHttpClientFactory"/> üzerinden <see cref="HttpClientName"/> adlı, kısa timeout'lu
/// client kullanılır — auth-api yavaşladığında rapor isteği asılı kalmasın.)
///
/// Cache (IMemoryCache, anahtar <c>caller-user-id:{sub}</c>):
/// <list type="bullet">
///   <item>Pozitif sonuç 15 dk — Keycloak sub → sayısal id eşlemesi değişmeyen bir veridir.</item>
///   <item>Negatif sonuç (non-2xx, boş/tutarsız profil, id&lt;=0, exception/timeout) 60 sn — böylece
///   kayıtsız/401 alan bir token ile auth-api'ye istek başına çağrı yapılıp amplifikasyon
///   oluşturulamaz, auth-api ayakta değilken de her istek timeout'u baştan beklemez.</item>
/// </list>
/// HTTP'ye hiç çıkılmayan yollar (token'da sub yok, AuthApi:BaseUrl yapılandırılmamış) cache'lenmez.
///
/// Her hata yolu <c>null</c> döner — çağıran controller bunu 403'e çevirir, yani fail-closed.
/// </summary>
public sealed class AuthApiCallerIdentityResolver : ICallerIdentityResolver
{
    /// <summary>DI'da kayıtlı named HttpClient (timeout'u Program.cs'te kısa tutulur).</summary>
    public const string HttpClientName = "auth-api-caller-identity";

    private const string CacheKeyPrefix = "caller-user-id:";
    private static readonly TimeSpan PositiveCacheTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan NegativeCacheTtl = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AuthApiCallerIdentityResolver> _logger;

    public AuthApiCallerIdentityResolver(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IMemoryCache cache,
        ILogger<AuthApiCallerIdentityResolver> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _cache = cache;
        _logger = logger;
    }

    public async Task<int?> ResolveUserIdAsync(HttpContext httpContext, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var sub = httpContext.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(sub))
        {
            _logger.LogWarning("[CallerIdentity] Token'da NameIdentifier (sub) claim'i yok; kimlik çözülemedi.");
            return null;
        }

        var cacheKey = CacheKeyPrefix + sub;
        if (_cache.TryGetValue<CachedIdentity>(cacheKey, out var cached) && cached is not null)
        {
            // Negatif kayıtta UserId null'dır; bu da "çözülemedi" demektir (fail-closed).
            return cached.UserId;
        }

        var baseUrl = _configuration["AuthApi:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _logger.LogWarning("[CallerIdentity] AuthApi:BaseUrl yapılandırılmamış; kimlik çözülemedi.");
            return null;
        }

        var profile = await FetchProfileAsync(baseUrl.TrimEnd('/'), httpContext, ct);
        if (profile is null)
        {
            return CacheNegative(cacheKey);
        }

        // Savunma: dönen profil, isteği yapan token'ın sahibine ait değilse güvenme.
        if (!string.Equals(profile.KeycloakId, sub, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "[CallerIdentity] auth-api profili token sub ile eşleşmiyor (sub={Sub}); kimlik reddedildi.",
                sub);
            return CacheNegative(cacheKey);
        }

        if (profile.Id <= 0)
        {
            _logger.LogWarning("[CallerIdentity] auth-api profilinde geçersiz id ({Id}) (sub={Sub}).", profile.Id, sub);
            return CacheNegative(cacheKey);
        }

        _cache.Set(cacheKey, new CachedIdentity(profile.Id), PositiveCacheTtl);
        return profile.Id;
    }

    private int? CacheNegative(string cacheKey)
    {
        _cache.Set(cacheKey, new CachedIdentity(null), NegativeCacheTtl);
        return null;
    }

    private async Task<CallerProfile?> FetchProfileAsync(string baseUrl, HttpContext httpContext, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/auth/user-profile");

            var authHeader = httpContext.Request.Headers.Authorization.ToString();
            if (!string.IsNullOrWhiteSpace(authHeader)
                && AuthenticationHeaderValue.TryParse(authHeader, out var parsed))
            {
                request.Headers.Authorization = parsed;
            }

            var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[CallerIdentity] auth-api user-profile çağrısı {StatusCode} döndü; kimlik çözülemedi.",
                    (int)response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<CallerProfile>(content, JsonOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Kimlik çözümü bir güvenlik kontrolünün girdisi; burada patlamak yerine null dönüp
            // çağıranın 403 almasını sağlıyoruz. Client timeout'u da (ct iptal edilmemişken gelen
            // TaskCanceledException) bu filtreye takılır ve null'a döner; yukarı yalnızca çağıranın
            // gerçekten iptal ettiği istek fırlar.
            _logger.LogWarning(ex, "[CallerIdentity] auth-api user-profile çağrısı başarısız; kimlik çözülemedi.");
            return null;
        }
    }

    /// <summary>Cache girdisi: <c>UserId == null</c> negatif (çözülemedi) sonucu temsil eder.</summary>
    private sealed record CachedIdentity(int? UserId);

    /// <summary>auth-api <c>UserProfileDto</c>'sunun bu servis için gereken alt kümesi.</summary>
    private sealed class CallerProfile
    {
        public int Id { get; set; }
        public string? KeycloakId { get; set; }
    }
}
