using System.Threading;
using System.Threading.Tasks;

namespace ExamApp.Api.Services.Schools.Seed;

/// <summary>
/// MEB türevi okul listesini <c>School</c> tablosuna idempotent olarak içe aktarır (issue #216).
/// YALNIZCA Development/Staging: başka ortamda <see cref="System.InvalidOperationException"/> fırlatır,
/// kaynağa dokunmaz.
/// </summary>
public interface ISchoolSeedService
{
    Task<SchoolSeedResult> RunAsync(SchoolSeedOptions options, CancellationToken ct = default);
}
