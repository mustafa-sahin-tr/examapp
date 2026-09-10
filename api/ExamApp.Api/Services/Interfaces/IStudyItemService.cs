using System.Collections.Generic;
using ExamApp.Api.Models.Dtos;
using Microsoft.AspNetCore.Http;

namespace ExamApp.Api.Services.Interfaces;

public interface IStudyItemService
{
    Task<Paged<StudyItemDto>> GetPagedAsync(StudyItemFilterDto filter, UserProfileDto user);
    Task<StudyItemDto?> GetByIdAsync(int id, UserProfileDto user);
    /// <summary>
    /// Yeni etkinlik oluşturur. ContentType'a göre doğrulama yapar; hata varsa <see cref="StudyItemMutationResult.Error"/> dolu döner.
    /// </summary>
    Task<StudyItemMutationResult> CreateAsync(CreateStudyItemRequestDto request, List<IFormFile> images, UserProfileDto user);
    /// <summary>
    /// Etkinliği günceller. Kayıt yoksa/sahibi değilse <see cref="StudyItemMutationResult.NotFound"/> true döner.
    /// </summary>
    Task<StudyItemMutationResult> UpdateAsync(int id, UpdateStudyItemRequestDto request, List<IFormFile> newImages, UserProfileDto user);
    Task<AttachStudyItemImageBySubTopicsResultDto> AttachImageBySubTopicsAsync(AttachStudyItemImageBySubTopicsRequestDto request, UserProfileDto user);
    Task<ResponseBaseDto> DeleteAsync(int id, UserProfileDto user);
}

/// <summary>Create/Update sonucu: ya <see cref="Item"/> dolu, ya <see cref="Error"/> dolu, ya da <see cref="NotFound"/>.</summary>
public class StudyItemMutationResult
{
    public StudyItemDto? Item { get; init; }
    public string? Error { get; init; }
    public bool NotFound { get; init; }

    public static StudyItemMutationResult Ok(StudyItemDto item) => new() { Item = item };
    public static StudyItemMutationResult Fail(string error) => new() { Error = error };
    public static StudyItemMutationResult Missing() => new() { NotFound = true };
}
