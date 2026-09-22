using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Services.StudentReset;
using ExamApp.Foundation.Contracts;
using Microsoft.Extensions.Configuration;

namespace ExamApp.Api.Services.Teachers.Seed;

/// <summary>
/// Bkz. <see cref="IAuthApiSeedClient"/>. <c>AuthApiBaseUrl</c> + <see cref="IServiceTokenProvider"/>
/// (BadgeResetApiClient ile aynı desen). Zaman aşımı uzun (named client): partial import'ta 500 kullanıcı
/// tek istek; temizlikte on binlerce Keycloak silme. Tüm hata yolları <see cref="TeacherSeedAuthApiException"/>
/// ile açıklayıcı mesaja çevrilir.
/// </summary>
public sealed class AuthApiSeedClient : IAuthApiSeedClient
{
    public const string HttpClientName = nameof(AuthApiSeedClient);
    public const string BaseUrlConfigKey = "AuthApiBaseUrl";

    /// <summary>Named client'ın tek zaman sınırı (resilience handler yok, yeniden deneme yok — bkz. TeacherSeedServiceCollectionExtensions).</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(30);

    private const string SeedUsersPath = "/api/auth/dev/seed-users";
    private const string CleanupPath = "/api/auth/dev/seed-users/cleanup";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IServiceTokenProvider _tokenProvider;

    public AuthApiSeedClient(IHttpClientFactory httpClientFactory, IConfiguration configuration, IServiceTokenProvider tokenProvider)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _tokenProvider = tokenProvider;
    }

    public Task<DevSeedUsersResponse> SeedUsersAsync(DevSeedUsersRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PostAsync<DevSeedUsersRequest, DevSeedUsersResponse>(SeedUsersPath, "seed-users", request,
            $"{request.Users.Count} hesap", "Keycloak yavaş olabilir; --batch-size küçültün ya da --keycloak-mode partial-import deneyin.", ct);
    }

    public Task<DevSeedCleanupResponse> CleanupSeedUsersAsync(DevSeedCleanupRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PostAsync<DevSeedCleanupRequest, DevSeedCleanupResponse>(CleanupPath, "seed-users/cleanup", request,
            request.DryRun ? "dry-run" : "apply", "Keycloak'ta çok kullanıcı silinmiş olabilir; tekrar koşu kalanı temizler (idempotent).", ct);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string path, string label, TRequest request, string sizeHint, string timeoutHint, CancellationToken ct)
    {
        var baseUrl = _configuration[BaseUrlConfigKey]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new TeacherSeedAuthApiException($"{BaseUrlConfigKey} yapılandırılmamış (örn. http://localhost:6079).");

        string token;
        try
        {
            token = await _tokenProvider.GetAccessTokenAsync(ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        {
            throw new TeacherSeedAuthApiException($"Servis token'ı alınamadı (Keycloak client_credentials): {ex.Message}", ex);
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var url = baseUrl + path;
        using var message = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(request, Json), Encoding.UTF8, "application/json")
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(message, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new TeacherSeedAuthApiException($"auth-api'ye ulaşılamadı ({url}): {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // HttpClient zaman aşımı (kullanıcı iptali değil): Keycloak/auth-api yavaş — parti küçültülebilir.
            throw new TeacherSeedAuthApiException(
                $"auth-api zaman aşımı ({url}, {sizeHint}, sınır {client.Timeout.TotalSeconds:F0} sn). {timeoutHint}", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new TeacherSeedAuthApiException(
                    $"auth-api dev ucu bulunamadı (404): {url}. auth-api Development/Staging'de mi, güncel build mi ve AuthApiBaseUrl gateway değil doğrudan auth-api mi?");
            }
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new TeacherSeedAuthApiException(
                    $"auth-api servis token'ını reddetti ({(int)response.StatusCode}). Keycloak:AdminClientId servis hesabı 'exam-service' rolüne ya da auth-api Keycloak:ServiceClients listesine sahip olmalı.");
            }
            if (!response.IsSuccessStatusCode)
            {
                throw new TeacherSeedAuthApiException(
                    $"auth-api {label} başarısız: {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(body)}");
            }

            try
            {
                return JsonSerializer.Deserialize<TResponse>(body, Json)
                    ?? throw new TeacherSeedAuthApiException("auth-api boş yanıt döndü.");
            }
            catch (JsonException ex)
            {
                throw new TeacherSeedAuthApiException($"auth-api yanıtı çözümlenemedi: {ex.Message}", ex);
            }
        }
    }

    private static string Truncate(string s) => s.Length <= 500 ? s : s[..500] + "…";
}
