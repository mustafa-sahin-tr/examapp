using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;

namespace ExamApp.Api.Services.AdminUsers;

/// <summary>
/// Admin hesap aksiyonlarının (şifre sıfırlama #156, #155) ortak hedef çözme zinciri:
/// exam DB kaydı (Teacher/Student, silinmemiş) → auth-api'den Keycloak sub (servis token'ı alınamazsa 502) →
/// çağıranın kendisi mi → hedefin Keycloak'taki ETKİN rolleri (Admin / exam-service realm rolü ya da herhangi bir
/// realm-management client rolü → korumalı). Yan etkisizdir.
/// </summary>
public interface IAdminAccountTargetResolver
{
    Task<AdminAccountTargetResolution> ResolveAsync(
        AdminUserTargetType targetType, int targetId, string actorKeycloakId, CancellationToken ct = default);
}

public enum AdminAccountTargetStatus
{
    Resolved,
    /// <summary>Öğretmen/öğrenci kaydı yok veya silinmiş.</summary>
    TargetNotFound,
    /// <summary>auth-api'de ya da Keycloak'ta (404) karşılık gelen hesap yok.</summary>
    AccountNotFound,
    ForbiddenSelf,
    ForbiddenProtectedRole,
    /// <summary>auth-api / Keycloak erişilemedi veya hata verdi.</summary>
    UpstreamFailure
}

/// <param name="KeycloakId">Yalnızca <see cref="AdminAccountTargetStatus.Resolved"/> iken dolu.</param>
/// <param name="ParentManaged">
/// Issue #438: hedef öğrenci hesabı veli tarafından açıldı (<c>Student.ParentManaged</c>) — şifresini veli yönetir; admin sıfırlaması
/// kalıcı şifre üretir (çocuk kendi şifresini koyamasın). Öğretmen hedefinde her zaman false.
/// </param>
public sealed record AdminAccountTargetResolution(AdminAccountTargetStatus Status, string? KeycloakId = null, bool ParentManaged = false);
