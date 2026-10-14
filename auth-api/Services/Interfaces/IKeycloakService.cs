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
    Task<TokenResponseDto> ExchangeTokenAsync(string code, string codeVerifier, CancellationToken ct = default);
    Task<TokenResponseDto> RefreshTokenAsync(string refreshToken, CancellationToken ct = default);
    Task<List<KeycloakRoleDto>> GetRealmRolesAsync();

    /// <summary>
    /// Issue #152: verilen Keycloak kullanıcılarının <c>enabled</c> bayrağı.
    /// Issue #262 (toplu): <see cref="KeycloakService.AccountStatusBulkThreshold"/>'dan fazla id için kullanıcı başı GET YERİNE
    /// realm'in DEVRE DIŞI kullanıcıları TEK istekle okunur (<c>GET /users?enabled=false&amp;briefRepresentation=true&amp;first=0&amp;max=100</c>).
    /// Toplu yolda: devre dışı listede olan id → false; listede OLMAYAN her id → <c>true</c>. Bu, Keycloak'ta artık bulunmayan
    /// (silinmiş) kullanıcıyı da <c>true</c> gösterir — kullanıcı başı yolda böyle bir kullanıcı sözlükte yer almaz (bilinmiyor).
    /// Sonuç bilgi amaçlıdır; yetkilendirme kararında KULLANILMAMALIDIR.
    /// Tarama sonuçsuzsa kullanıcı başı <c>GET /users/{id}</c> yoluna düşer (en fazla
    /// <see cref="KeycloakService.AccountStatusMaxParallelism"/> eşzamanlı): Keycloak filtreyi yok saydı ya da devre dışı kullanıcı
    /// sayısı tek sayfaya (<see cref="KeycloakService.DisabledScanPageSize"/>) sığmadı — offset sayfalaması eşzamanlı
    /// değişikliklerde kullanıcı atlayabileceği için ikinci sayfa istenmez. Az id'de doğrudan kullanıcı başı yol.
    /// Fail-soft: okunamayan kullanıcı sözlükte YER ALMAZ (tarama hata verirse hiçbiri); admin token'ı alınamazsa
    /// <see cref="KeycloakException"/> fırlatır.
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

    /// <summary>
    /// <c>PUT /users/{id}/reset-password</c> — kalıcı (temporary=false) parola. Seed onarımı ve veli çocuk hesabı (#438) kullanır.
    /// 400 (parola politikası) <see cref="KeycloakFailureKind.Validation"/>; <see cref="KeycloakException.StatusCode"/> HTTP durumu.
    /// </summary>
    Task ResetPasswordAsync(string keycloakUserId, string password, CancellationToken ct = default);

    /// <summary>Realm'in varsayılan rol kompoziti (<c>default-roles-&lt;realm&gt;</c>) — account rolleri/aud bunun içindedir.</summary>
    Task<string> GetRealmDefaultRoleNameAsync(CancellationToken ct = default);

    // ---- Yetkili hesap denetimi (issue #267) — yalnızca audit-privileged-users komutu kullanır ----

    /// <summary>
    /// Realm rolüne DOĞRUDAN atanmış kullanıcılar: <c>GET /roles/{role}/users?first=&amp;max=&amp;briefRepresentation=false</c>,
    /// boş sayfa gelene kadar tüm sayfalar dolaşılır. Salt okunur. Rol realm'de yoksa
    /// <see cref="KeycloakRoleNotFoundException"/>; diğer hatalarda (realm/URL yanlış dahil) <see cref="KeycloakException"/>. Not: kompozit rol/grup üzerinden gelen üyelik bu uçta listelenmez.
    /// </summary>
    Task<IReadOnlyList<KeycloakRoleMember>> GetUsersInRoleAsync(string roleName, CancellationToken ct = default);

    /// <summary>Rolün doğrudan atandığı gruplar (<c>GET /roles/{role}/groups</c>, sayfalı). Rol yoksa <see cref="KeycloakRoleNotFoundException"/>.</summary>
    Task<IReadOnlyList<KeycloakGroupRef>> GetGroupsInRoleAsync(string roleName, CancellationToken ct = default);

    /// <summary>Grubun doğrudan üyeleri (<c>GET /groups/{id}/members</c>, sayfalı).</summary>
    Task<IReadOnlyList<KeycloakRoleMember>> GetGroupMembersAsync(string groupId, CancellationToken ct = default);

    /// <summary>Grubun doğrudan alt grupları (<c>GET /groups/{id}/children</c>, sayfalı) — alt gruplar üst grubun rollerini miras alır.</summary>
    Task<IReadOnlyList<KeycloakGroupRef>> GetSubGroupsAsync(string groupId, CancellationToken ct = default);

    /// <summary>
    /// Kompozit realm rolleri → doğrudan içerdiği realm rolleri (<c>GET /roles/{r}/composites/realm</c>). Kompozit olmayan
    /// roller sözlükte yer almaz. Client rolü kompozitleri <c>view-clients</c> gerektirdiği için kapsam dışı.
    /// </summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetRealmRoleCompositesAsync(CancellationToken ct = default);

    /// <summary>
    /// <c>GET /users/{id}/credentials</c> → kimlik bilgisi türleri (örn. <c>password</c>, <c>otp</c>). Salt okunur. Keycloak 26
    /// kullanıcı temsilinde <c>serviceAccountClientId</c> dönmediği ve client listesi <c>view-clients</c> yetkisi istediği
    /// için servis hesabı doğrulamasında kullanılır: gerçek servis hesabının kimlik bilgisi yoktur.
    /// Fail-closed: 404 dahil her hata <see cref="KeycloakException"/>.
    /// </summary>
    Task<IReadOnlyList<string>> GetUserCredentialTypesAsync(string keycloakUserId, CancellationToken ct = default);

    /// <summary><c>GET /users/{id}/federated-identity</c> → bağlı IdP adları. Fail-closed: 404 dahil her hata <see cref="KeycloakException"/>.</summary>
    Task<IReadOnlyList<string>> GetUserFederatedIdentityProvidersAsync(string keycloakUserId, CancellationToken ct = default);

    // ---- Temizleme (issue #218) — yalnızca DevUserSeedService.CleanupAsync kullanır ----

    /// <summary>
    /// <c>GET /users?{email|username}=&lt;fragment&gt;&amp;exact=false</c> ile alan içinde (infix) arar; boş sayfa gelene
    /// kadar sayfalayarak tümünü döner (<c>search=</c> prefix eşleştirdiği için kullanılmaz). Çağıran sonucu kendi
    /// kuralıyla (örn. seed alanı regex'i, kullanıcı adı üzerinden) yeniden süzmelidir.
    /// </summary>
    Task<IReadOnlyList<KeycloakUserSummary>> SearchUsersAsync(string fragment, KeycloakUserSearchField field, CancellationToken ct = default);

    /// <summary>Kullanıcıyı siler; 404 (zaten yok) durumunda false döner, hata fırlatmaz. Diğer hatalar <see cref="KeycloakException"/>.</summary>
    Task<bool> TryDeleteUserAsync(string keycloakUserId, CancellationToken ct = default);

    // ---- Veli tarafından açılan çocuk hesabı (issue #438) ----

    /// <summary>
    /// E-postasız, required action'sız, <c>parent_managed=true</c> ve <c>child_request_id=&lt;requestId&gt;</c> attribute'lu, etkin
    /// Keycloak kullanıcısı (kimlik bilgisi YOK — parola ayrıca <see cref="ResetPasswordAsync"/> ile). Kullanıcı adı çakışması
    /// <see cref="KeycloakFailureKind.Conflict"/> (409), doğrulama reddi <see cref="KeycloakFailureKind.Validation"/> (400). Yeni
    /// kullanıcının id'sini döner. Rol kümesi ayrıca <see cref="ApplyParentManagedRolesAsync"/> ile daraltılır.
    /// </summary>
    Task<string> CreateParentManagedUserAsync(string username, string firstName, string lastName, Guid requestId, CancellationToken ct = default);

    /// <summary>
    /// <c>GET /users?q=&lt;ad&gt;:&lt;değer&gt;</c> — attribute'u tam olarak bu değere eşit kullanıcıların id'leri (yanıttaki
    /// attribute'la yerelde yeniden doğrulanır; en fazla 5). Hata <see cref="KeycloakException"/>.
    /// </summary>
    Task<IReadOnlyList<string>> FindUserIdsByAttributeAsync(string name, string value, CancellationToken ct = default);

    /// <summary>
    /// Veli tarafından açılan çocuk hesabının rol kümesi (#438): realm varsayılan kompoziti (<c>manage-account</c> taşır) kaldırılır;
    /// yalnız <c>offline_access</c>, <c>uma_authorization</c>, <c>Student</c> ve <c>account</c>/<c>view-profile</c> kalır.
    /// Fail-closed: herhangi bir adım ya da son doğrulama başarısızsa <see cref="KeycloakException"/>.
    /// </summary>
    Task ApplyParentManagedRolesAsync(string keycloakUserId, CancellationToken ct = default);
}
