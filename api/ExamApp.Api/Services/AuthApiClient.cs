using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.StudentReset;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services;

public class AuthApiClient : IAuthApiClient
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceTokenProvider _serviceTokenProvider;
    private readonly ILogger<AuthApiClient> _logger;

    public AuthApiClient(
        IConfiguration configuration,
        IHttpContextAccessor httpContextAccessor,
        IHttpClientFactory httpClientFactory,
        IServiceTokenProvider serviceTokenProvider,
        ILogger<AuthApiClient> logger)
    {
        _configuration = configuration;
        _httpContextAccessor = httpContextAccessor;
        _httpClientFactory = httpClientFactory;
        _serviceTokenProvider = serviceTokenProvider;
        _logger = logger;
    }

    /// <summary>
    /// "Kendi profilim" ucu — çağıran kullanıcının kendi token'ı ile gider (auth-api sub'dan kullanıcıyı bulur).
    /// </summary>
    public async Task<UserProfileDto> GetUserProfileAsync()
    {
        var httpClient = _httpClientFactory.CreateClient();
        var baseUrl = _configuration["AuthApiBaseUrl"]; // Configuration'dan URL oku
        _logger.LogDebug("[AuthApiClient] Base URL: {BaseUrl}", baseUrl);

        var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/auth/user-profile");

        // Mevcut request'ten Authorization header'ını al
        var authHeader = _httpContextAccessor.HttpContext?.Request.Headers["Authorization"];
        if (!string.IsNullOrEmpty(authHeader))
        {
            request.Headers.Add("Authorization", authHeader.ToString());
        }

        var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();

        var userProfile = JsonSerializer.Deserialize<UserProfileDto>(content, Json);
        return userProfile;
    }

    /// <summary>
    /// Toplu ad/e-posta/KeycloakId çözümü. Issue #219: auth-api'deki uç artık yalnızca servis principal'ı
    /// kabul ettiği için kullanıcının token'ı forward EDİLMEZ; client_credentials servis token'ı kullanılır.
    /// Servis token'ı alınamazsa (Keycloak config/erişim) boş liste döner ve uyarı loglanır — çağıran
    /// yerlerin tamamı best-effort zenginleştirme yapar, listeler bu yüzden kırılmamalı.
    /// Keycloak zaman aşımı (HttpClient timeout, kullanıcı iptali değil) de aynı şekilde boş liste döner.
    /// Lookup HTTP çağrısının kendisi başarısız olursa eskisi gibi <see cref="HttpRequestException"/> fırlatır
    /// (çağıran yerler bunu zaten yakalıyor); 401/403'te önce teşhis uyarısı loglanır.
    /// </summary>
    public async Task<IReadOnlyList<UserLookupResultDto>> GetUsersByIdsAsync(IEnumerable<int> userIds, CancellationToken ct = default)
    {
        if (userIds == null)
        {
            throw new ArgumentNullException(nameof(userIds));
        }

        var distinctIds = userIds.Distinct().ToList();
        if (!distinctIds.Any())
        {
            return Array.Empty<UserLookupResultDto>();
        }

        string token;
        try
        {
            token = await _serviceTokenProvider.GetAccessTokenAsync(ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException
                                   || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            _logger.LogWarning(ex,
                "[AuthApiClient] Servis token'ı alınamadı; users/lookup atlanıyor, {Count} kullanıcı isimsiz kalacak.",
                distinctIds.Count);
            return Array.Empty<UserLookupResultDto>();
        }

        var httpClient = _httpClientFactory.CreateClient();
        var baseUrl = _configuration["AuthApiBaseUrl"];
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/auth/users/lookup")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { UserIds = distinctIds }),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await httpClient.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _logger.LogWarning(
                "[AuthApiClient] auth-api servis token'ını reddetti ({Status}). Keycloak:AdminClientId hesabı 'exam-service' rolüne ya da auth-api Keycloak:ServiceClients listesine sahip olmalı.",
                (int)response.StatusCode);
        }
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<List<UserLookupResultDto>>(content, Json) ?? new List<UserLookupResultDto>();
    }
}
