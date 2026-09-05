using System;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services;

public class KeycloakService : IKeycloakService
{
    private readonly HttpClient _http;
    private readonly KeycloakSettings _keycloakSettings;
    private readonly ILogger<KeycloakService> _logger;

    public KeycloakService(IHttpClientFactory factory, IOptions<KeycloakSettings> options, ILogger<KeycloakService> logger)
    {
        _http = factory.CreateClient();
        _keycloakSettings = options.Value;
        _logger = logger;
    }



    private async Task<string> GetKeycloakAdminTokenAsync()
    {
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", _keycloakSettings.AdminClientId),
            new KeyValuePair<string, string>("client_secret", _keycloakSettings.AdminClientSecret)
        });

        _logger.LogDebug("Requesting Keycloak admin token from {Host}/{TokenUrl} with client_id={ClientId}",
            _keycloakSettings.Host, _keycloakSettings.TokenUrl, _keycloakSettings.AdminClientId);

        var response = await _http.PostAsync($"{_keycloakSettings.Host}/{_keycloakSettings.TokenUrl}", content);
        var json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Keycloak admin token request failed: {Status}", response.StatusCode);
            throw new KeycloakException($"Keycloak admin token request failed: {(int)response.StatusCode}");
        }
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("access_token").GetString();
    }

    public async Task<TokenResponseDto> ExchangeTokenAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(_keycloakSettings.RedirectUri))
        {
            throw new KeycloakException("Keycloak redirect URI is not configured. Set Keycloak:RedirectUri to the same callback URL used in the authorization request (e.g. https://<domain>/app/callback). Do not hard-code localhost for staging/prod.");
        }

        var body = new Dictionary<string, string>
            {
                { "grant_type", "authorization_code" },
                { "client_id", _keycloakSettings.ClientId },
                { "client_secret", _keycloakSettings.ClientSecret },
                { "redirect_uri", _keycloakSettings.RedirectUri },
                { "code", code }
            };

        var response = await _http.PostAsync(
            $"{_keycloakSettings.Host}/{_keycloakSettings.TokenUrl}",
            new FormUrlEncodedContent(body)
        );

        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            // Keycloak error'ını ayıkla
            using var doc = JsonDocument.Parse(content);
            var error = doc.RootElement.GetProperty("error").GetString();
            var description = doc.RootElement.TryGetProperty("error_description", out var descProp)
                ? descProp.GetString()
                : null;
            _logger.LogWarning("Keycloak token exchange failed: {Error} - {Description}", error, description);
            throw new KeycloakException($"Keycloak login failed: {error} - {description}");
        }

        return JsonSerializer.Deserialize<TokenResponseDto>(content)!;
    }

    // App-level roles a user may hold — used to recognize which of a user's *current*
    // Keycloak realm-role mappings are "app roles" that must be cleared before assigning
    // a new one (see SetRoleAsync).
    private static readonly string[] AppRoleNames = { "Student", "Teacher", "Parent" };

    public async Task SetRoleAsync(string keycloakUserId, UserRole userRole)
    {
        if (string.IsNullOrEmpty(keycloakUserId))
        {
            throw new KeycloakException("Keycloak user ID cannot be null or empty.");
        }

        var adminToken = await GetKeycloakAdminTokenAsync();

        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminToken);

        // 1) Resolve the target role's id+name from the realm's role catalog — Keycloak's
        //    role-mappings API needs the full role representation, not just the name.
        var rolesResponse = await _http.GetAsync($"{_keycloakSettings.Host}/{_keycloakSettings.RealmRolesUrl}");
        if (!rolesResponse.IsSuccessStatusCode)
        {
            var error = await rolesResponse.Content.ReadAsStringAsync();
            _logger.LogError("Failed to fetch realm roles from Keycloak: {Error}", error);
            throw new KeycloakException($"Failed to fetch realm roles from Keycloak: {error}");
        }

        var rolesJson = await rolesResponse.Content.ReadAsStringAsync();
        var roles = JsonSerializer.Deserialize<List<KeycloakRoleDto>>(rolesJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new List<KeycloakRoleDto>();

        var role = roles.FirstOrDefault(f => f.name.Equals(userRole.ToString(), StringComparison.OrdinalIgnoreCase));
        if (role == null)
        {
            throw new KeycloakException($"Realm role '{userRole}' was not found in Keycloak.");
        }

        // 2) Make the assignment exclusive: remove any *other* app-role (Student/Teacher/
        //    Parent) realm-role mapping the user currently holds before adding the new
        //    one. Without this, assigning a role is additive and can leave a user with
        //    multiple app roles in Keycloak.
        var currentMappingsResponse = await _http.GetAsync(
            $"{_keycloakSettings.Host}/{_keycloakSettings.UserUrl}/{keycloakUserId}/role-mappings/realm");
        if (!currentMappingsResponse.IsSuccessStatusCode)
        {
            var error = await currentMappingsResponse.Content.ReadAsStringAsync();
            _logger.LogError("Failed to fetch current role mappings from Keycloak: {Error}", error);
            throw new KeycloakException($"Failed to fetch current role mappings from Keycloak: {error}");
        }

        var currentMappingsJson = await currentMappingsResponse.Content.ReadAsStringAsync();
        var currentMappings = JsonSerializer.Deserialize<List<KeycloakRoleDto>>(currentMappingsJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new List<KeycloakRoleDto>();

        var mappingsToRemove = currentMappings
            .Where(m => m != null && !string.IsNullOrEmpty(m.name) &&
                AppRoleNames.Contains(m.name, StringComparer.OrdinalIgnoreCase) &&
                !m.name.Equals(role.name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (mappingsToRemove.Count > 0)
        {
            var removeJson = JsonSerializer.Serialize(mappingsToRemove);
            var removeRequest = new HttpRequestMessage(HttpMethod.Delete,
                $"{_keycloakSettings.Host}/{_keycloakSettings.UserUrl}/{keycloakUserId}/role-mappings/realm")
            {
                Content = new StringContent(removeJson, Encoding.UTF8, "application/json")
            };
            var removeResponse = await _http.SendAsync(removeRequest);
            if (!removeResponse.IsSuccessStatusCode)
            {
                var error = await removeResponse.Content.ReadAsStringAsync();
                _logger.LogError("Failed to remove existing role mapping(s) in Keycloak: {Error}", error);
                throw new KeycloakException($"Failed to remove existing role mapping(s) in Keycloak: {error}");
            }
        }

        // 3) Assign the new (and now sole) app role.
        var roleAssignJson = JsonSerializer.Serialize(new[] { role });
        var assignContent = new StringContent(roleAssignJson, Encoding.UTF8, "application/json");
        var assignResponse = await _http.PostAsync(
            $"{_keycloakSettings.Host}/{_keycloakSettings.UserUrl}/{keycloakUserId}/role-mappings/realm", assignContent);
        if (!assignResponse.IsSuccessStatusCode)
        {
            var error = await assignResponse.Content.ReadAsStringAsync();
            _logger.LogError("Failed to assign role in Keycloak: {Error}", error);
            throw new KeycloakException($"Failed to assign role in Keycloak: {error}");
        }
    }

    public async Task<TokenResponseDto> LoginAsync(string username, string password)
    {
        var body = new Dictionary<string, string>
            {
                { "grant_type", _keycloakSettings.GrantType },
                { "client_id", _keycloakSettings.ClientId },
                { "client_secret", _keycloakSettings.ClientSecret },
                { "username", username },
                { "password", password }
            };

        var response = await _http.PostAsync(
            $"{_keycloakSettings.Host}/{_keycloakSettings.TokenUrl}",
            new FormUrlEncodedContent(body)
        );

        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            // Keycloak error'ını ayıkla
            using var doc = JsonDocument.Parse(content);
            var error = doc.RootElement.GetProperty("error").GetString();
            var description = doc.RootElement.TryGetProperty("error_description", out var descProp)
                ? descProp.GetString()
                : null;
            // _logger.LogWarning("Login failed: {Error} - {Description}", error, description);
            throw new KeycloakException($"Keycloak login failed: {error} - {description}");
        }

        return JsonSerializer.Deserialize<TokenResponseDto>(content)!;
    }

    public async Task LogoutAsync(string userId)
    {
        // 2) Admin token'ı al
        var adminToken = await GetKeycloakAdminTokenAsync();

        // 3) Admin API ile session'ları sonlandır

        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminToken);

        var logoutUrl = string.Format($"{_keycloakSettings.Host}/{_keycloakSettings.LogoutUrl}", userId);

        var resp = await _http.PostAsync(logoutUrl, null);
        if (!resp.IsSuccessStatusCode)
            throw new KeycloakException($"Keycloak logout failed: {await resp.Content.ReadAsStringAsync()}");

    }

    public async Task DeleteUserAsync(string userId)
    {
        var adminToken = await GetKeycloakAdminTokenAsync();

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await _http.DeleteAsync($"{_keycloakSettings.Host}/{_keycloakSettings.UserUrl}/{userId}");

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new KeycloakException($"Keycloak user deletion failed: {error}");
        }
    }






    public async Task<string> CreateUserAsync(string username, string password, string email, string firstName, string lastName)
    {
        var keycloakUser = new
        {
            username = username,
            email = email,
            enabled = true,
            firstName = firstName,
            lastName = lastName,
            credentials = new[]
            {
                new {
                    type = "password",
                    value = password,
                    temporary = false
                }
            }
        };

        var json = JsonSerializer.Serialize(keycloakUser);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var adminToken = await GetKeycloakAdminTokenAsync();

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await _http.PostAsync($"{_keycloakSettings.Host}/{_keycloakSettings.UserUrl}", content);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new KeycloakException($"Keycloak user creation failed: {error}");
        }
        // Keycloak yeni kullanıcıya ID dönmez, ama Location header'ı olur
        var locationHeader = response.Headers.Location?.ToString();
        return locationHeader?.Split("/").Last();
    }
    public Task<string> GetAccessTokenAsync(string username, string password, string clientId, string clientSecret)
    {
        throw new NotImplementedException();
    }

    public Task<string> GetUserInfoAsync(string accessToken)
    {
        throw new NotImplementedException();
    }

    public Task<bool> ValidateTokenAsync(string token)
    {
        throw new NotImplementedException();
    }

    public async Task<TokenResponseDto> RefreshTokenAsync(string refreshToken)
    {
        var parameters = new Dictionary<string, string>
            {
                { "grant_type", "refresh_token" },
                { "client_id", _keycloakSettings.ClientId },
                { "client_secret", _keycloakSettings.ClientSecret },
                { "refresh_token", refreshToken }
            };

        var response = await _http.PostAsync(
             $"{_keycloakSettings.Host}/{_keycloakSettings.TokenUrl}",
            new FormUrlEncodedContent(parameters)
        );

        if (!response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var error = doc.RootElement.GetProperty("error").GetString();
            var description = doc.RootElement.TryGetProperty("error_description", out var descProp)
                ? descProp.GetString()
                : null;
            throw new KeycloakException($"Keycloak refresh token failed: {error} - {description}");
        }

        return await response.Content.ReadFromJsonAsync<TokenResponseDto>();
    }

    public Task<string> GetUserIdFromTokenAsync(string token)
    {
        throw new NotImplementedException();
    }

    public Task<string> GetUserNameFromTokenAsync(string token)
    {
        throw new NotImplementedException();
    }
}
