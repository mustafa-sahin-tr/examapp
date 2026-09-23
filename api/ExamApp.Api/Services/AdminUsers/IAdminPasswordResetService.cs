using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;

namespace ExamApp.Api.Services.AdminUsers;

/// <summary>Admin şifre sıfırlama (issue #156).</summary>
public interface IAdminPasswordResetService
{
    /// <summary>
    /// <paramref name="targetId"/> exam DB'deki Teacher.Id / Student.Id'dir (<paramref name="targetType"/>'a göre).
    /// Sıra: hedef çözümü (<see cref="IAdminAccountTargetResolver"/>) → reddedilirse Denied/NotFound audit (best-effort) →
    /// Requested audit (fail-closed, yan etkiden önce) → Keycloak geçici şifre + oturum kapatma (iptal edilemez) →
    /// sonuç audit'i (best-effort). Geçici şifre yalnızca <see cref="AdminPasswordResetStatus.Success"/>'te döner;
    /// hiçbir log/audit/exception mesajına yazılmaz.
    /// </summary>
    Task<AdminPasswordResetResult> ResetAsync(
        AdminUserTargetType targetType, int targetId, string actorKeycloakId, CancellationToken ct = default);
}

public enum AdminPasswordResetStatus
{
    Success,
    /// <summary>Öğretmen/öğrenci kaydı yok (veya silinmiş) → 404.</summary>
    TargetNotFound,
    /// <summary>auth-api'de ya da Keycloak'ta hesap yok → 404.</summary>
    AccountNotFound,
    /// <summary>Admin kendi şifresini bu uçtan sıfırlayamaz → 403.</summary>
    ForbiddenSelf,
    /// <summary>Hedef Admin / exam-service ya da realm-management client rolünde → 403.</summary>
    ForbiddenProtectedRole,
    /// <summary>auth-api veya Keycloak hatası / zaman aşımı; şifre DEĞİŞMEDİ → 502.</summary>
    UpstreamFailure,
    /// <summary>Şifre değişti ama oturumlar kapatılamadı; şifre dönmez, admin tekrar denemeli → 502.</summary>
    SessionRevokeFailed
}

public sealed record AdminPasswordResetResult(AdminPasswordResetStatus Status, string? TemporaryPassword = null)
{
    // Şifre yanlışlıkla ToString/log ile yazılmasın (record'un üretilen ToString'i tüm alanları basar).
    public override string ToString() => $"{nameof(AdminPasswordResetResult)} {{ Status = {Status} }}";
}
