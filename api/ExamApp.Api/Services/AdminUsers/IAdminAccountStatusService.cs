using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;

namespace ExamApp.Api.Services.AdminUsers;

/// <summary>Admin hesap devre dışı bırakma / etkinleştirme (issue #155).</summary>
public interface IAdminAccountStatusService
{
    /// <summary>
    /// <paramref name="targetId"/> exam DB'deki Teacher.Id / Student.Id'dir (<paramref name="targetType"/>'a göre).
    /// Sıra (#156 ile aynı): hedef çözümü (<see cref="IAdminAccountTargetResolver"/>) → reddedilirse Denied/NotFound audit
    /// (best-effort) → Requested audit (fail-closed, yan etkiden önce) → Keycloak <c>enabled</c> güncellemesi; devre dışı
    /// bırakmada ayrıca tüm oturumlar kapatılır, ardından profil önbelleği düşürülür (logout başarısız olsa da; iptal edilemez) → sonuç audit'i (best-effort).
    /// İdempotenttir: hesap zaten istenen durumdaysa da aynı adımlar çalışır ve <see cref="AdminAccountStatusChangeStatus.Success"/> döner.
    /// </summary>
    Task<AdminAccountStatusChangeResult> SetEnabledAsync(
        AdminUserTargetType targetType, int targetId, bool enabled, string actorKeycloakId, CancellationToken ct = default);
}

public enum AdminAccountStatusChangeStatus
{
    Success,
    /// <summary>Öğretmen/öğrenci kaydı yok (veya silinmiş) → 404.</summary>
    TargetNotFound,
    /// <summary>auth-api'de ya da Keycloak'ta hesap yok → 404.</summary>
    AccountNotFound,
    /// <summary>Admin kendi hesabının durumunu bu uçtan değiştiremez → 403.</summary>
    ForbiddenSelf,
    /// <summary>Hedef Admin / exam-service ya da realm-management client rolünde → 403.</summary>
    ForbiddenProtectedRole,
    /// <summary>auth-api veya Keycloak hatası / zaman aşımı; hesap durumu DEĞİŞMEDİ → 502.</summary>
    UpstreamFailure,
    /// <summary>Hesap devre dışı bırakıldı ama açık oturumlar kapatılamadı; admin tekrar denemeli (idempotent) → 502.</summary>
    SessionRevokeFailed
}

/// <param name="Enabled">Yalnızca <see cref="AdminAccountStatusChangeStatus.Success"/>'te dolu: hesabın yeni durumu.</param>
public sealed record AdminAccountStatusChangeResult(AdminAccountStatusChangeStatus Status, bool? Enabled = null);
