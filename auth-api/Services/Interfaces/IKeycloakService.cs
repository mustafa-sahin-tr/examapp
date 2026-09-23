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
    Task<TokenResponseDto> LoginAsync(string username, string password, CancellationToken ct = default); Task SetRoleAsync(string keycloakUserId, string userRole);
    Task<TokenResponseDto> ExchangeTokenAsync(string code, CancellationToken ct = default);
    Task<TokenResponseDto> RefreshTokenAsync(string refreshToken, CancellationToken ct = default);
    Task<List<KeycloakRoleDto>> GetRealmRolesAsync();

    /// <summary>
    /// Issue #152: verilen Keycloak kullanıcılarının <c>enabled</c> bayrağı. Keycloak admin API id listesiyle toplu
    /// filtre sunmadığı için kullanıcı başı <c>GET /users/{id}</c>, en fazla <see cref="KeycloakService.AccountStatusMaxParallelism"/>
    /// eşzamanlı. Fail-soft: okunamayan (404, hata, zaman aşımı/iptal) kullanıcı sözlükte YER ALMAZ; admin token'ı
    /// alınamazsa <see cref="KeycloakException"/> fırlatır.
    /// </summary>
    Task<IReadOnlyDictionary<string, bool>> GetUsersEnabledAsync(IReadOnlyCollection<string> keycloakUserIds, CancellationToken ct = default);

    // ---- Toplu test verisi (issue #217) — yalnızca DevUserSeedService kullanır ----

    /// <summary>Realm rolünü adına göre çözer (role-mappings API tam temsili ister). Yoksa <see cref="KeycloakException"/>.</summary>
    Task<KeycloakRoleDto> GetRealmRoleAsync(string roleName, CancellationToken ct = default);

    /// <summary>Kullanıcı adı ile (exact) arar; yoksa null. Register (#240) kayıtlı e-posta yolunda Keycloak round-trip eşitlemesi için de kullanır.</summary>
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

    /// <summary>Kullanıcının tek değerli attribute'ları (GET tam temsil). Yoksa boş sözlük.</summary>
    Task<IReadOnlyDictionary<string, string>> GetUserAttributesAsync(string keycloakUserId, CancellationToken ct = default);

    /// <summary>
    /// Verilen attribute'ları (tek değer) ister; hepsi zaten aynıysa PUT atmaz ve false döner. Diğer attribute'lara
    /// dokunulmaz (GET + PUT tam temsil). Realm user-profile/unmanaged policy attribute'a izin vermiyorsa Keycloak değeri
    /// sessizce düşürebilir — çağıran bunu hata saymaz.
    /// </summary>
    Task<bool> EnsureUserAttributesAsync(string keycloakUserId, IReadOnlyDictionary<string, string> desired, CancellationToken ct = default);

    /// <summary><c>PUT /users/{id}/reset-password</c> — kalıcı (temporary=false) parola. Yalnızca seed onarımı kullanır.</summary>
    Task ResetPasswordAsync(string keycloakUserId, string password, CancellationToken ct = default);

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
