using ExamApp.Api.Models.Dtos;
using Microsoft.AspNetCore.Http;

namespace ExamApp.Api.Services.Worksheets;

/// <summary>Worksheet create/update/delete. Split out of the former god-class <c>ExamService</c>.</summary>
public interface IWorksheetAuthoringService
{
    Task<UpdateWorksheetBackgroundImageDto> UpdateWorksheetBackgroundImageAsync(int worksheetId, IFormFile file, int userId);
    Task<ExamSavedDto> CreateOrUpdateAsync(ExamDto examDto, int userId);
    Task<BulkExamResultDto> CreateBulkExamsAsync(BulkExamCreateDto bulkExamDto, int userId);
    Task<ResponseBaseDto> DeleteWorksheetAsync(int worksheetId, int userId);
}
