using System;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services;

public class KeycloakService : IKeycloakService
{
    private readonly HttpClient _http;
    private readonly KeycloakSettings _keycloakSettings;
    
    public KeycloakService(IHttpClientFactory factory, IOptions<KeycloakSettings> options)
    {
        _http = factory.CreateClient();
        _keycloakSettings = options.Value;
    }

    

    private async Task<string> GetKeycloakAdminTokenAsync()
    {
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", _keycloakSettings.AdminClientId),
            new KeyValuePair<string, string>("client_secret", _keycloakSettings.AdminClientSecret)
        });

        var response = await _http.PostAsync($"{_keycloakSettings.Host}/{_keycloakSettings.TokenUrl}", content);
        var json = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("access_token").GetString();
    }

    public async Task<TokenResponseDto> ExchangeTokenAsync(string code) 
    {
        var body = new Dictionary<string, string>
            {
                { "grant_type", "authorization_code" },
                { "client_id", _keycloakSettings.ClientId },
                { "client_secret", _keycloakSettings.ClientSecret },
                { "redirect_uri", "http://localhost:5678/callback" },
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
                throw new KeycloakException($"Keycloak login failed: {error} - {description}");                
            }

            return JsonSerializer.Deserialize<TokenResponseDto>(content)!;
    }

     public async Task SetRoleAsync(string keycloakUserId, UserRole userRole) 
    {

        var adminToken = await GetKeycloakAdminTokenAsync();

        // 3) Admin API ile session'ları sonlandır

        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminToken);
            
        if (string.IsNullOrEmpty(keycloakUserId)){
            throw new KeycloakException("Keycloak user ID cannot be null or empty.");
        }            

        var rolesResponse = await _http.GetAsync($"{_keycloakSettings.Host}/{_keycloakSettings.RealmRolesUrl}");
        var rolesJson = await rolesResponse.Content.ReadAsStringAsync();

        var roles = JsonSerializer.Deserialize<List<KeycloakRoleDto>>(rolesJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        var role = roles.FirstOrDefault(f => f.name.Equals(userRole.ToString()));

        var roleAssignJson = JsonSerializer.Serialize(new[] { role });
        var assignContent = new StringContent(roleAssignJson, Encoding.UTF8, "application/json");

        await _http.PostAsync($"{_keycloakSettings.Host}/{_keycloakSettings.UserUrl}/{keycloakUserId}/role-mappings/realm", assignContent);
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
