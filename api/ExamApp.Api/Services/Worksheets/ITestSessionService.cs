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

    Task<ResponseBaseDto> SaveAnswer(SaveAnswerDto dto, UserProfileDto user);

    Task<ResponseBaseDto> EndTest(int testInstanceId, int userId);
}
