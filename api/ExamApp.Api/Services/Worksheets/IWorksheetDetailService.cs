using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Worksheets;

/// <summary>
/// Aggregates read-only detail data for the redesigned /test/{id} screen and
/// builds practice worksheets from a student's wrong answers. No schema of its own —
/// everything is derived from existing worksheet/instance tables.
/// </summary>
public interface IWorksheetDetailService
{
    /// <param name="userId">Authenticated user id (teacher or student user) — used for ownership / access checks.</param>
    /// <param name="studentId">Student profile id when the caller is a student; otherwise null.</param>
    Task<WorksheetDetailDto?> GetWorksheetDetailAsync(int worksheetId, string role, int? studentId, int userId, CancellationToken ct = default);

    Task<WorksheetFromMistakesResultDto?> CreateWorksheetFromMistakesAsync(int instanceId, int studentId, int userId, CancellationToken ct = default);
}
