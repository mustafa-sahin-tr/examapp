using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace ExamApp.Api.Services.StudentReset;

public interface IServiceTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Obtains a service-to-service access token from Keycloak.
/// Avoids persisting end-user tokens in Hangfire storage.
/// </summary>
public sealed class ServiceTokenProvider : IServiceTokenProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _cachedTokenExpiresAtUtc;

    public ServiceTokenProvider(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        // 30s safety window
        if (!string.IsNullOrWhiteSpace(_cachedToken) && _cachedTokenExpiresAtUtc > DateTimeOffset.UtcNow.AddSeconds(30))
        {
            return _cachedToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
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

            // Prefer admin client for server-to-server calls.
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
            var request = new HttpRequestMessage(HttpMethod.Post, $"{keycloakHost}/{tokenUrl}")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret,
                })
            };

            var response = await httpClient.SendAsync(request, cancellationToken);

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // Keycloak typically returns: {"error":"...","error_description":"..."}
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
                    // Ignore JSON parsing errors; we will still surface the payload.
                }

                var details = !string.IsNullOrWhiteSpace(errorResponse?.ErrorDescription)
                    ? errorResponse.ErrorDescription
                    : (!string.IsNullOrWhiteSpace(errorResponse?.Error) ? errorResponse.Error : payload);

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
