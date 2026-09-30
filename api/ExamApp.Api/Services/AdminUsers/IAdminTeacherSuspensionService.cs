using System;
using System.Threading;
using System.Threading.Tasks;

namespace ExamApp.Api.Services.AdminUsers;

/// <summary>
/// issue #289: admin'in öğretmen HESAP ONAYINI askıya alması / askıyı kaldırması. Yalnızca exam DB'deki öğretmen yetkisini
/// (<c>Teachers.AccountApprovedAt</c>) kapatır/açar — Keycloak hesabına (<c>Enabled</c>, #155) ve oturumlara DOKUNMAZ.
/// Askıdaki öğretmen <c>IApprovedTeacherGuard</c>'dan <c>Suspended</c> alır (policy'de 403 <c>TeacherNotApproved</c>,
/// Hangfire dashboard'u reddedilir).
/// </summary>
public interface IAdminTeacherSuspensionService
{
    /// <summary>
    /// <paramref name="teacherId"/> exam DB'deki Teacher.Id'dir. Sıra: neden doğrulaması (trim, 1-500) → hedef okuma
    /// (yok → NotFound audit) → durum kontrolü (askıda / hesap onaysız → Conflict audit, yan etki yok) → Requested audit
    /// (fail-closed, yazımdan önce) → koşullu UPDATE (<c>AccountApprovedAt != null &amp;&amp; AccountSuspendedAt == null</c>;
    /// 0 satır → <see cref="AdminTeacherSuspensionStatus.Conflict"/>) → sonuç audit'i. Neden audit'e YAZILMAZ.
    /// issue #298: <paramref name="actorUserId"/> admin'in exam user id'si — otomatik reddedilen randevuların
    /// <c>UpdateUserId</c> alanına yazılır (0 = bilinmiyor).
    /// </summary>
    Task<AdminTeacherSuspensionResult> SuspendAsync(int teacherId, string? reason, string actorKeycloakId, int actorUserId = 0,
        CancellationToken ct = default);

    /// <summary>
    /// Askıyı kaldırır: yalnızca <c>AccountSuspendedAt != null</c> için; <c>AccountApprovedAt = now</c>, askı alanları null.
    /// Aynı audit sırası (NotFound / Conflict / Requested → Succeeded | Conflict).
    /// <para>
    /// BİLİNÇLİ KARAR: askıya alma <c>AccountApprovedAt</c>'i null'a çektiği için ilk hesap onayı tarihi (#287) kaybolur;
    /// geri açma onu geri getirmez, askının kalktığı anı (<c>now</c>) yazar. Böylece <c>AccountApprovedAt</c> askı sonrası
    /// "hesabın en son açıldığı an" anlamını taşır. İlk onay anı gerekirse admin karar audit'inden
    /// (<c>AdminUserActionLogs</c>, <c>TeacherApproved</c>) okunur.
    /// </para>
    /// </summary>
    Task<AdminTeacherSuspensionResult> UnsuspendAsync(int teacherId, string actorKeycloakId, CancellationToken ct = default);
}

public enum AdminTeacherSuspensionStatus
{
    Success,
    /// <summary>Neden boş/yalnızca boşluk → 400.</summary>
    ReasonRequired,
    /// <summary>Neden (trim sonrası) 500 karakterden uzun → 400.</summary>
    ReasonTooLong,
    /// <summary>Öğretmen kaydı yok (veya silinmiş) → 404.</summary>
    TargetNotFound,
    /// <summary>Askıya alma: öğretmen hesabı hiç onaylanmamış (AccountApprovedAt null, askıda değil) → 409.</summary>
    AccountNotApproved,
    /// <summary>Askıya alma: zaten askıda → 409.</summary>
    AlreadySuspended,
    /// <summary>Askıyı kaldırma: öğretmen askıda değil → 409.</summary>
    NotSuspended,
    /// <summary>Okuma ile koşullu yazma arasında öğretmenin durumu başka bir istekle değişti; değişiklik yapılmadı → 409.</summary>
    Conflict
}

public sealed record AdminTeacherSuspensionResult(
    AdminTeacherSuspensionStatus Status, DateTime? AccountApprovedAt = null, DateTime? AccountSuspendedAt = null);
