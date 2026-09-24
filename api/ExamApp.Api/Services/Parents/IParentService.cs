using ExamApp.Api.Helpers;

namespace ExamApp.Api.Services.Parents;

/// <summary>Veli kaydı (issue #277 madde 3: iş kuralı controller'dan taşındı).</summary>
public interface IParentService
{
    /// <summary>
    /// Keycloak Parent rolü ATANDIKTAN sonra çağrılır: kullanıcının Parent satırını açar (varsa dokunmaz) ve rol gerçekten
    /// değişiyorsa <c>UserRoleChangedEvent</c> outbox satırını AYNI SaveChanges'te yazar. Parent.Id döner.
    /// </summary>
    Task<int> RegisterAsync(int userId, UserRoleChangeRequest roleChange, CancellationToken ct = default);

    /// <summary>Kullanıcının (silinmemiş) Parent satırı var mı.</summary>
    Task<bool> HasParentRecordAsync(int userId, CancellationToken ct = default);
}
