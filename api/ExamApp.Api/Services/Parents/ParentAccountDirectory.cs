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

/// <summary>auth-api veli hesabı çağrısının sonucu (issue #440).</summary>
public enum ParentAccountCallStatus
{
    Success,
    /// <summary>400 — girdi geçersiz / parola reddi (<see cref="ParentAccountCallResult.ErrorCode"/>).</summary>
    Invalid,
    /// <summary>409 — <c>EmailAlreadyRegistered</c> (aktivasyon: e-postaya ait hesap var) / <c>EmailMismatch</c> / <c>NotParentAccount</c> (kabul).</summary>
    Conflict,
    /// <summary>auth-api/Keycloak erişilemedi, servis token'ı alınamadı ya da 5xx.</summary>
    Unavailable
}

public sealed record ParentAccountCallResult(
    ParentAccountCallStatus Status,
    string? ErrorCode = null,
    ParentAccountLookupResponse? Lookup = null,
    CreateParentAccountResponse? Created = null);

/// <summary>
/// Issue #440: exam API → auth-api veli hesabı uçları (<c>api/auth/parent-accounts</c>, yalnız servis token'ı — <c>exam-service</c>
/// client_credentials; gateway'de engelli). Kimlik deposu auth-api'nindir. Security review M2: davet anında hesap açılmaz —
/// <see cref="LookupAsync"/> salt okunur, hesap yalnız <see cref="CreateAsync"/> ile (veli aktivasyonunda). Parola yalnız istek gövdesinde;
/// log/hata mesajına yazılmaz.
/// </summary>
public interface IParentAccountDirectory
{
    /// <summary>Salt okunur: <c>ExistingParent</c> (+ userId/keycloakId) | <c>None</c> | <c>NotParentAccount</c>.</summary>
    Task<ParentAccountCallResult> LookupAsync(string email, CancellationToken ct = default);

    /// <summary>Aktivasyon: yeni veli hesabı (kalıcı parola). E-posta bu arada kaydedildiyse Conflict <c>EmailAlreadyRegistered</c>.</summary>
    Task<ParentAccountCallResult> CreateAsync(string email, string fullName, string pw, CancellationToken ct = default);

    /// <summary>
    /// Security C1: oturum açmış velinin hesabı saf veli ve SUNUCUDAKİ e-postası davet e-postasıysa e-posta doğrulanmış işaretlenir. Conflict
    /// <c>EmailMismatch</c> / <c>NotParentAccount</c>.
    /// </summary>
    Task<ParentAccountCallResult> ConfirmEmailAsync(int parentUserId, string email, CancellationToken ct = default);
}

/// <inheritdoc cref="IParentAccountDirectory"/>
public sealed class AuthApiParentAccountDirectory : IParentAccountDirectory
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceTokenProvider _tokens;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthApiParentAccountDirectory> _logger;

    public AuthApiParentAccountDirectory(
        IHttpClientFactory httpClientFactory, IServiceTokenProvider tokens, IConfiguration configuration,
        ILogger<AuthApiParentAccountDirectory> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokens = tokens;
        _configuration = configuration;
        _logger = logger;
    }

    private string BaseUrl => (_configuration["AuthApiBaseUrl"] ?? string.Empty).TrimEnd('/') + "/api/auth/parent-accounts";

    public Task<ParentAccountCallResult> LookupAsync(string email, CancellationToken ct = default)
        => SendAsync($"{BaseUrl}/lookup", new ParentAccountLookupRequest { Email = email }, "lookup", ct);

    public Task<ParentAccountCallResult> ConfirmEmailAsync(int parentUserId, string email, CancellationToken ct = default)
        => SendAsync($"{BaseUrl}/confirm-email", new ConfirmParentEmailRequest { UserId = parentUserId, Email = email }, "confirm", ct);

    public Task<ParentAccountCallResult> CreateAsync(string email, string fullName, string pw, CancellationToken ct = default)
    {
        var body = new CreateParentAccountRequest { Email = email, FullName = fullName };
        body.Password = pw;
        return SendAsync($"{BaseUrl}/activate", body, "activate", ct);
    }

    private async Task<ParentAccountCallResult> SendAsync(string url, object body, string operation, CancellationToken ct)
    {
        try
        {
            var token = await _tokens.GetAccessTokenAsync(ct);
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);

            // #438 ile aynı yeniden denemesiz named client (hesap açma çift POST üretmesin; tek sınır 30 sn).
            using var response = await _httpClientFactory.CreateClient(AuthApiChildAccountDirectory.HttpClientName).SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                if (operation == "confirm")
                    return new ParentAccountCallResult(ParentAccountCallStatus.Success);
                if (operation == "lookup")
                {
                    var lookup = await response.Content.ReadFromJsonAsync<ParentAccountLookupResponse>(Json, ct);
                    if (lookup == null || string.IsNullOrWhiteSpace(lookup.Outcome)
                        || (lookup.Outcome == ParentAccountOutcomes.ExistingParent && (lookup.UserId is not > 0 || string.IsNullOrWhiteSpace(lookup.KeycloakId))))
                        return Unreadable(operation);
                    return new ParentAccountCallResult(ParentAccountCallStatus.Success, Lookup: lookup);
                }
                var created = await response.Content.ReadFromJsonAsync<CreateParentAccountResponse>(Json, ct);
                if (created == null || created.UserId <= 0 || string.IsNullOrWhiteSpace(created.KeycloakId))
                    return Unreadable(operation);
                return new ParentAccountCallResult(ParentAccountCallStatus.Success, Created: created);
            }

            var errorCode = await ReadErrorCodeAsync(response, ct);
            var status = response.StatusCode switch
            {
                HttpStatusCode.BadRequest => ParentAccountCallStatus.Invalid,
                HttpStatusCode.Conflict => ParentAccountCallStatus.Conflict,
                _ => ParentAccountCallStatus.Unavailable
            };
            if (status == ParentAccountCallStatus.Unavailable)
                _logger.LogWarning("[ParentAccounts] auth-api {Operation} başarısız: HTTP {Status}", operation, (int)response.StatusCode);
            return new ParentAccountCallResult(status, errorCode ?? ParentAccountErrorCodes.Unavailable);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException
                                   || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            _logger.LogWarning(ex, "[ParentAccounts] auth-api {Operation} çağrısı yapılamadı.", operation);
            return new ParentAccountCallResult(ParentAccountCallStatus.Unavailable, ParentAccountErrorCodes.Unavailable);
        }
    }

    private ParentAccountCallResult Unreadable(string operation)
    {
        _logger.LogError("[ParentAccounts] auth-api {Operation} yanıtı çözümlenemedi.", operation);
        return new ParentAccountCallResult(ParentAccountCallStatus.Unavailable, ParentAccountErrorCodes.Unavailable);
    }

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<ParentAccountErrorResponse>(Json, ct);
            return string.IsNullOrWhiteSpace(body?.ErrorCode) ? null : body.ErrorCode;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }
}
