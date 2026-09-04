using ExamApp.Api.Models.Dtos;
using Microsoft.AspNetCore.Http;

namespace ExamApp.Api.Services.Worksheets;

/// <summary>Worksheet create/update/delete. Split out of the former god-class <c>ExamService</c>.</summary>
public interface IWorksheetAuthoringService
{
    Task<UpdateWorksheetBackgroundImageDto> UpdateWorksheetBackgroundImageAsync(int worksheetId, IFormFile file, int userId, bool isAdmin);
    Task<ExamSavedDto> CreateOrUpdateAsync(ExamDto examDto, int userId, bool isAdmin);
    Task<BulkExamResultDto> CreateBulkExamsAsync(BulkExamCreateDto bulkExamDto, int userId, bool isAdmin);
    Task<ResponseBaseDto> DeleteWorksheetAsync(int worksheetId, int userId, bool isAdmin);
    Task<ResponseBaseDto> UpdateVisibilityAsync(int worksheetId, UpdateWorksheetVisibilityDto dto, int userId, bool isAdmin);
}
