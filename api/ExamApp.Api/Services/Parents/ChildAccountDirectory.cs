using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Services.StudentReset;
using ExamApp.Foundation.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.Parents;

/// <summary>auth-api çocuk hesabı çağrısının sonucu (issue #438).</summary>
public enum ChildAccountCallStatus
{
    Success,
    /// <summary>400 — girdi geçersiz (<see cref="ChildAccountCallResult.ErrorCode"/>).</summary>
    Invalid,
    /// <summary>409 — kullanıcı adı alınmış.</summary>
    Conflict,
    /// <summary>404 — hesap yok / veli tarafından yönetilmiyor.</summary>
    NotFound,
    /// <summary>auth-api/Keycloak erişilemedi, servis token'ı alınamadı ya da 5xx.</summary>
    Unavailable
}

public sealed record ChildAccountCallResult(ChildAccountCallStatus Status, string? ErrorCode = null, CreateChildAccountResponse? Created = null);

/// <summary>
/// Issue #438: exam API → auth-api çocuk hesabı uçları (<c>api/auth/child-accounts</c>, yalnız servis token'ı — #372
/// <c>exam-service</c> client_credentials). Kimlik deposu (Keycloak + identity DB) auth-api'nindir; exam API yalnız bu sözleşmeyle
/// konuşur. Parola yalnız istek gövdesinde taşınır; hiçbir log/hata mesajına yazılmaz.
/// </summary>
public interface IChildAccountDirectory
{
    Task<ChildAccountCallResult> CreateAsync(CreateChildAccountRequest request, CancellationToken ct = default);

    /// <summary>Kompanzasyon: yalnız <c>parent_managed</c> hesabı geri alır (idempotent; zaten yoksa NotFound).</summary>
    Task<ChildAccountCallResult> DeleteAsync(int userId, CancellationToken ct = default);

    Task<ChildAccountCallResult> ResetPasswordAsync(int userId, string newPw, CancellationToken ct = default);
}

/// <inheritdoc cref="IChildAccountDirectory"/>
public sealed class AuthApiChildAccountDirectory : IChildAccountDirectory
{
    /// <summary>
    /// Yeniden denemesiz named client: hesap oluşturma idempotent DEĞİL — ServiceDefaults'un standart resilience handler'ı
    /// (10 sn attempt timeout + retry) ilk istek Keycloak'ta tamamlanmışken ikinci POST'u gönderip 409 + yetim hesap üretirdi
    /// (2026-09-22 seed olayı). Tek sınır <see cref="Timeout"/>.
    /// </summary>
    public const string HttpClientName = "AuthApiChildAccounts";

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceTokenProvider _tokens;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthApiChildAccountDirectory> _logger;

    public AuthApiChildAccountDirectory(
        IHttpClientFactory httpClientFactory, IServiceTokenProvider tokens, IConfiguration configuration,
        ILogger<AuthApiChildAccountDirectory> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokens = tokens;
        _configuration = configuration;
        _logger = logger;
    }

    private string BaseUrl => (_configuration["AuthApiBaseUrl"] ?? string.Empty).TrimEnd('/') + "/api/auth/child-accounts";

    public Task<ChildAccountCallResult> CreateAsync(CreateChildAccountRequest request, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, BaseUrl, request, "create", readCreated: true, ct);

    public Task<ChildAccountCallResult> DeleteAsync(int userId, CancellationToken ct = default)
        => SendAsync(HttpMethod.Delete, $"{BaseUrl}/{userId}", body: null, "delete", readCreated: false, ct);

    public Task<ChildAccountCallResult> ResetPasswordAsync(int userId, string newPw, CancellationToken ct = default)
    {
        var body = new ResetChildAccountPasswordRequest();
        body.Password = newPw;
        return SendAsync(HttpMethod.Put, $"{BaseUrl}/{userId}/password", body, "reset", readCreated: false, ct);
    }

    private async Task<ChildAccountCallResult> SendAsync(
        HttpMethod method, string url, object? body, string operation, bool readCreated, CancellationToken ct)
    {
        try
        {
            var token = await _tokens.GetAccessTokenAsync(ct);
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body != null)
                request.Content = JsonContent.Create(body, body.GetType(), options: Json);

            using var response = await _httpClientFactory.CreateClient(HttpClientName).SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                var created = readCreated ? await response.Content.ReadFromJsonAsync<CreateChildAccountResponse>(Json, ct) : null;
                if (readCreated && (created == null || created.UserId <= 0 || string.IsNullOrWhiteSpace(created.KeycloakId)))
                {
                    _logger.LogError("[ChildAccounts] auth-api {Operation} yanıtı çözümlenemedi.", operation);
                    return new ChildAccountCallResult(ChildAccountCallStatus.Unavailable, ChildAccountErrorCodes.Unavailable);
                }
                return new ChildAccountCallResult(ChildAccountCallStatus.Success, Created: created);
            }

            var errorCode = await ReadErrorCodeAsync(response, ct);
            var status = response.StatusCode switch
            {
                HttpStatusCode.BadRequest => ChildAccountCallStatus.Invalid,
                HttpStatusCode.Conflict => ChildAccountCallStatus.Conflict,
                HttpStatusCode.NotFound => ChildAccountCallStatus.NotFound,
                _ => ChildAccountCallStatus.Unavailable
            };
            if (status == ChildAccountCallStatus.Unavailable)
            {
                // 401/403: servis hesabı (exam-service) auth-api Service policy'sini geçemiyor — yapılandırma hatası.
                _logger.LogWarning("[ChildAccounts] auth-api {Operation} başarısız: HTTP {Status}", operation, (int)response.StatusCode);
            }
            return new ChildAccountCallResult(status, errorCode ?? ChildAccountErrorCodes.Unavailable);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException
                                   || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            // ex mesajı URL/durum içerir, gövde/parola içermez.
            _logger.LogWarning(ex, "[ChildAccounts] auth-api {Operation} çağrısı yapılamadı.", operation);
            return new ChildAccountCallResult(ChildAccountCallStatus.Unavailable, ChildAccountErrorCodes.Unavailable);
        }
    }

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<ChildAccountErrorResponse>(Json, ct);
            return string.IsNullOrWhiteSpace(body?.ErrorCode) ? null : body.ErrorCode;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }
}
