using System;

namespace ExamApp.Api.Models.Dtos.Admin;

/// <summary>
/// Admin öğretmen listesi satırı (issue #152). Yanıt zarfı <see cref="Paged{T}"/>:
/// <c>{ pageNumber, pageSize, totalCount, items: [...] }</c>.
/// </summary>
public class AdminTeacherListItemDto
{
    /// <summary>Teacher kaydının id'si.</summary>
    public int Id { get; set; }

    // issue #262: auth-api kullanıcı id'si (UserId) artık dönülmez — UI kullanmıyor (yalnızca spec fixture'larında vardı),
    // iç kimliği gereksiz yere dışarı verir. Lookup için yalnızca sunucu tarafında kullanılır (öğrenci listesiyle aynı).

    /// <summary>auth-api'den çözümlenir; erişilemezse boş string.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// MASKELİ e-posta (issue #246): <c>a***@okul.k12.tr</c> — bkz. <see cref="ExamApp.Api.Helpers.EmailMask"/>.
    /// auth-api'den çözümlenemezse boş string. Tam adres bu listede hiç dönülmez.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Bağlı okul; bağımsız/okulsuz öğretmen için null.</summary>
    public int? SchoolId { get; set; }

    /// <summary>Okul adı (School.Name); okul bağlantısı yoksa legacy Teacher.SchoolName; o da yoksa null.</summary>
    public string? SchoolName { get; set; }

    public bool IsIndependentTutor { get; set; }

    /// <summary><c>Pending</c> | <c>Approved</c> | <c>Rejected</c>.</summary>
    public string ApprovalStatus { get; set; } = string.Empty;

    /// <summary>
    /// Keycloak hesap durumu: true = aktif, false = devre dışı, null = bilinmiyor
    /// (auth-api/Keycloak erişilemedi ya da kullanıcı Keycloak'ta yok).
    /// </summary>
    public bool? IsEnabled { get; set; }

    /// <summary>
    /// issue #289: öğretmen HESABI onaylı ve askıda değil (Teachers.AccountApprovedAt dolu) — öğretmen özellikleri açık.
    /// UI "askıya al" aksiyonunu yalnızca bu true iken gösterir.
    /// </summary>
    public bool AccountApproved { get; set; }

    /// <summary>issue #289: hesap onayı askıda (Teachers.AccountSuspendedAt dolu). UI "askıyı kaldır" aksiyonunu bu true iken gösterir.</summary>
    public bool AccountSuspended { get; set; }

    /// <summary>issue #289: askıya alma anı (UTC); askıda değilse null.</summary>
    public DateTime? AccountSuspendedAt { get; set; }

    /// <summary>issue #289: admin'in girdiği askı nedeni; askıda değilse null. Yalnızca admin'e döner.</summary>
    public string? AccountSuspensionReason { get; set; }
}
