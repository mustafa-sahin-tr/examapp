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
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services;

public class KeycloakService : IKeycloakService
{
    /// <summary>
    /// Toplu admin işlemleri (partial import, sayfalı arama, seed onarımı — issue #217/#218) için named client.
    /// Program.cs'te ServiceDefaults'un standart resilience handler'ından MUAF tutulur: standart handler her denemeyi
    /// 10 sn'de iptal edip 3 kez yeniler; 500'lük partialImport bunu aşınca aynı parti ikinci kez gönderilir ve
    /// Keycloak 409 "Duplicate resource error" döner (import idempotent değil). Bu client'ta yeniden deneme yok,
    /// zaman aşımı uzun (<see cref="AdminHttpClientTimeout"/>).
    /// </summary>
    public const string AdminHttpClientName = "KeycloakAdmin";
    public static readonly TimeSpan AdminHttpClientTimeout = TimeSpan.FromMinutes(30);

    private readonly HttpClient _http;
    private readonly HttpClient _adminHttp;
    private readonly KeycloakSettings _keycloakSettings;

    public KeycloakService(IHttpClientFactory factory, IOptions<KeycloakSettings> options)
    {
        _http = factory.CreateClient();
        _adminHttp = factory.CreateClient(AdminHttpClientName);
        _keycloakSettings = options.Value;
    }

    private Uri GetKeycloakBaseUri()
    {
        if (!string.IsNullOrWhiteSpace(_keycloakSettings.Host) &&
            Uri.TryCreate(_keycloakSettings.Host, UriKind.Absolute, out var hostUri))
        {
            return hostUri;
        }

        if (!string.IsNullOrWhiteSpace(_keycloakSettings.Authority) &&
            Uri.TryCreate(_keycloakSettings.Authority, UriKind.Absolute, out var authorityUri))
        {
            return new Uri($"{authorityUri.Scheme}://{authorityUri.Authority}");
        }

        throw new KeycloakException("Keycloak base URL is not configured. Set Keycloak:Host or Keycloak:Authority.");
    }

    private Uri BuildKeycloakUri(string pathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl))
        {
            throw new KeycloakException("Keycloak URL segment is not configured.");
        }

        // if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var absolute))
        // {
        //     return absolute;
        // }

        var baseUri = GetKeycloakBaseUri();
        var relative = pathOrUrl.TrimStart('/');
        return new Uri(baseUri, relative);
    }



    private static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions IgnoreNulls = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    // Admin token'ı servis örneği (scoped → HTTP isteği) ömrünce önbellekle: toplu seed'de (issue #217)
    // her Keycloak çağrısı için ayrıca token istemek istek sayısını ikiye katlıyordu. Tekil akışlarda
    // davranış değişmez (istek başına en fazla bir token). Süre: Keycloak'ın expires_in'i - 10 sn.
    private string? _cachedAdminToken;
    private DateTimeOffset _cachedAdminTokenExpiresAt;

    private async Task<string> GetKeycloakAdminTokenAsync(CancellationToken ct = default)
    {
        if (_cachedAdminToken is not null && _cachedAdminTokenExpiresAt > DateTimeOffset.UtcNow)
        {
            return _cachedAdminToken;
        }

        var (token, expiresInSeconds) = await RequestKeycloakAdminTokenAsync(ct);
        _cachedAdminToken = token;
        _cachedAdminTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, expiresInSeconds - 10));
        return token;
    }

    private async Task<(string Token, int ExpiresInSeconds)> RequestKeycloakAdminTokenAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_keycloakSettings.AdminClientId) ||
            string.IsNullOrWhiteSpace(_keycloakSettings.AdminClientSecret))
        {
            throw new KeycloakException("Keycloak admin client credentials are not configured. Set KeycloakSettings:AdminClientId and KeycloakSettings:AdminClientSecret.");
        }

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", _keycloakSettings.AdminClientId),
            new KeyValuePair<string, string>("client_secret", _keycloakSettings.AdminClientSecret)
        });

        var response = await _http.PostAsync(BuildKeycloakUri(_keycloakSettings.TokenUrl), content, ct);
        var json = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var details = string.IsNullOrWhiteSpace(json) ? "<empty response>" : json;
            throw new KeycloakException($"Failed to get admin token from Keycloak. Status={(int)response.StatusCode} {response.ReasonPhrase}. Body: {details}");
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("access_token", out var tokenProp))
            {
                var token = tokenProp.GetString();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var exp) && exp.TryGetInt32(out var seconds) && seconds > 0
                        ? seconds
                        : 60;
                    return (token, expiresIn);
                }
            }

            // Common Keycloak error schema: {"error": "...", "error_description": "..."}
            var error = doc.RootElement.TryGetProperty("error", out var errProp) ? errProp.GetString() : null;
            var description = doc.RootElement.TryGetProperty("error_description", out var descProp) ? descProp.GetString() : null;
            throw new KeycloakException($"Keycloak admin token response did not contain 'access_token'. error={error ?? "<none>"} description={description ?? "<none>"}. Raw: {json}");
        }
        catch (JsonException jex)
        {
            throw new KeycloakException($"Failed to parse Keycloak admin token response as JSON. Raw: {json}", jex);
        }
    }

    public async Task<TokenResponseDto> ExchangeTokenAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_keycloakSettings.RedirectUri))
        {
            throw new KeycloakException("Keycloak redirect URI is not configured. Set Keycloak:RedirectUri (e.g. https://<domain>/app/callback).");
        }

        var body = new Dictionary<string, string>
            {
                { "grant_type", "authorization_code" },
                { "client_id", _keycloakSettings.ClientId },
                { "client_secret", _keycloakSettings.ClientSecret },
                { "redirect_uri", _keycloakSettings.RedirectUri },
                { "code", code }
            };

        var content = await PostTokenRequestAsync(BuildKeycloakUri(_keycloakSettings.TokenUrl), body, "code exchange", ct);
        return JsonSerializer.Deserialize<TokenResponseDto>(content)!;
    }

    /// <summary>
    /// Token uç noktasına form isteği gönderir ve başarısızlığı <see cref="KeycloakFailureKind"/> ile
    /// sınıflandırır (issue #231): <c>invalid_grant</c> → <see cref="KeycloakFailureKind.InvalidGrant"/>;
    /// ağ hatası/zaman aşımı/devre kesici/5xx → <see cref="KeycloakFailureKind.ProviderUnavailable"/>;
    /// diğerleri (ör. <c>invalid_client</c> — yapılandırma hatası) → <see cref="KeycloakFailureKind.Unexpected"/>.
    /// Exception mesajı log içindir; istemciye controller'ın genel mesajı gider.
    /// </summary>
    private async Task<string> PostTokenRequestAsync(Uri tokenUri, Dictionary<string, string> body, string operation, CancellationToken ct)
    {
        try
        {
            using var form = new FormUrlEncodedContent(body);
            using var response = await _http.PostAsync(tokenUri, form, ct);
            var content = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode)
            {
                return content;
            }

            throw ClassifyTokenError((int)response.StatusCode, content, operation);
        }
        catch (HttpRequestException ex)
        {
            throw new KeycloakException($"Keycloak {operation} failed: token endpoint unreachable ({ex.Message})", ex,
                StatusCodes.Status503ServiceUnavailable, KeycloakFailureKind.ProviderUnavailable);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // HttpClient.Timeout. İstemci isteği iptal ettiyse (RequestAborted) exception olduğu gibi yükselir.
            throw new KeycloakException($"Keycloak {operation} failed: token endpoint timed out", ex,
                StatusCodes.Status503ServiceUnavailable, KeycloakFailureKind.ProviderUnavailable);
        }
        catch (Polly.ExecutionRejectedException ex)
        {
            // ServiceDefaults standart resilience handler'ı: deneme zaman aşımı (TimeoutRejectedException) / devre kesici açık.
            throw new KeycloakException($"Keycloak {operation} failed: resilience pipeline rejected the call ({ex.GetType().Name})", ex,
                StatusCodes.Status503ServiceUnavailable, KeycloakFailureKind.ProviderUnavailable);
        }
    }

    private static KeycloakException ClassifyTokenError(int statusCode, string content, string operation)
    {
        string? error = null;
        string? description = null;
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                error = doc.RootElement.TryGetProperty("error", out var errProp) ? errProp.GetString() : null;
                description = doc.RootElement.TryGetProperty("error_description", out var descProp) ? descProp.GetString() : null;
            }
        }
        catch (JsonException)
        {
            // Proxy/HTML hata sayfası vb. — sınıflandırma durum koduna göre yapılır.
        }

        if (statusCode >= 500)
        {
            return new KeycloakException($"Keycloak {operation} failed: HTTP {statusCode} {error} - {description}",
                StatusCodes.Status503ServiceUnavailable, KeycloakFailureKind.ProviderUnavailable);
        }

        if (string.Equals(error, "invalid_grant", StringComparison.Ordinal))
        {
            return new KeycloakException($"Keycloak {operation} failed: {error} - {description}",
                StatusCodes.Status401Unauthorized, KeycloakFailureKind.InvalidGrant);
        }

        return new KeycloakException($"Keycloak {operation} failed: HTTP {statusCode} {error ?? "<no error>"} - {description}");
    }

    // App-level roles a user may hold — used both to validate the incoming role and to
    // recognize which of a user's *current* Keycloak realm-role mappings are "app roles"
    // that must be cleared before assigning a new one (see SetRoleAsync).
    private static readonly string[] AppRoleNames = { "Student", "Teacher", "Parent" };

    public async Task SetRoleAsync(string keycloakUserId, string userRole)
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
        var rolesResponse = await _http.GetAsync(BuildKeycloakUri(_keycloakSettings.RealmRolesUrl));
        if (!rolesResponse.IsSuccessStatusCode)
        {
            var error = await rolesResponse.Content.ReadAsStringAsync();
            throw new KeycloakException($"Failed to fetch realm roles from Keycloak: {error}");
        }

        var rolesJson = await rolesResponse.Content.ReadAsStringAsync();
        var roles = JsonSerializer.Deserialize<List<KeycloakRoleDto>>(rolesJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new List<KeycloakRoleDto>();

        var role = roles.FirstOrDefault(f => f.name.Equals(userRole, StringComparison.OrdinalIgnoreCase));
        if (role == null)
        {
            throw new KeycloakException($"Realm role '{userRole}' was not found in Keycloak.");
        }

        // 2) Make the assignment exclusive: remove any *other* app-role (Student/Teacher/
        //    Parent) realm-role mapping the user currently holds before adding the new
        //    one. This is what prevents role-stacking — without it, calling this method
        //    (even a single time, let alone concurrently) is additive and can leave a
        //    user with multiple app roles in Keycloak.
        var currentMappingsResponse = await _http.GetAsync(
            BuildKeycloakUri($"{_keycloakSettings.UserUrl}/{keycloakUserId}/role-mappings/realm"));
        if (!currentMappingsResponse.IsSuccessStatusCode)
        {
            var error = await currentMappingsResponse.Content.ReadAsStringAsync();
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
                BuildKeycloakUri($"{_keycloakSettings.UserUrl}/{keycloakUserId}/role-mappings/realm"))
            {
                Content = new StringContent(removeJson, Encoding.UTF8, "application/json")
            };
            var removeResponse = await _http.SendAsync(removeRequest);
            if (!removeResponse.IsSuccessStatusCode)
            {
                var error = await removeResponse.Content.ReadAsStringAsync();
                throw new KeycloakException($"Failed to remove existing role mapping(s) in Keycloak: {error}");
            }
        }

        // 3) Assign the new (and now sole) app role.
        var roleAssignJson = JsonSerializer.Serialize(new[] { role });
        var assignContent = new StringContent(roleAssignJson, Encoding.UTF8, "application/json");
        var assignResponse = await _http.PostAsync(
            BuildKeycloakUri($"{_keycloakSettings.UserUrl}/{keycloakUserId}/role-mappings/realm"), assignContent);
        if (!assignResponse.IsSuccessStatusCode)
        {
            var error = await assignResponse.Content.ReadAsStringAsync();
            throw new KeycloakException($"Failed to assign role in Keycloak: {error}");
        }
    }

    public async Task<TokenResponseDto> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var body = new Dictionary<string, string>
            {
                { "grant_type", _keycloakSettings.GrantType },
                { "client_id", _keycloakSettings.ClientId },
                { "client_secret", _keycloakSettings.ClientSecret },
                { "username", username },
                { "password", password }
            };

        var content = await PostTokenRequestAsync(BuildKeycloakUri(_keycloakSettings.TokenUrl), body, "login", ct);
        return JsonSerializer.Deserialize<TokenResponseDto>(content)!;
    }

    public async Task LogoutAsync(string userId)
    {
        // 2) Admin token'ı al
        var adminToken = await GetKeycloakAdminTokenAsync();

        // 3) Admin API ile session'ları sonlandır

        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminToken);

        var logoutUrl = string.Format(_keycloakSettings.LogoutUrl, userId);

        var resp = await _http.PostAsync(BuildKeycloakUri(logoutUrl), null);
        if (!resp.IsSuccessStatusCode)
            throw new KeycloakException($"Keycloak logout failed: {await resp.Content.ReadAsStringAsync()}");

    }

    public async Task DeleteUserAsync(string userId)
    {
        var adminToken = await GetKeycloakAdminTokenAsync();

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await _http.DeleteAsync(BuildKeycloakUri($"{_keycloakSettings.UserUrl}/{userId}"));

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

        var response = await _http.PostAsync(BuildKeycloakUri(_keycloakSettings.UserUrl), content);

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

    public async Task<TokenResponseDto> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        var parameters = new Dictionary<string, string>
            {
                { "grant_type", "refresh_token" },
                { "client_id", _keycloakSettings.ClientId },
                { "client_secret", _keycloakSettings.ClientSecret },
                { "refresh_token", refreshToken }
            };

        // Aynı sınıflandırma (#231): süresi dolmuş/iptal edilmiş refresh token → InvalidGrant (global handler 401).
        var content = await PostTokenRequestAsync(
            new Uri($"{_keycloakSettings.Host}/{_keycloakSettings.TokenUrl}"), parameters, "refresh token", ct);
        return JsonSerializer.Deserialize<TokenResponseDto>(content)!;
    }
    public async Task<List<KeycloakRoleDto>> GetRealmRolesAsync()
    {
        try
        {
            var adminToken = await GetKeycloakAdminTokenAsync();
            if (string.IsNullOrEmpty(adminToken))
            {
                throw new KeycloakException("Failed to get admin token");
            }

            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

            var rolesResponse = await _http.GetAsync(BuildKeycloakUri(_keycloakSettings.RealmRolesUrl));

            if (!rolesResponse.IsSuccessStatusCode)
            {
                var errorContent = await rolesResponse.Content.ReadAsStringAsync();
                throw new KeycloakException($"Failed to fetch realm roles: {errorContent}");
            }

            var rolesJson = await rolesResponse.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(rolesJson))
            {
                return new List<KeycloakRoleDto>();
            }

            var roles = JsonSerializer.Deserialize<List<KeycloakRoleDto>>(rolesJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (roles == null)
            {
                return new List<KeycloakRoleDto>();
            }

            // Sistem rollerini filtrele - sadece uygulama için oluşturulan rolleri döndür
            var filteredRoles = roles.Where(role =>
                role != null &&
                !role.composite && // Composite olmayan roller
                !string.IsNullOrEmpty(role.name) && // Boş isimli rolleri hariç tut
                (_keycloakSettings.ExcludedRoles == null || !_keycloakSettings.ExcludedRoles.Contains(role.name)) && // Konfigürasyonda belirtilen rolleri hariç tut
                !role.name.StartsWith("default-roles") && // Default role gruplarını hariç tut
                !role.name.Contains("uma_") // UMA authorization rollerini hariç tut
            ).ToList();

            return filteredRoles ?? new List<KeycloakRoleDto>();
        }
        catch (KeycloakException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or Polly.ExecutionRejectedException)
        {
            throw new KeycloakException($"Error fetching roles: Keycloak unreachable ({ex.GetType().Name})", ex,
                StatusCodes.Status503ServiceUnavailable, KeycloakFailureKind.ProviderUnavailable);
        }
        catch (Exception ex)
        {
            throw new KeycloakException($"Error fetching roles: {ex.Message}", ex);
        }
    }

    public Task<string> GetUserIdFromTokenAsync(string token)
    {
        throw new NotImplementedException();
    }

    public Task<string> GetUserNameFromTokenAsync(string token)
    {
        throw new NotImplementedException();
    }

    // ------------------------------------------------------------------
    // Toplu test verisi (issue #217) — DevUserSeedService
    // ------------------------------------------------------------------

    private async Task AuthorizeAdminAsync(CancellationToken ct)
    {
        var adminToken = await GetKeycloakAdminTokenAsync(ct);
        _adminHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
    }

    /// <summary>UserUrl = admin/realms/{realm}/users → aynı realm'in admin kökü (admin/realms/{realm}).</summary>
    private string RealmAdminPath()
    {
        var path = _keycloakSettings.UserUrl.TrimEnd('/');
        return path.EndsWith("/users", StringComparison.OrdinalIgnoreCase) ? path[..^"/users".Length] : path;
    }

    private static Dictionary<string, string[]>? ToAttributes(KeycloakSeedUser user)
        => user.Attributes is { Count: > 0 }
            ? user.Attributes.ToDictionary(kv => kv.Key, kv => new[] { kv.Value })
            : null;

    public async Task<KeycloakRoleDto> GetRealmRoleAsync(string roleName, CancellationToken ct = default)
    {
        await AuthorizeAdminAsync(ct);

        // GET /roles/{role-name} tek istekte tam temsili döner.
        var response = await _adminHttp.GetAsync(BuildKeycloakUri($"{_keycloakSettings.RealmRolesUrl}/{Uri.EscapeDataString(roleName)}"), ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new KeycloakException($"Realm role '{roleName}' was not found in Keycloak.");
        }
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new KeycloakException($"Failed to fetch realm role '{roleName}' from Keycloak: {error}");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<KeycloakRoleDto>(json, CaseInsensitive)
            ?? throw new KeycloakException($"Realm role '{roleName}' response could not be parsed.");
    }

    public async Task<string?> FindUserIdByUsernameAsync(string username, CancellationToken ct = default)
    {
        await AuthorizeAdminAsync(ct);

        var response = await _adminHttp.GetAsync(BuildKeycloakUri(
            $"{_keycloakSettings.UserUrl}?username={Uri.EscapeDataString(username)}&exact=true&briefRepresentation=true&max=2"), ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new KeycloakException($"Failed to search Keycloak user '{username}': {error}");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            // exact=true olsa da adı bir kez daha karşılaştır (eski Keycloak sürümleri prefix eşleştirir).
            var name = element.TryGetProperty("username", out var u) ? u.GetString() : null;
            if (string.Equals(name, username, StringComparison.OrdinalIgnoreCase) &&
                element.TryGetProperty("id", out var id))
            {
                return id.GetString();
            }
        }
        return null;
    }

    public async Task<KeycloakUserCreateResult> CreateSeedUserAsync(KeycloakSeedUser user, string password, CancellationToken ct = default)
    {
        var representation = new Dictionary<string, object?>
        {
            ["username"] = user.Username,
            ["email"] = user.Email,
            ["emailVerified"] = true,
            ["enabled"] = true,
            ["firstName"] = user.FirstName,
            ["lastName"] = user.LastName,
            ["attributes"] = ToAttributes(user),
            ["credentials"] = new[] { new { type = "password", value = password, temporary = false } }
        };

        var content = new StringContent(JsonSerializer.Serialize(representation, IgnoreNulls), Encoding.UTF8, "application/json");

        await AuthorizeAdminAsync(ct);
        var response = await _adminHttp.PostAsync(BuildKeycloakUri(_keycloakSettings.UserUrl), content, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var existingId = await FindUserIdByUsernameAsync(user.Username, ct)
                ?? throw new KeycloakException($"Keycloak reported '{user.Username}' as existing but it could not be found by username.");
            return new KeycloakUserCreateResult(existingId, AlreadyExisted: true);
        }

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new KeycloakException($"Keycloak user creation failed for '{user.Username}': {error}");
        }

        var id = response.Headers.Location?.ToString().Split('/').Last();
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new KeycloakException($"Keycloak did not return a Location header for created user '{user.Username}'.");
        }
        return new KeycloakUserCreateResult(id, AlreadyExisted: false);
    }

    public async Task AddRealmRoleMappingAsync(string keycloakUserId, KeycloakRoleDto role, CancellationToken ct = default)
    {
        await AuthorizeAdminAsync(ct);

        var body = new StringContent(JsonSerializer.Serialize(new[] { role }), Encoding.UTF8, "application/json");
        var response = await _adminHttp.PostAsync(
            BuildKeycloakUri($"{_keycloakSettings.UserUrl}/{keycloakUserId}/role-mappings/realm"), body, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new KeycloakException($"Failed to assign role '{role.name}' in Keycloak: {error}");
        }
    }

    public async Task<IReadOnlyList<string>> GetUserRealmRoleNamesAsync(string keycloakUserId, CancellationToken ct = default)
    {
        await AuthorizeAdminAsync(ct);

        var response = await _adminHttp.GetAsync(
            BuildKeycloakUri($"{_keycloakSettings.UserUrl}/{keycloakUserId}/role-mappings/realm"), ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new KeycloakException($"Failed to fetch current role mappings from Keycloak: {error}");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var mappings = JsonSerializer.Deserialize<List<KeycloakRoleDto>>(json, CaseInsensitive) ?? new List<KeycloakRoleDto>();
        return mappings.Where(m => !string.IsNullOrEmpty(m?.name)).Select(m => m.name).ToList();
    }

    public async Task<string> GetRealmDefaultRoleNameAsync(CancellationToken ct = default)
    {
        await AuthorizeAdminAsync(ct);

        var response = await _adminHttp.GetAsync(BuildKeycloakUri(RealmAdminPath()), ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new KeycloakException($"Failed to read realm representation from Keycloak: {json}");
        }

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("defaultRole", out var role) &&
            role.TryGetProperty("name", out var name) &&
            name.GetString() is { Length: > 0 } value)
        {
            return value;
        }

        // Keycloak konvansiyonu: default-roles-<realm>
        var realm = doc.RootElement.TryGetProperty("realm", out var r) ? r.GetString() : null;
        if (string.IsNullOrEmpty(realm))
            throw new KeycloakException("Realm representation has neither defaultRole nor realm name.");
        return $"default-roles-{realm}";
    }

    public async Task<KeycloakPartialImportResult> PartialImportUsersAsync(
        IReadOnlyList<KeycloakSeedUser> users, IReadOnlyList<string> realmRoleNames, KeycloakHashedCredential credential,
        CancellationToken ct = default)
    {
        var payload = new
        {
            ifResourceExists = "SKIP",
            users = users.Select(u => new Dictionary<string, object?>
            {
                ["username"] = u.Username,
                ["email"] = u.Email,
                ["emailVerified"] = true,
                ["enabled"] = true,
                ["firstName"] = u.FirstName,
                ["lastName"] = u.LastName,
                ["realmRoles"] = realmRoleNames.ToArray(),
                ["attributes"] = ToAttributes(u),
                ["credentials"] = new[]
                {
                    new
                    {
                        type = "password",
                        temporary = false,
                        secretData = credential.SecretData,
                        credentialData = credential.CredentialData
                    }
                }
            }).ToList()
        };

        var content = new StringContent(JsonSerializer.Serialize(payload, IgnoreNulls), Encoding.UTF8, "application/json");

        await AuthorizeAdminAsync(ct);

        var response = await _adminHttp.PostAsync(BuildKeycloakUri($"{RealmAdminPath()}/partialImport"), content, ct);

        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new KeycloakException($"Keycloak partial import failed: {(int)response.StatusCode} {json}");
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var results = new Dictionary<string, KeycloakPartialImportEntry>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("results", out var arr))
        {
            foreach (var r in arr.EnumerateArray())
            {
                var type = r.TryGetProperty("resourceType", out var t) ? t.GetString() : null;
                if (!string.Equals(type, "USER", StringComparison.OrdinalIgnoreCase)) continue;
                var name = r.TryGetProperty("resourceName", out var n) ? n.GetString() : null;
                if (string.IsNullOrEmpty(name)) continue;
                var action = r.TryGetProperty("action", out var a) ? a.GetString() ?? string.Empty : string.Empty;
                var id = r.TryGetProperty("id", out var i) ? i.GetString() : null;
                results[name] = new KeycloakPartialImportEntry(action, id);
            }
        }

        return new KeycloakPartialImportResult(
            root.TryGetProperty("added", out var added) ? added.GetInt32() : 0,
            root.TryGetProperty("skipped", out var skipped) ? skipped.GetInt32() : 0,
            root.TryGetProperty("overwritten", out var over) ? over.GetInt32() : 0,
            results);
    }

    // ---- Temizleme (issue #218) ----

    public async Task<IReadOnlyList<KeycloakUserSummary>> SearchUsersAsync(string fragment, KeycloakUserSearchField field, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fragment))
            throw new ArgumentException("Arama parçası boş olamaz.", nameof(fragment));

        await AuthorizeAdminAsync(ct);

        const int pageSize = 500;
        var param = field == KeycloakUserSearchField.Username ? "username" : "email";
        var all = new List<KeycloakUserSummary>();
        var first = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            // `search=` prefix eşleştirir (Keycloak 22+); `email=`/`username=` exact=false iken infix ("contains").
            // Boş sayfa gelene kadar döner (sayfa < max ile durmak, Keycloak'ın max'ı kırptığı sürümlerde eksik bırakır).
            var response = await _adminHttp.GetAsync(BuildKeycloakUri(
                $"{_keycloakSettings.UserUrl}?{param}={Uri.EscapeDataString(fragment)}&exact=false&briefRepresentation=false&first={first}&max={pageSize}"), ct);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                throw new KeycloakException($"Failed to search Keycloak users '{fragment}': {error}");
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var page = 0;
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                page++;
                var id = element.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                var username = element.TryGetProperty("username", out var u) ? u.GetString() : null;
                var email = element.TryGetProperty("email", out var e) ? e.GetString() : null;
                if (id is null || username is null) continue;
                all.Add(new KeycloakUserSummary(id, username, email, ReadSingleValuedAttributes(element)));
            }

            if (page == 0) break;
            first += page;
        }

        return all;
    }

    public async Task<bool> TryDeleteUserAsync(string keycloakUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keycloakUserId))
            throw new ArgumentException("Keycloak kullanıcı id'si boş olamaz.", nameof(keycloakUserId));

        await AuthorizeAdminAsync(ct);

        var response = await _adminHttp.DeleteAsync(BuildKeycloakUri($"{_keycloakSettings.UserUrl}/{Uri.EscapeDataString(keycloakUserId)}"), ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return false;
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new KeycloakException($"Keycloak user deletion failed ({(int)response.StatusCode}): {error}");
        }
        return true;
    }

    // ---- Yetim adoptasyonu / parola onarımı (seed-teachers --reset-password) ----

    private static Dictionary<string, string> ReadSingleValuedAttributes(JsonElement element)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (element.TryGetProperty("attributes", out var attrEl) && attrEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in attrEl.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Array && prop.Value.GetArrayLength() > 0)
                    attributes[prop.Name] = prop.Value[0].GetString() ?? string.Empty;
                else if (prop.Value.ValueKind == JsonValueKind.String)
                    attributes[prop.Name] = prop.Value.GetString() ?? string.Empty;
            }
        }
        return attributes;
    }

    private async Task<(Dictionary<string, JsonElement> Representation, Dictionary<string, string[]> Attributes)> GetUserRepresentationAsync(string keycloakUserId, CancellationToken ct)
    {
        await AuthorizeAdminAsync(ct);

        var userUri = BuildKeycloakUri($"{_keycloakSettings.UserUrl}/{Uri.EscapeDataString(keycloakUserId)}");
        var getResponse = await _adminHttp.GetAsync(userUri, ct);
        var json = await getResponse.Content.ReadAsStringAsync(ct);
        if (!getResponse.IsSuccessStatusCode)
        {
            throw new KeycloakException($"Failed to read Keycloak user '{keycloakUserId}' ({(int)getResponse.StatusCode}): {json}");
        }

        // Tam temsili sözlük olarak al: PUT'ta bilinmeyen alanlar olduğu gibi geri gider, yalnızca attributes değişir.
        var rep = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
            ?? throw new KeycloakException($"Keycloak user '{keycloakUserId}' representation could not be parsed.");

        var attributes = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (rep.TryGetValue("attributes", out var attrEl) && attrEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in attrEl.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Array)
                    attributes[prop.Name] = prop.Value.EnumerateArray().Select(v => v.GetString() ?? string.Empty).ToArray();
            }
        }
        return (rep, attributes);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetUserAttributesAsync(string keycloakUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keycloakUserId))
            throw new ArgumentException("Keycloak kullanıcı id'si boş olamaz.", nameof(keycloakUserId));

        var (_, attributes) = await GetUserRepresentationAsync(keycloakUserId, ct);
        return attributes.Where(kv => kv.Value.Length > 0).ToDictionary(kv => kv.Key, kv => kv.Value[0], StringComparer.Ordinal);
    }

    public async Task<bool> EnsureUserAttributesAsync(string keycloakUserId, IReadOnlyDictionary<string, string> desired, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keycloakUserId))
            throw new ArgumentException("Keycloak kullanıcı id'si boş olamaz.", nameof(keycloakUserId));
        ArgumentNullException.ThrowIfNull(desired);
        if (desired.Count == 0) return false;

        var (rep, attributes) = await GetUserRepresentationAsync(keycloakUserId, ct);

        var changed = false;
        foreach (var (name, value) in desired)
        {
            if (attributes.TryGetValue(name, out var current) && current.Length == 1 && string.Equals(current[0], value, StringComparison.Ordinal))
                continue;
            attributes[name] = new[] { value };
            changed = true;
        }
        if (!changed) return false;

        // Salt okunur alanlar PUT'ta reddedilebilir; temsilden düşür.
        rep.Remove("userProfileMetadata");
        rep.Remove("access");
        var body = new Dictionary<string, object?>();
        foreach (var kv in rep) body[kv.Key] = kv.Value;
        body["attributes"] = attributes;

        var userUri = BuildKeycloakUri($"{_keycloakSettings.UserUrl}/{Uri.EscapeDataString(keycloakUserId)}");
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var putResponse = await _adminHttp.PutAsync(userUri, content, ct);
        if (!putResponse.IsSuccessStatusCode)
        {
            var error = await putResponse.Content.ReadAsStringAsync(ct);
            throw new KeycloakException($"Failed to update attributes on Keycloak user '{keycloakUserId}' ({(int)putResponse.StatusCode}): {error}");
        }
        return true;
    }

    public async Task ResetPasswordAsync(string keycloakUserId, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keycloakUserId))
            throw new ArgumentException("Keycloak kullanıcı id'si boş olamaz.", nameof(keycloakUserId));
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Parola boş olamaz.", nameof(password));

        await AuthorizeAdminAsync(ct);

        var content = new StringContent(
            JsonSerializer.Serialize(new { type = "password", value = password, temporary = false }), Encoding.UTF8, "application/json");
        var response = await _adminHttp.PutAsync(
            BuildKeycloakUri($"{_keycloakSettings.UserUrl}/{Uri.EscapeDataString(keycloakUserId)}/reset-password"), content, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new KeycloakException($"Keycloak password reset failed for user '{keycloakUserId}' ({(int)response.StatusCode}): {error}");
        }
    }
}
