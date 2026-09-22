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
    /// <summary>Keycloak'ta var ama identity'de seed kaydı yok — yabancı/elle açılmış hesap; dokunulmadı.</summary>
    public const string StatusSkippedForeign = "SkippedForeign";

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
    /// <summary>Created | Existing | Failed | SkippedForeign</summary>
    public string KeycloakStatus { get; set; } = string.Empty;
    /// <summary>Created | Existing | Failed | SkippedForeign</summary>
    public string IdentityStatus { get; set; } = string.Empty;
    public string? Error { get; set; }
}
