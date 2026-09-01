using System;
using ExamApp.Api.Data;
using ExamApp.Api.Models;
using ExamApp.Api.Models.Dtos;
using Microsoft.AspNetCore.Http;

namespace ExamApp.Api.Services.Interfaces;

public interface IExamService
{
    // Define methods for exam-related operations here
    Task<Paged<WorksheetDto>> GetWorksheetsForStudentsAsync(ExamFilterDto dto, StudentProfileDto userProfile);
    Task<Paged<WorksheetDto>> GetWorksheetsForTeacherAsync(ExamFilterDto dto, UserProfileDto userProfile);

    Task<List<WorksheetDto>> GetLatestWorksheetsAsync(int pageNumber, int pageSize);

    Task<List<QuestionDto>> GetExamQuestionsAsync();

    Task<WorksheetDto?> GetWorksheetByIdAsync(int id);

    Task<List<WorksheetWithInstanceDto>> GetWorksheetAndInstancesAsync(StudentProfileDto student, int gradeId);

    // GetAllCanvasQuestions stays on the concrete ExamService (admin/debug read, not routed).

    Task<ExamSavedDto> CreateOrUpdateAsync(ExamDto examDto, int userId);

    Task<BulkExamResultDto> CreateBulkExamsAsync(BulkExamCreateDto bulkExamDto, int userId);

    Task<ExamAllStatisticsDto> GetGroupedStudentStatistics(int studentId);
    Task<List<Grade>> GetGradesAsync();
    Task<ResponseBaseDto> DeleteWorksheetAsync(int worksheetId, int userId);
    Task<UpdateWorksheetBackgroundImageDto> UpdateWorksheetBackgroundImageAsync(int worksheetId, IFormFile file, int userId);
}
