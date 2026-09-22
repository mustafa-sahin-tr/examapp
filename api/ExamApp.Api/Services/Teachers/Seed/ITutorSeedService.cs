using System.Threading;
using System.Threading.Tasks;

namespace ExamApp.Api.Services.Teachers.Seed;

/// <summary>
/// Bağımsız öğretmen (tutor) test hesapları (issue #218): il + branş bazında seed okul öğretmeni sayısının yarısı
/// kadar <c>IsIndependentTutor=true, SchoolId=null, ApprovalStatus=Approved</c> (opsiyonel Pending oranı) hesap;
/// Keycloak + identity auth-api dev ucu üzerinden (<c>seed-teachers</c> ile aynı yazım yolu), exam <c>Teacher</c>
/// + <c>TeacherSubject</c> + tutor profili varsayılanları burada. İdempotent. YALNIZCA Development/Staging.
/// </summary>
public interface ITutorSeedService
{
    Task<TutorSeedResult> RunAsync(TutorSeedOptions options, CancellationToken ct = default);
}
