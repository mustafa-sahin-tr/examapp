using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace ExamApp.Api.Services.StudentReset;

public interface IBadgeResetApiClient
{
    /// <param name="resetAtUtc">issue #396: sıfırlama çizgisi (exam API saati); BadgeService bundan önce gönderilmiş
    /// AnswerSubmittedEvent'leri yok sayar.</param>
    Task ResetUserAsync(int userId, DateTime resetAtUtc, CancellationToken cancellationToken);
}

public sealed class BadgeResetApiClient : IBadgeResetApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IServiceTokenProvider _tokenProvider;

    public BadgeResetApiClient(IHttpClientFactory httpClientFactory, IConfiguration configuration, IServiceTokenProvider tokenProvider)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _tokenProvider = tokenProvider;
    }

    /// <summary>issue #396: hata mesajına eklenen yanıt gövdesinin üst sınırı (log/Hangfire paneli şişmesin).</summary>
    internal const int MaxErrorBodyLength = 512;

    public async Task ResetUserAsync(int userId, DateTime resetAtUtc, CancellationToken cancellationToken)
    {
        var baseUrl = _configuration["BadgeApiBaseUrl"]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("BadgeApiBaseUrl is not configured");
        }

        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);

        var httpClient = _httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // We support both patterns:
        // 1) Direct container base URL: http://exam-badge-api:8006  -> /api/reset/users/{id}
        // 2) Gateway base URL:          http://ocelot-gateway:5678 -> /api/badge/reset/users/{id}
        // 3) If baseUrl already includes /api/badge, allow /reset/users/{id}
        // issue #396: "O" (round-trip, +00:00/Z) → BadgeService DateTimeOffset olarak bağlar; URL-encode '+' için şart.
        var query = "?resetAtUtc=" + Uri.EscapeDataString(
            new DateTimeOffset(DateTime.SpecifyKind(resetAtUtc, DateTimeKind.Utc)).ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        var candidates = new List<string>
        {
            $"{baseUrl}/api/reset/users/{userId}{query}",
            $"{baseUrl}/api/badge/reset/users/{userId}{query}",
            $"{baseUrl}/reset/users/{userId}{query}",
        };

        HttpResponseMessage? lastResponse = null;
        string? lastBody = null;
        foreach (var url in candidates)
        {
            lastResponse?.Dispose();

            var response = await httpClient.DeleteAsync(url, cancellationToken);
            lastResponse = response;
            lastBody = Truncate(await response.Content.ReadAsStringAsync(cancellationToken));

            if (response.IsSuccessStatusCode)
            {
                return;
            }

            // If it's not a 404, fail fast (auth/config errors shouldn't be masked).
            if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                throw new HttpRequestException(
                    $"Badge reset call failed: {(int)response.StatusCode} {response.ReasonPhrase}. Url: {url}. Body: {lastBody}");
            }
        }

        throw new HttpRequestException(
            $"Badge reset endpoint not found (404) for all candidates. BaseUrl: {baseUrl}. " +
            $"Tried: {string.Join(" | ", candidates)}. Last body: {lastBody}");
    }

    /// <summary>issue #396: hata gövdesini <see cref="MaxErrorBodyLength"/> karaktere kırpar.</summary>
    internal static string Truncate(string? body) =>
        body is null ? string.Empty
        : body.Length <= MaxErrorBodyLength ? body
        : body[..MaxErrorBodyLength] + "…(truncated)";
}
