using ExamApp.Foundation.Contracts;

namespace ExamApp.Api.Services.Interfaces;

/// <summary>
/// Dev-only toplu kullanıcı oluşturma (issue #217): Keycloak kullanıcısı (rol + school_id attribute) ve
/// identity <c>User</c> satırı; register akışının yaptığı yazımları HTTP login/rate-limit olmadan,
/// idempotent uygular. YALNIZCA Development/Staging: başka ortamda
/// <see cref="ExamApp.Api.Helpers.DevSeedEnvironmentException"/>. Sözleşme: ExamApp.Foundation.Contracts.
/// </summary>
public interface IDevUserSeedService
{
    Task<DevSeedUsersResponse> SeedAsync(DevSeedUsersRequest request, CancellationToken ct = default);
}
