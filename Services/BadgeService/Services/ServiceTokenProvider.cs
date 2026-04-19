using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BadgeService.Services;

/// <summary>
/// Obtains a service-to-service access token from Keycloak using client_credentials grant.
/// Caches token with 30s safety window before expiry to minimize token requests.
/// </summary>
public sealed class ServiceTokenProvider : IServiceTokenProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ServiceTokenProvider> _logger;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _cachedTokenExpiresAtUtc;

    public ServiceTokenProvider(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<ServiceTokenProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        // 30s safety window: only use cached token if it won't expire in next 30 seconds
        if (!string.IsNullOrWhiteSpace(_cachedToken) && _cachedTokenExpiresAtUtc > DateTimeOffset.UtcNow.AddSeconds(30))
        {
            _logger.LogDebug("🔄 Using cached token (expires in {SecondsRemaining}s)",
                Math.Round((_cachedTokenExpiresAtUtc - DateTimeOffset.UtcNow).TotalSeconds));
            return _cachedToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after lock acquisition
            if (!string.IsNullOrWhiteSpace(_cachedToken) && _cachedTokenExpiresAtUtc > DateTimeOffset.UtcNow.AddSeconds(30))
            {
                return _cachedToken;
            }

            var keycloakHost = _configuration["Keycloak:Host"]?.TrimEnd('/');
            var tokenUrl = _configuration["Keycloak:TokenUrl"]?.TrimStart('/');
            if (string.IsNullOrWhiteSpace(keycloakHost) || string.IsNullOrWhiteSpace(tokenUrl))
            {
                throw new InvalidOperationException("Keycloak config missing (Keycloak:Host / Keycloak:TokenUrl)");
            }

            // Prefer admin client for server-to-server calls
            var clientId = _configuration["Keycloak:AdminClientId"];
            var clientSecret = _configuration["Keycloak:AdminClientSecret"];
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                clientId = _configuration["Keycloak:ClientId"];
                clientSecret = _configuration["Keycloak:ClientSecret"];
            }

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                throw new InvalidOperationException("Keycloak client credentials missing (Keycloak:AdminClientId/AdminClientSecret or ClientId/ClientSecret)");
            }

            var httpClient = _httpClientFactory.CreateClient();
            var tokenRequest = new HttpRequestMessage(HttpMethod.Post, $"{keycloakHost}/{tokenUrl}")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret,
                })
            };

            _logger.LogInformation("🔐 Requesting service token from Keycloak (client_id: {ClientId})", clientId);

            var response = await httpClient.SendAsync(tokenRequest, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                KeycloakTokenResponse? errorResponse = null;
                try
                {
                    errorResponse = JsonSerializer.Deserialize<KeycloakTokenResponse>(payload, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
                catch
                {
                    // Ignore JSON parsing errors
                }

                var details = !string.IsNullOrWhiteSpace(errorResponse?.ErrorDescription)
                    ? errorResponse.ErrorDescription
                    : (!string.IsNullOrWhiteSpace(errorResponse?.Error) ? errorResponse.Error : payload);

                _logger.LogError("❌ Keycloak token request failed: {StatusCode} {ReasonPhrase}. Details: {Details}",
                    response.StatusCode, response.ReasonPhrase, details);

                throw new InvalidOperationException(
                    $"Keycloak token request failed: {(int)response.StatusCode} {response.ReasonPhrase}. Details: {details}");
            }

            var tokenResponse = JsonSerializer.Deserialize<KeycloakTokenResponse>(payload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (string.IsNullOrWhiteSpace(tokenResponse?.AccessToken))
            {
                throw new InvalidOperationException("Keycloak token response missing access_token");
            }

            var expiresInSeconds = tokenResponse.ExpiresIn > 0 ? tokenResponse.ExpiresIn : 60;
            _cachedToken = tokenResponse.AccessToken;
            _cachedTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds);

            _logger.LogInformation("✅ Service token obtained (expires in {ExpiresInSeconds}s)", expiresInSeconds);

            return _cachedToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private sealed class KeycloakTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; set; }
    }
}
