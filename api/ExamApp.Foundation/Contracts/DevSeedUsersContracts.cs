using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// auth-api dev-only <c>POST /api/auth/dev/seed-users</c> sözleşmesi (issue #217). Üretici: exam API
/// <c>seed-teachers</c> komutu; tüketici: auth-api <c>DevUserSeedService</c>. Her öğe için Keycloak
/// kullanıcısı + identity <c>User</c> satırı açılır; mevcut olanlar atlanır (idempotent). E-postalar yalnızca
/// <see cref="ExamApp.Foundation.Security.SeedDataConventions.EmailDomain"/> alanında olabilir.
/// </summary>
public sealed class DevSeedUsersRequest
{
    public const int MaxUsersPerRequest = 500;

    public const string ModeAdminApi = "admin-api";
    public const string ModePartialImport = "partial-import";

    [Required, MinLength(1), MaxLength(MaxUsersPerRequest)]
    public List<DevSeedUserItem> Users { get; set; } = new();

    /// <summary>Tüm hesaplar için ortak parola. Yalnızca istek gövdesinde taşınır, saklanmaz/loglanmaz.</summary>
    [Required, MinLength(6)]
    public string Password { get; set; } = string.Empty;

    /// <summary>Realm rolü ve identity <c>User.Role</c>: Student/Teacher/Parent.</summary>
    [Required]
    public string Role { get; set; } = "Teacher";

    /// <summary>Register akışındaki gibi <c>UserPreferredLocaleChangedEvent</c> outbox satırı yazılsın mı?</summary>
    public bool EmitLocaleEvents { get; set; } = true;

    /// <summary><see cref="ModeAdminApi"/> (kullanıcı başına 2 istek) ya da <see cref="ModePartialImport"/> (parti başına tek istek).</summary>
    public string Mode { get; set; } = ModeAdminApi;

    /// <summary>
    /// true ise Keycloak'ta ZATEN var olan (Existing/Adopted) seed kullanıcılarının parolası bu istekteki
    /// <see cref="Password"/> ile sıfırlanır (önceki koşu farklı parolayla açılmış olabilir). Yeni oluşturulanlar zaten
    /// bu parolayla doğar. Varsayılan false: mevcut parolaya dokunulmaz.
    /// </summary>
    public bool ResetPassword { get; set; }

    /// <summary>
    /// Tek seferlik geçiş (yalnızca incident temizliği): Keycloak'ta var, identity'de yok VE
    /// <see cref="ExamApp.Foundation.Security.SeedDataConventions.KeycloakOriginAttribute"/> işareti taşımayan (işaret
    /// eklenmeden önce açılmış) hesapları da sahiplen; sahiplenilen hesaba işaret yazılır. Varsayılan false: işaretsiz
    /// yetim <c>SkippedForeign</c>.
    /// </summary>
    public bool AdoptUnmarked { get; set; }
}

public sealed class DevSeedUserItem
{
    /// <summary>Keycloak username = e-posta. Yalnızca seed alanı (SeedDataConventions.IsSeedEmail).</summary>
    [Required, MaxLength(100)]
    public string Email { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string FirstName { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string LastName { get; set; } = string.Empty;

    /// <summary>Exam DB okul id'si; Keycloak <c>school_id</c> attribute'una yazılır (JWT mapper claim'e taşır). Pozitif ya da null.</summary>
    public int? SchoolId { get; set; }
}

public sealed class DevSeedUsersResponse
{
    public const string StatusCreated = "Created";
    public const string StatusExisting = "Existing";
    public const string StatusFailed = "Failed";
    /// <summary>Seed alanında ama identity'de <c>IsSeedData=false</c> satırı var — elle açılmış hesap; dokunulmadı.</summary>
    public const string StatusSkippedForeign = "SkippedForeign";
    /// <summary>
    /// Keycloak'ta vardı, identity'de HİÇ satır yoktu (önceki koşu Keycloak'tan sonra kesilmiş — yetim): bu koşunun
    /// planındaki deterministik seed e-postası olduğu için sahiplenildi — roller/<c>school_id</c> onarıldı, identity
    /// satırı açıldı (<see cref="DevSeedUserResult.IdentityStatus"/> = Created). Parola yalnızca
    /// <see cref="DevSeedUsersRequest.ResetPassword"/> ile sıfırlanır.
    /// </summary>
    public const string StatusAdopted = "Adopted";

    public List<DevSeedUserResult> Results { get; set; } = new();

    /// <summary>Keycloak çağrılarında geçen toplam süre (ms).</summary>
    public long KeycloakElapsedMs { get; set; }

    /// <summary>Identity DB yazımında geçen süre (ms).</summary>
    public long IdentityDbElapsedMs { get; set; }

    public string Mode { get; set; } = string.Empty;
}

public sealed class DevSeedUserResult
{
    public string Email { get; set; } = string.Empty;
    public string? KeycloakId { get; set; }
    /// <summary>Identity DB <c>User.Id</c> — exam API <c>Teacher.UserId</c> bunu bekler.</summary>
    public int? UserId { get; set; }
    /// <summary>Created | Existing | Adopted | Failed | SkippedForeign</summary>
    public string KeycloakStatus { get; set; } = string.Empty;
    /// <summary>Created | Existing | Failed | SkippedForeign</summary>
    public string IdentityStatus { get; set; } = string.Empty;
    /// <summary>Keycloak parolası bu istekle sıfırlandı (<see cref="DevSeedUsersRequest.ResetPassword"/>; yalnızca Existing/Adopted).</summary>
    public bool PasswordReset { get; set; }
    public string? Error { get; set; }
}

// ---------------------------------------------------------------------------------------------------
// Temizleme (issue #218): POST /api/auth/dev/seed-users/cleanup
// ---------------------------------------------------------------------------------------------------

/// <summary>
/// auth-api dev-only <c>POST /api/auth/dev/seed-users/cleanup</c> sözleşmesi (issue #218). Üretici: exam API
/// <c>seed-cleanup</c> komutu. Kapsam SABİTTİR ve istekle genişletilemez: yalnızca
/// <see cref="ExamApp.Foundation.Security.SeedDataConventions.EmailDomain"/> alanındaki Keycloak kullanıcıları
/// ve identity'de <c>IsSeedData=true</c> olan satırlar. İstek yalnızca kapsamı DARALTABİLİR
/// (<see cref="ExcludeUserIds"/>: exam tarafında bağımlı verisi olduğu için atlanan öğretmenlerin identity id'leri).
/// </summary>
public sealed class DevSeedCleanupRequest
{
    /// <summary>true (varsayılan): hiçbir şey silinmez, yalnızca plan raporlanır.</summary>
    public bool DryRun { get; set; } = true;

    /// <summary>Silinmeyecek identity <c>User.Id</c> listesi (exam'de atlanan seed öğretmenler). Keycloak'ta da korunur.</summary>
    public List<int> ExcludeUserIds { get; set; } = new();

    /// <summary>
    /// true ise Keycloak'ta kullanıcı adı seed desenine uyan, identity'de HİÇ satırı olmayan VE
    /// <see cref="ExamApp.Foundation.Security.SeedDataConventions.KeycloakOriginAttribute"/> işaretini taşıyan yetim
    /// hesaplar da silinir (kesilmiş bir seed koşusunun kalıntısı). Kapsam yine seed alanıyla sınırlıdır: seed alanı dışı
    /// kullanıcılar, identity'de <c>IsSeedData=false</c> satırı olanlar ve işaretsiz yetimler bu bayrakla da silinmez.
    /// <see cref="ExcludeUserIds"/> yetimlere uygulanamaz (identity id'leri yoktur). Varsayılan false.
    /// </summary>
    public bool IncludeOrphans { get; set; }
}

public sealed class DevSeedCleanupResponse
{
    public const string StatusDeleted = "Deleted";
    /// <summary>Dry-run: silinecekti.</summary>
    public const string StatusPlanned = "Planned";
    /// <summary>Keycloak'ta zaten yok (önceki kısmi koşu) — identity yine silinir.</summary>
    public const string StatusMissing = "Missing";
    /// <summary><see cref="DevSeedCleanupRequest.ExcludeUserIds"/> ile korundu.</summary>
    public const string StatusExcluded = "Excluded";
    /// <summary>Seed alanında ama identity'de <c>IsSeedData=false</c> (ya da hiç yok, Keycloak'ta var) — yabancı, dokunulmadı.</summary>
    public const string StatusSkippedForeign = "SkippedForeign";
    public const string StatusFailed = "Failed";

    public bool DryRun { get; set; }

    public int KeycloakDeleted { get; set; }
    public int KeycloakMissing { get; set; }
    public int KeycloakExcluded { get; set; }
    public int KeycloakSkippedForeign { get; set; }
    public int KeycloakFailed { get; set; }
    /// <summary>
    /// Keycloak'ta seed desenli ama identity'de hiç satırı olmayan yetim hesaplar — <see cref="DevSeedCleanupRequest.IncludeOrphans"/>
    /// ile kapsama alınanlar (Planned/Deleted/Failed olarak <see cref="Users"/> içinde, <see cref="DevSeedCleanupUser.Orphan"/> = true).
    /// Bayrak kapalıyken bunlar <see cref="KeycloakSkippedForeign"/> içinde sayılır.
    /// </summary>
    public int KeycloakOrphans { get; set; }

    public int IdentityDeleted { get; set; }
    public int IdentityExcluded { get; set; }
    public int IdentityFailed { get; set; }

    public long KeycloakElapsedMs { get; set; }
    public long IdentityDbElapsedMs { get; set; }

    /// <summary>Kapsamdaki tüm hesaplar (identity ∪ Keycloak) — exam tarafı e-posta ↔ UserId eşlemesi için kullanır.</summary>
    public List<DevSeedCleanupUser> Users { get; set; } = new();
}

public sealed class DevSeedCleanupUser
{
    public string Email { get; set; } = string.Empty;
    /// <summary>Identity <c>User.Id</c> (aktif satır; yoksa ilk kalıntı); yalnızca Keycloak'ta bulunduysa null.</summary>
    public int? UserId { get; set; }
    /// <summary>Aynı e-postadaki TÜM identity satırları (soft-delete kalıntıları dahil) — hepsi birlikte silinir ya da korunur.</summary>
    public List<int> IdentityIds { get; set; } = new();
    public string? KeycloakId { get; set; }
    /// <summary>Deleted | Planned | Missing | Excluded | SkippedForeign | Failed</summary>
    public string KeycloakStatus { get; set; } = string.Empty;
    /// <summary>Deleted | Planned | Missing | Excluded | SkippedForeign | Failed (yetimde Missing: identity satırı hiç yok)</summary>
    public string IdentityStatus { get; set; } = string.Empty;
    /// <summary>Keycloak'ta var, identity'de hiç satır yok; <see cref="DevSeedCleanupRequest.IncludeOrphans"/> ile kapsama alındı.</summary>
    public bool Orphan { get; set; }
    public string? Error { get; set; }
}
