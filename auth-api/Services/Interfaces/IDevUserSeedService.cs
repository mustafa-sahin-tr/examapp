using ExamApp.Foundation.Contracts;

namespace ExamApp.Api.Services.Interfaces;

/// <summary>
/// Dev-only toplu kullanıcı oluşturma (issue #217) ve temizleme (issue #218): Keycloak kullanıcısı (rol +
/// school_id attribute) ve identity <c>User</c> satırı; register akışının yaptığı yazımları HTTP
/// login/rate-limit olmadan, idempotent uygular. YALNIZCA Development/Staging: başka ortamda
/// <see cref="ExamApp.Api.Helpers.DevSeedEnvironmentException"/>. Sözleşme: ExamApp.Foundation.Contracts.
/// </summary>
public interface IDevUserSeedService
{
    Task<DevSeedUsersResponse> SeedAsync(DevSeedUsersRequest request, CancellationToken ct = default);

    /// <summary>
    /// Seed hesaplarını Keycloak (kullanıcı adı seed alanında) ve identity (<c>IsSeedData=true</c>) tarafından
    /// kaldırır — hard delete. Kapsam istekle genişletilemez; <c>ExcludeUserIds</c> yalnızca daraltır.
    /// <c>DryRun=true</c> hiçbir şey silmez. İdempotent: kısmi hatadan sonra ikinci koşu kalanı temizler.
    /// </summary>
    Task<DevSeedCleanupResponse> CleanupAsync(DevSeedCleanupRequest request, CancellationToken ct = default);
}
