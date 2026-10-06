using ExamApp.Api.Models;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Worksheets;

/// <summary>
/// A student's test-taking session. Split out of the former god-class <c>ExamService</c>.
/// </summary>
public interface ITestSessionService
{
    Task<Paged<InstanceSummaryDto>> GetCompletedTestsAsync(StudentProfileDto student, int pageNumber, int pageSize);

    Task<TestStartResultDto> StartTestAsync(int testId, StudentProfileDto student);

    Task<WorksheetInstanceDto?> GetTestInstanceQuestionsAsync(int testInstanceId, int userId);

    Task<WorksheetInstanceResultDto?> GetCanvasTestResultAsync(int testInstanceId, int userId, bool includeCorrectAnswer = false);

    /// <summary>
    /// issue #367: yalnız <c>Started</c> instance'a yazar; aksi halde <c>Conflict=true</c> +
    /// <see cref="TestSessionErrorCodes.TestNotInProgress"/>, cevap ve outbox event'i yazılmaz.
    /// </summary>
    Task<TestSessionResultDto> SaveAnswer(SaveAnswerDto dto, UserProfileDto user, CancellationToken ct = default);

    /// <summary>issue #367: idempotent — zaten Completed instance için de başarı döner (EndTime değişmez).</summary>
    Task<TestSessionResultDto> EndTest(int testInstanceId, int userId, CancellationToken ct = default);
}
