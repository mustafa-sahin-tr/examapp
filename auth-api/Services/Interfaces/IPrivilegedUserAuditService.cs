using ExamApp.Api.Models.Audit;

namespace ExamApp.Api.Services.Interfaces;

/// <summary>
/// Yetkili rol (Admin, exam-service) üyelerinin salt okunur denetimi (issue #267). Keycloak'ta ve identity DB'de hiçbir
/// şey yazmaz; rol kaldırma/devre dışı bırakma kararı insana bırakılır.
/// </summary>
public interface IPrivilegedUserAuditService
{
    Task<PrivilegedAuditReport> AuditAsync(IReadOnlyList<string> roles, CancellationToken ct = default);
}
