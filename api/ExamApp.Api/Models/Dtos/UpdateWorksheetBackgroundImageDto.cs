using ExamApp.Api.Services.Storage;
namespace ExamApp.Api.Models.Dtos;

public class UpdateWorksheetBackgroundImageDto : ResponseBaseDto
{
    [StorageUrl(StorageArea.WorksheetCover)]
    public string? ImageUrl { get; set; }
}