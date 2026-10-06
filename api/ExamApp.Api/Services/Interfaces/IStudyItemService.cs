using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

    /// <summary>
    /// issue #365 (S3): JSON kılavuzundaki kitap sayfalarının <c>study-pages/books/{book}/page_{n}.webp</c> nesnesi var mı
    /// (sunucu tarafı StatObject). Kitap adı sıkı allowlist'ten geçmezse <see cref="StudyBookPageLookupResult.Error"/>;
    /// MinIO'ya ulaşılamazsa <see cref="StudyBookPageLookupResult.StorageUnavailable"/>.
    /// </summary>
    Task<StudyBookPageLookupResult> LookupBookPagesAsync(IReadOnlyList<StudyBookPageRefDto> pages, CancellationToken ct = default);
}

/// <summary><see cref="IStudyItemService.LookupBookPagesAsync"/> sonucu: ya <see cref="Items"/>, ya <see cref="Error"/> (400), ya 503.</summary>
public sealed class StudyBookPageLookupResult
{
    public IReadOnlyList<StudyBookPageLookupResultDto>? Items { get; init; }
    public string? Error { get; init; }
    public bool StorageUnavailable { get; init; }

    public static StudyBookPageLookupResult Ok(IReadOnlyList<StudyBookPageLookupResultDto> items) => new() { Items = items };
    public static StudyBookPageLookupResult Fail(string error) => new() { Error = error };
    public static StudyBookPageLookupResult Unavailable(string error) => new() { Error = error, StorageUnavailable = true };
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
