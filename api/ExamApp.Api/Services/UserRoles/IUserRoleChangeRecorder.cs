using ExamApp.Api.Data;
using ExamApp.Api.Helpers;

namespace ExamApp.Api.Services.UserRoles;

/// <summary>
/// issue #277 (madde 4): register akışlarında Keycloak rolü BAŞARIYLA atandıktan SONRA <c>UserRoleChangedEvent</c> outbox
/// satırını yazar (auth-api kendi <c>Users.Role</c>'ünü günceller; tüketici rolü Keycloak'tan yeniden okur).
/// </summary>
public interface IUserRoleChangeRecorder
{
    /// <summary>
    /// Rol gerçekten değişiyorsa (<see cref="UserRoleChangeOutbox.IsChange"/>) tek bir outbox satırı yazar ve true döner;
    /// değişmiyorsa hiçbir şey yazmaz (false).
    /// </summary>
    Task<bool> RecordIfChangedAsync(UserRoleChangeRequest request, UserRole newRole, CancellationToken ct = default);
}
