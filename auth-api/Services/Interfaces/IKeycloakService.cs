using System;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Interfaces;

public interface IKeycloakService
{
    Task<string> GetAccessTokenAsync(string username, string password, string clientId, string clientSecret);
    Task<string> GetUserInfoAsync(string accessToken);
    Task<bool> ValidateTokenAsync(string token);
    Task<string> GetUserIdFromTokenAsync(string token);
    Task<string> GetUserNameFromTokenAsync(string token);
    Task<string> CreateUserAsync(string username, string password, string email, string firstName, string lastName);
    Task DeleteUserAsync(string userId);
    Task LogoutAsync(string refreshToken);
    Task<TokenResponseDto> LoginAsync(string username, string password); Task SetRoleAsync(string keycloakUserId, string userRole);
    Task<TokenResponseDto> ExchangeTokenAsync(string code);
    Task<TokenResponseDto> RefreshTokenAsync(string refreshToken);
    Task<List<KeycloakRoleDto>> GetRealmRolesAsync();

    // ---- Toplu test verisi (issue #217) — yalnızca DevUserSeedService kullanır ----

    /// <summary>Realm rolünü adına göre çözer (role-mappings API tam temsili ister). Yoksa <see cref="KeycloakException"/>.</summary>
    Task<KeycloakRoleDto> GetRealmRoleAsync(string roleName, CancellationToken ct = default);

    /// <summary>Kullanıcı adı ile (exact) arar; yoksa null.</summary>
    Task<string?> FindUserIdByUsernameAsync(string username, CancellationToken ct = default);

    /// <summary>
    /// Kullanıcıyı attribute'larıyla oluşturur. 409 (zaten var) durumunda kullanıcı adıyla bulup
    /// <c>AlreadyExisted=true</c> döner — idempotent yeniden koşu için.
    /// </summary>
    Task<KeycloakUserCreateResult> CreateSeedUserAsync(KeycloakSeedUser user, string password, CancellationToken ct = default);

    /// <summary>
    /// Verilen realm rolünü kullanıcıya ekler (mevcut mapping'ler kontrol edilmez, kaldırılmaz —
    /// yeni oluşturulmuş kullanıcı için tek istek). Rolü tekrar eklemek Keycloak'ta no-op'tur.
    /// </summary>
    Task AddRealmRoleMappingAsync(string keycloakUserId, KeycloakRoleDto role, CancellationToken ct = default);

    /// <summary>Kullanıcının mevcut realm rol adları.</summary>
    Task<IReadOnlyList<string>> GetUserRealmRoleNamesAsync(string keycloakUserId, CancellationToken ct = default);

    /// <summary>Realm'in varsayılan rol kompoziti (<c>default-roles-&lt;realm&gt;</c>) — account rolleri/aud bunun içindedir.</summary>
    Task<string> GetRealmDefaultRoleNameAsync(CancellationToken ct = default);

    /// <summary>
    /// Realm partial import: tek istekte çok kullanıcı, <c>ifResourceExists=SKIP</c>, verilen realm rolleri ve
    /// önceden hash'lenmiş parola ile. <c>manage-realm</c> yetkisi gerekir. DİKKAT: import'ta <c>realmRoles</c>
    /// varsayılan rolü otomatik eklemez — çağıran <see cref="GetRealmDefaultRoleNameAsync"/> sonucunu listeye koymalı.
    /// </summary>
    Task<KeycloakPartialImportResult> PartialImportUsersAsync(
        IReadOnlyList<KeycloakSeedUser> users, IReadOnlyList<string> realmRoleNames, KeycloakHashedCredential credential,
        CancellationToken ct = default);

    // ---- Temizleme (issue #218) — yalnızca DevUserSeedService.CleanupAsync kullanır ----

    /// <summary>
    /// <c>GET /users?{email|username}=&lt;fragment&gt;&amp;exact=false</c> ile alan içinde (infix) arar; boş sayfa gelene
    /// kadar sayfalayarak tümünü döner (<c>search=</c> prefix eşleştirdiği için kullanılmaz). Çağıran sonucu kendi
    /// kuralıyla (örn. seed alanı regex'i, kullanıcı adı üzerinden) yeniden süzmelidir.
    /// </summary>
    Task<IReadOnlyList<KeycloakUserSummary>> SearchUsersAsync(string fragment, KeycloakUserSearchField field, CancellationToken ct = default);

    /// <summary>Kullanıcıyı siler; 404 (zaten yok) durumunda false döner, hata fırlatmaz. Diğer hatalar <see cref="KeycloakException"/>.</summary>
    Task<bool> TryDeleteUserAsync(string keycloakUserId, CancellationToken ct = default);
}
