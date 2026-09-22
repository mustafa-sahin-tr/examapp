using System.Threading;
using System.Threading.Tasks;

namespace ExamApp.Api.Services.Teachers.Seed;

/// <summary>
/// <c>IsSeedData</c> okullar için okula bağlı öğretmen hesaplarını (Keycloak + identity + exam
/// <c>Teacher</c>) idempotent olarak üretir (issue #217). YALNIZCA Development/Staging: başka ortamda
/// <see cref="System.InvalidOperationException"/> fırlatır, hiçbir sisteme dokunmaz.
/// </summary>
public interface ITeacherSeedService
{
    Task<TeacherSeedResult> RunAsync(TeacherSeedOptions options, CancellationToken ct = default);
}
