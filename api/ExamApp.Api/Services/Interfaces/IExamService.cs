using System;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Interfaces;

public interface IExamService
{
    // Define methods for exam-related operations here
    Task<Paged<WorksheetDto>> GetWorksheetsForStudentsAsync(ExamFilterDto dto, UserProfileDto userProfile);
    Task<Paged<WorksheetDto>> GetWorksheetsForTeacherAsync(ExamFilterDto dto, UserProfileDto userProfile);
    Task<Paged<InstanceSummaryDto>> GetCompletedTestsAsync(Student student, int pageNumber, int pageSize);

    Task<List<WorksheetDto>> GetLatestWorksheetsAsync(int pageNumber, int pageSize);

    Task<List<QuestionDto>> GetExamQuestionsAsync();

    Task<WorksheetDto?> GetWorksheetByIdAsync(int id);

    Task<List<WorksheetWithInstanceDto>> GetWorksheetAndInstancesAsync(Student student, int gradeId);

    Task<TestStartResultDto> StartTestAsync(int testId, Student student);

    Task<WorksheetInstanceDto?> GetTestInstanceQuestionsAsync(int testInstanceId, int userId);

    // Task<List<QuestionDto>> GetAllCanvasQuestions(bool includeAnswers = false, int maxId = 0);
    Task<WorksheetInstanceResultDto?> GetCanvasTestResultAsync(int testInstanceId, int userId, bool includeCorrectAnswer = false);
    Task<ResponseBaseDto> SaveAnswer(SaveAnswerDto dto, User user);
    Task<ResponseBaseDto> EndTest(int testInstanceId, int userId); Task<ExamSavedDto> CreateOrUpdateAsync(ExamDto examDto, int userId);

    Task<BulkExamResultDto> CreateBulkExamsAsync(BulkExamCreateDto bulkExamDto, int userId);

    Task<ExamAllStatisticsDto> GetGroupedStudentStatistics(int studentId);

}
