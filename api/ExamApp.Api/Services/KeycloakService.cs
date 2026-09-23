using System;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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



    // issue #156: admin token bu (scoped) örnek içinde yeniden kullanılır — tek istek içindeki rol/reset/logout
    // çağrıları tek token alır. Süresi (expires_in) dolmadan 30 sn önce yenilenir.
    private string? _cachedAdminToken;
    private DateTimeOffset _cachedAdminTokenExpiresAt;

    private async Task<string> GetKeycloakAdminTokenAsync(CancellationToken ct = default)
    {
        if (_cachedAdminToken is not null && DateTimeOffset.UtcNow < _cachedAdminTokenExpiresAt)
            return _cachedAdminToken;

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", _keycloakSettings.AdminClientId),
            new KeyValuePair<string, string>("client_secret", _keycloakSettings.AdminClientSecret)
        });

        _logger.LogDebug("Requesting Keycloak admin token from {Host}/{TokenUrl} with client_id={ClientId}",
            _keycloakSettings.Host, _keycloakSettings.TokenUrl, _keycloakSettings.AdminClientId);

        using var response = await _http.PostAsync($"{_keycloakSettings.Host}/{_keycloakSettings.TokenUrl}", content, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Keycloak admin token request failed: {Status}", response.StatusCode);
            throw new KeycloakException($"Keycloak admin token request failed: {(int)response.StatusCode}", 502);
        }
        using var doc = JsonDocument.Parse(json);
        // access_token yoksa KeyNotFoundException yerine anlamlı KeycloakException (çağıranlar 502'ye eşler).
        if (!doc.RootElement.TryGetProperty("access_token", out var tokenElement)
            || tokenElement.ValueKind != JsonValueKind.String
            || string.IsNullOrEmpty(tokenElement.GetString()))
        {
            _logger.LogError("Keycloak admin token response has no access_token");
            throw new KeycloakException("Keycloak admin token response has no access_token", 502);
        }

        var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var exp) && exp.TryGetInt32(out var seconds) ? seconds : 60;
        _cachedAdminToken = tokenElement.GetString()!;
        _cachedAdminTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, expiresIn - 30));
        return _cachedAdminToken;
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

    public async Task SetSchoolIdAttributeAsync(string keycloakUserId, int? schoolId)
    {
        if (string.IsNullOrEmpty(keycloakUserId))
        {
            throw new KeycloakException("Keycloak user ID cannot be null or empty.");
        }

        var adminToken = await GetKeycloakAdminTokenAsync();

        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminToken);

        // Diğer alanları (email, firstName, lastName, diğer attribute'lar vb.) ezmemek için
        // TAM UserRepresentation'ı çek, sadece attributes.school_id'yi değiştirip aynı nesneyi
        // geri gönder. Keycloak 24 declarative user-profile ile eksik gövde (ör. yalnızca
        // "attributes") required alanları (email vb.) boşaltabilir / 400 döndürebilir.
        var getResponse = await _http.GetAsync($"{_keycloakSettings.Host}/{_keycloakSettings.UserUrl}/{keycloakUserId}");
        if (!getResponse.IsSuccessStatusCode)
        {
            var error = await getResponse.Content.ReadAsStringAsync();
            _logger.LogError("Failed to fetch Keycloak user for school_id attribute update: {Error}", error);
            throw new KeycloakException($"Failed to fetch Keycloak user: {error}");
        }

        var userJson = await getResponse.Content.ReadAsStringAsync();
        var userNode = JsonNode.Parse(userJson)?.AsObject()
            ?? throw new KeycloakException("Failed to parse Keycloak user representation.");

        var attributesNode = userNode["attributes"]?.AsObject();
        if (attributesNode is null)
        {
            attributesNode = new JsonObject();
            userNode["attributes"] = attributesNode;
        }

        if (schoolId.HasValue)
        {
            attributesNode["school_id"] = new JsonArray(JsonValue.Create(schoolId.Value.ToString()));
        }
        else
        {
            attributesNode.Remove("school_id");
        }

        var updateContent = new StringContent(userNode.ToJsonString(), Encoding.UTF8, "application/json");

        var putResponse = await _http.PutAsync(
            $"{_keycloakSettings.Host}/{_keycloakSettings.UserUrl}/{keycloakUserId}", updateContent);
        if (!putResponse.IsSuccessStatusCode)
        {
            var error = await putResponse.Content.ReadAsStringAsync();
            _logger.LogError("Failed to update school_id attribute in Keycloak: {Error}", error);
            throw new KeycloakException($"Failed to update school_id attribute in Keycloak: {error}");
        }
    }

    // ---- issue #156: admin şifre sıfırlama (#155 aynı altyapıyı kullanır) ----
    // Bu metotlar paylaşılan _http.DefaultRequestHeaders'ı DEĞİŞTİRMEZ; token istek başına eklenir.
    // Hata mesajlarına Keycloak yanıt gövdesi konmaz (yalnızca durum kodu): reset-password hata gövdesi
    // politika ayrıntısı taşıyabilir ve exception mesajları loglara düşer.

    private const string RealmManagementClientId = "realm-management";

    // realm-management client'ının iç UUID'si (Host+UserUrl başına süreç ömrü boyunca sabit).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> RealmManagementUuidCache = new();

    private static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

    public async Task<KeycloakUserRolesDto> GetUserRolesAsync(string keycloakUserId, CancellationToken ct = default)
    {
        var realmRoles = await GetRoleNamesAsync($"{AdminUserUrl(keycloakUserId)}/role-mappings/realm/composite", ct);

        // realm-management client rolleri (realm-admin, manage-users, ...): ETKİN (composite + grup) roller için
        // client UUID'si gerekir. /clients sorgusu view-clients ister (exam-admin'de yok); UUID bunun yerine admin
        // servis hesabının kendi role-mapping'lerinden okunur (manage-users taşıdığı için realm-management girdisi hep var).
        var uuid = await GetRealmManagementClientUuidAsync(ct);
        IReadOnlyList<string> clientRoles;
        if (uuid is not null)
        {
            clientRoles = await GetRoleNamesAsync(
                $"{AdminUserUrl(keycloakUserId)}/role-mappings/clients/{Uri.EscapeDataString(uuid)}/composite", ct);
        }
        else
        {
            // Yedek: yalnızca DOĞRUDAN atanmış client rolleri (grup/composite üzerinden gelenler görünmez).
            _logger.LogWarning("realm-management client UUID çözülemedi; yalnızca doğrudan client rol atamaları kontrol ediliyor");
            using var response = await SendAsAdminAsync(HttpMethod.Get, $"{AdminUserUrl(keycloakUserId)}/role-mappings", null, ct);
            EnsureSuccess(response, "Keycloak role mapping lookup failed");
            var mappings = await response.Content.ReadFromJsonAsync<KeycloakRoleMappingsDto>(CaseInsensitive, ct);
            clientRoles = mappings?.ClientMappings is { } cm && cm.TryGetValue(RealmManagementClientId, out var rm)
                ? rm.Mappings?.Where(r => !string.IsNullOrEmpty(r?.name)).Select(r => r.name).ToList() ?? new List<string>()
                : new List<string>();
        }

        return new KeycloakUserRolesDto(realmRoles, clientRoles);
    }

    public async Task<string> ResetPasswordAsync(string keycloakUserId, CancellationToken ct = default)
    {
        var generated = TemporaryPasswordGenerator.Generate();
        var body = JsonSerializer.Serialize(new { type = "password", value = generated, temporary = true });

        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await SendAsAdminAsync(HttpMethod.Put, $"{AdminUserUrl(keycloakUserId)}/reset-password", content, ct);
        // Gövde bilerek okunmuyor/loglanmıyor (bkz. yukarıdaki not).
        EnsureSuccess(response, "Keycloak password reset failed");
        return generated;
    }

    public async Task LogoutUserSessionsAsync(string keycloakUserId, CancellationToken ct = default)
    {
        using var response = await SendAsAdminAsync(HttpMethod.Post, $"{AdminUserUrl(keycloakUserId)}/logout", content: null, ct);
        EnsureSuccess(response, "Keycloak session logout failed");
    }

    // GET → alan değiştir → PUT deseninde geri gönderilmeyen salt okunur / hesaplanan alanlar.
    private static readonly string[] ReadOnlyUserRepresentationFields = ["userProfileMetadata", "access"];

    public async Task SetEnabledAsync(string keycloakUserId, bool enabled, CancellationToken ct = default)
    {
        var url = AdminUserUrl(keycloakUserId);

        // SetSchoolIdAttributeAsync ile aynı desen: TAM UserRepresentation çekilir, yalnızca "enabled" değiştirilir ve
        // geri gönderilir. Kısmi gövde, realm'in declarative user-profile ayarına göre diğer alanları (email, attribute'lar)
        // boşaltabilir / 400 döndürebilir. Gövdeler loglanmaz (yalnızca durum kodu).
        JsonObject user;
        using (var getResponse = await SendAsAdminAsync(HttpMethod.Get, url, content: null, ct))
        {
            EnsureSuccess(getResponse, "Keycloak account status lookup failed");
            user = JsonNode.Parse(await getResponse.Content.ReadAsStringAsync(ct))?.AsObject()
                ?? throw new KeycloakException("Keycloak account status lookup failed: empty representation", 502);
        }

        foreach (var field in ReadOnlyUserRepresentationFields)
            user.Remove(field);
        user["enabled"] = enabled;

        using var content = new StringContent(user.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await SendAsAdminAsync(HttpMethod.Put, url, content, ct);
        EnsureSuccess(response, "Keycloak account status update failed");
    }

    private async Task<IReadOnlyList<string>> GetRoleNamesAsync(string url, CancellationToken ct)
    {
        using var response = await SendAsAdminAsync(HttpMethod.Get, url, content: null, ct);
        EnsureSuccess(response, "Keycloak role lookup failed");
        var roles = await response.Content.ReadFromJsonAsync<List<KeycloakRoleDto>>(CaseInsensitive, ct) ?? new List<KeycloakRoleDto>();
        return roles.Where(r => !string.IsNullOrEmpty(r?.name)).Select(r => r.name).ToList();
    }

    private async Task<string?> GetRealmManagementClientUuidAsync(CancellationToken ct)
    {
        var cacheKey = $"{_keycloakSettings.Host}/{_keycloakSettings.UserUrl}";
        if (RealmManagementUuidCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var serviceAccountId = SubjectOf(await GetKeycloakAdminTokenAsync(ct));
        if (serviceAccountId is null)
            return null;

        using var response = await SendAsAdminAsync(HttpMethod.Get, $"{AdminUserUrl(serviceAccountId)}/role-mappings", null, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Admin service account role mappings could not be read: {Status}", (int)response.StatusCode);
            return null;
        }

        var mappings = await response.Content.ReadFromJsonAsync<KeycloakRoleMappingsDto>(CaseInsensitive, ct);
        var uuid = mappings?.ClientMappings is { } cm && cm.TryGetValue(RealmManagementClientId, out var rm) ? rm.Id : null;
        if (string.IsNullOrEmpty(uuid))
            return null;
        RealmManagementUuidCache[cacheKey] = uuid;
        return uuid;
    }

    /// <summary>JWT payload'undaki <c>sub</c> (imza doğrulanmaz — token'ı Keycloak'tan biz aldık).</summary>
    private static string? SubjectOf(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2)
                return null;
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
            return doc.RootElement.TryGetProperty("sub", out var sub) ? sub.GetString() : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    private void EnsureSuccess(HttpResponseMessage response, string what)
    {
        if (response.IsSuccessStatusCode)
            return;
        _logger.LogWarning("{What}: {Status}", what, (int)response.StatusCode);
        throw new KeycloakException($"{what}: {(int)response.StatusCode}", (int)response.StatusCode);
    }

    private string AdminUserUrl(string keycloakUserId)
    {
        if (string.IsNullOrWhiteSpace(keycloakUserId))
            throw new KeycloakException("Keycloak user ID cannot be null or empty.", 400);
        return $"{_keycloakSettings.Host}/{_keycloakSettings.UserUrl}/{Uri.EscapeDataString(keycloakUserId)}";
    }

    private async Task<HttpResponseMessage> SendAsAdminAsync(HttpMethod method, string url, HttpContent? content, CancellationToken ct)
    {
        var adminToken = await GetKeycloakAdminTokenAsync(ct);
        using var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        return await _http.SendAsync(request, ct);
    }
}
