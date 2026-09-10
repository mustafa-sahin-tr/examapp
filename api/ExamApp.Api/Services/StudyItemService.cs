using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Api.Services;

public class StudyItemService : IStudyItemService
{
    private readonly AppDbContext _context;
    private readonly IMinIoService _minioService;
    private readonly ILogger<StudyItemService> _logger;

    public StudyItemService(AppDbContext context, IMinIoService minioService, ILogger<StudyItemService>? logger = null)
    {
        _context = context;
        _minioService = minioService;
        _logger = logger ?? NullLogger<StudyItemService>.Instance;
    }

    public async Task<Paged<StudyItemDto>> GetPagedAsync(StudyItemFilterDto filter, UserProfileDto user)
    {
        var query = _context.StudyItems
            .Include(p => p.Images)
            .Include(p => p.Book)
            .Include(p => p.BookTest)
            .Where(p => !p.IsDeleted);

        if (user.Role == UserRole.Student.ToString())
        {
            query = query.Where(p => p.IsPublished);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToLowerInvariant();
            query = query.Where(p => p.Title.ToLower().Contains(search) || p.Description.ToLower().Contains(search));
        }

        if (filter.SubjectId.HasValue && filter.SubjectId.Value > 0)
        {
            query = query.Where(p => p.SubjectId == filter.SubjectId.Value);
        }

        if (filter.TopicId.HasValue && filter.TopicId.Value > 0)
        {
            query = query.Where(p => p.TopicId == filter.TopicId.Value);
        }

        if (filter.SubTopicId.HasValue && filter.SubTopicId.Value > 0)
        {
            query = query.Where(p => p.SubTopicId == filter.SubTopicId.Value);
        }

        if (filter.ContentType.HasValue)
        {
            query = query.Where(p => p.ContentType == filter.ContentType.Value);
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(p => p.CreateTime)
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        return new Paged<StudyItemDto>
        {
            PageNumber = filter.PageNumber,
            PageSize = filter.PageSize,
            TotalCount = totalCount,
            Items = items.Select(MapToDto).ToList()
        };
    }

    public async Task<StudyItemDto?> GetByIdAsync(int id, UserProfileDto user)
    {
        var item = await _context.StudyItems
            .Include(p => p.Images)
            .Include(p => p.Book)
            .Include(p => p.BookTest)
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);

        if (item == null)
        {
            return null;
        }

        if (user.Role == UserRole.Student.ToString() && !item.IsPublished)
        {
            return null;
        }

        return MapToDto(item);
    }

    public async Task<StudyItemMutationResult> CreateAsync(CreateStudyItemRequestDto request, List<IFormFile> images, UserProfileDto user)
    {
        var validationError = ValidateContent(
            request.ContentType, request.Url,
            request.BookId, request.NewBookName, request.BookTestId, request.NewBookTestName,
            request.StartPage, request.EndPage,
            hasAnyImage: HasAnyImage(images, request.MinioImages));

        if (validationError != null)
        {
            return StudyItemMutationResult.Fail(validationError);
        }

        var entity = new StudyItem
        {
            Title = request.Title.Trim(),
            Description = request.Description?.Trim() ?? string.Empty,
            GradeId = request.GradeId,
            SubjectId = request.SubjectId,
            TopicId = request.TopicId,
            SubTopicId = request.SubTopicId,
            IsPublished = request.IsPublished,
            ContentType = request.ContentType,
            CreatedByUserId = user.Id,
            CreatedByName = user.FullName ?? string.Empty,
            CreatedByRole = user.Role ?? string.Empty
        };

        var contentError = await ApplyTypedContentAsync(
            entity, request.ContentType, request.Url, request.Platform,
            request.BookId, request.NewBookName, request.BookTestId, request.NewBookTestName,
            request.StartPage, request.EndPage);

        if (contentError != null)
        {
            return StudyItemMutationResult.Fail(contentError);
        }

        _context.StudyItems.Add(entity);
        await _context.SaveChangesAsync();

        if (request.ContentType == StudyItemContentType.Image)
        {
            await AddImagesAsync(entity.Id, images, request.MinioImages, startSortOrder: 1, "create");
            await _context.SaveChangesAsync();
        }

        var dto = await GetByIdAsync(entity.Id, user) ?? MapToDto(entity);
        return StudyItemMutationResult.Ok(dto);
    }

    public async Task<StudyItemMutationResult> UpdateAsync(int id, UpdateStudyItemRequestDto request, List<IFormFile> newImages, UserProfileDto user)
    {
        var item = await _context.StudyItems
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);

        if (item == null)
        {
            return StudyItemMutationResult.Missing();
        }

        if (item.CreatedByUserId != user.Id && user.Role != "Service")
        {
            return StudyItemMutationResult.Missing();
        }

        // Image tipinde: mevcut (silinmeyen) resimler + yeni gelenler toplamda en az bir olmalı.
        var remainingImageCount = item.Images.Count(i => !i.IsDeleted && !request.RemovedImageIds.Contains(i.Id));
        var hasAnyImage = remainingImageCount > 0 || HasAnyImage(newImages, request.MinioImages);

        var validationError = ValidateContent(
            request.ContentType, request.Url,
            request.BookId, request.NewBookName, request.BookTestId, request.NewBookTestName,
            request.StartPage, request.EndPage,
            hasAnyImage);

        if (validationError != null)
        {
            return StudyItemMutationResult.Fail(validationError);
        }

        item.Title = request.Title.Trim();
        item.Description = request.Description?.Trim() ?? string.Empty;
        item.GradeId = request.GradeId;
        item.SubjectId = request.SubjectId;
        item.TopicId = request.TopicId;
        item.SubTopicId = request.SubTopicId;
        item.IsPublished = request.IsPublished;
        item.ContentType = request.ContentType;

        var contentError = await ApplyTypedContentAsync(
            item, request.ContentType, request.Url, request.Platform,
            request.BookId, request.NewBookName, request.BookTestId, request.NewBookTestName,
            request.StartPage, request.EndPage);

        if (contentError != null)
        {
            return StudyItemMutationResult.Fail(contentError);
        }

        if (request.RemovedImageIds.Count > 0)
        {
            var toRemove = item.Images.Where(i => request.RemovedImageIds.Contains(i.Id)).ToList();
            _context.StudyItemImages.RemoveRange(toRemove);
        }

        if (request.ContentType == StudyItemContentType.Image)
        {
            var sortOrder = item.Images.Count == 0 ? 1 : item.Images.Max(i => i.SortOrder) + 1;
            await AddImagesAsync(item.Id, newImages, request.MinioImages, sortOrder, "update");
        }

        await _context.SaveChangesAsync();

        var dto = await GetByIdAsync(item.Id, user);
        return dto == null ? StudyItemMutationResult.Missing() : StudyItemMutationResult.Ok(dto);
    }

    public async Task<AttachStudyItemImageBySubTopicsResultDto> AttachImageBySubTopicsAsync(AttachStudyItemImageBySubTopicsRequestDto request, UserProfileDto user)
    {
        var result = new AttachStudyItemImageBySubTopicsResultDto();

        var imageUrl = request.ImageUrl?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            result.Success = false;
            result.Message = "imageUrl zorunludur.";
            return result;
        }

        var subTopicIds = request.SubTopicIds
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        if (subTopicIds.Count == 0)
        {
            result.Success = false;
            result.Message = "Gecerli subTopicIds bulunamadi.";
            return result;
        }

        var subTopics = await _context.SubTopics
            .Include(st => st.Topic)
            .Where(st => subTopicIds.Contains(st.Id) && !st.IsDeleted)
            .ToListAsync();

        var subTopicById = subTopics.ToDictionary(st => st.Id, st => st);
        var missingSubTopicIds = subTopicIds.Where(id => !subTopicById.ContainsKey(id)).ToList();
        if (missingSubTopicIds.Count > 0)
        {
            result.Success = false;
            result.Message = "Bazi subTopic kayitlari bulunamadi.";
            result.MissingSubTopicIds = missingSubTopicIds;
            return result;
        }

        var existingItems = await _context.StudyItems
            .Include(p => p.Images)
            .Where(p => !p.IsDeleted && p.SubTopicId.HasValue && subTopicIds.Contains(p.SubTopicId.Value))
            .ToListAsync();

        var itemBySubTopicId = existingItems
            .GroupBy(p => p.SubTopicId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.CreateTime).First());

        var processedItems = new List<StudyItem>();

        foreach (var subTopicId in subTopicIds)
        {
            if (!itemBySubTopicId.TryGetValue(subTopicId, out var item))
            {
                var subTopic = subTopicById[subTopicId];

                item = new StudyItem
                {
                    Title = subTopic.Name.Trim(),
                    Description = string.Empty,
                    GradeId = subTopic.Topic?.GradeId,
                    SubjectId = subTopic.Topic?.SubjectId,
                    TopicId = subTopic.TopicId,
                    SubTopicId = subTopic.Id,
                    IsPublished = true,
                    ContentType = StudyItemContentType.Image,
                    CreatedByUserId = user.Id,
                    CreatedByName = user.FullName ?? string.Empty,
                    CreatedByRole = user.Role ?? string.Empty
                };

                _context.StudyItems.Add(item);
                itemBySubTopicId[subTopicId] = item;
                result.CreatedStudyItemCount += 1;
            }
            else
            {
                result.UpdatedStudyItemCount += 1;
            }

            var nextSortOrder = item.Images
                .Where(i => !i.IsDeleted)
                .Select(i => i.SortOrder)
                .DefaultIfEmpty(0)
                .Max() + 1;

            item.Images.Add(new StudyItemImage
            {
                ImageUrl = imageUrl,
                SortOrder = nextSortOrder,
                FileName = GetFileNameFromUrl(imageUrl)
            });

            processedItems.Add(item);
        }

        await _context.SaveChangesAsync();

        result.StudyItems = processedItems
            .GroupBy(p => p.Id)
            .Select(g => MapToDto(g.First()))
            .ToList();
        result.Success = true;
        result.Message = "Image basariyla eklendi.";

        return result;
    }

    public async Task<ResponseBaseDto> DeleteAsync(int id, UserProfileDto user)
    {
        var item = await _context.StudyItems
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);

        if (item == null)
        {
            return new ResponseBaseDto { Success = false, Message = "Calisma etkinligi bulunamadi." };
        }

        if (item.CreatedByUserId != user.Id && user.Role != "Service")
        {
            return new ResponseBaseDto { Success = false, Message = "Bu etkinligi silme yetkiniz yok." };
        }

        _context.StudyItems.Remove(item);
        await _context.SaveChangesAsync();

        return new ResponseBaseDto { Success = true, ObjectId = item.Id };
    }

    // ---- ContentType doğrulama ----

    /// <summary>
    /// ContentType'a göre alan doğrulaması. Hata mesajı döner; null ise geçerli.
    /// </summary>
    private static string? ValidateContent(
        StudyItemContentType contentType, string? url,
        int? bookId, string? newBookName, int? bookTestId, string? newBookTestName,
        int? startPage, int? endPage, bool hasAnyImage)
    {
        switch (contentType)
        {
            case StudyItemContentType.Image:
                return hasAnyImage ? null : "En az bir resim eklemelisiniz.";

            case StudyItemContentType.Link:
                if (string.IsNullOrWhiteSpace(url))
                {
                    return "Link tipi icin url zorunludur.";
                }
                if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    return "Gecerli bir http/https URL giriniz.";
                }
                return null;

            case StudyItemContentType.BookPageRange:
                if ((bookId is null || bookId <= 0) && string.IsNullOrWhiteSpace(newBookName))
                {
                    return "Kitap secilmedi (bookId veya newBookName zorunlu).";
                }
                if ((bookTestId is null || bookTestId <= 0) && string.IsNullOrWhiteSpace(newBookTestName))
                {
                    return "Kitap testi secilmedi (bookTestId veya newBookTestName zorunlu).";
                }
                if (startPage is null || endPage is null)
                {
                    return "startPage ve endPage zorunludur.";
                }
                if (startPage <= 0 || endPage <= 0)
                {
                    return "Sayfa numaralari pozitif olmalidir.";
                }
                if (endPage < startPage)
                {
                    return "endPage, startPage degerinden kucuk olamaz.";
                }
                return null;

            default:
                return "Gecersiz contentType.";
        }
    }

    /// <summary>
    /// Tipe özel alanları entity'ye yazar; diğer tiplerin alanlarını temizler.
    /// BookPageRange için Book/BookTest find-or-create yapar (WorksheetAuthoringService.CreateOrUpdateAsync deseni).
    /// </summary>
    private async Task<string?> ApplyTypedContentAsync(
        StudyItem entity, StudyItemContentType contentType, string? url, StudyItemLinkPlatform? platform,
        int? bookId, string? newBookName, int? bookTestId, string? newBookTestName,
        int? startPage, int? endPage)
    {
        // Önce hepsini sıfırla; sadece seçili tipin alanları dolu kalır.
        entity.Url = null;
        entity.Platform = null;
        entity.BookId = null;
        entity.BookTestId = null;
        entity.StartPage = null;
        entity.EndPage = null;

        switch (contentType)
        {
            case StudyItemContentType.Link:
                entity.Url = url!.Trim();
                entity.Platform = platform ?? StudyItemLinkPlatform.Other;
                return null;

            case StudyItemContentType.BookPageRange:
            {
                var resolved = await ResolveBookAndTestAsync(bookId, newBookName, bookTestId, newBookTestName);
                if (resolved.Error != null)
                {
                    return resolved.Error;
                }

                entity.BookId = resolved.BookId;
                entity.BookTestId = resolved.BookTestId;
                entity.StartPage = startPage;
                entity.EndPage = endPage;
                return null;
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// Kitap ve testi ID veya isimle bulur, yoksa oluşturur. Aynı isimli kayıt varsa yeniden oluşturmaz.
    /// </summary>
    private async Task<(int? BookId, int? BookTestId, string? Error)> ResolveBookAndTestAsync(
        int? bookId, string? newBookName, int? bookTestId, string? newBookTestName)
    {
        Book? book;

        if (bookId is > 0)
        {
            book = await _context.Books
                .Include(b => b.BookTests)
                .FirstOrDefaultAsync(b => b.Id == bookId.Value);

            if (book == null)
            {
                return (null, null, "Kitap bulunamadi.");
            }
        }
        else
        {
            var name = newBookName!.Trim();
            book = await _context.Books
                .Include(b => b.BookTests)
                .FirstOrDefaultAsync(b => b.Name == name);

            if (book == null)
            {
                book = new Book { Name = name };
                _context.Books.Add(book);
            }
        }

        BookTest? bookTest;

        if (bookTestId is > 0)
        {
            bookTest = book.BookTests.FirstOrDefault(bt => bt.Id == bookTestId.Value);
            if (bookTest == null)
            {
                return (null, null, "Kitap testi bulunamadi veya secilen kitaba ait degil.");
            }
        }
        else
        {
            var testName = newBookTestName!.Trim();
            bookTest = book.BookTests.FirstOrDefault(bt => bt.Name == testName);
            if (bookTest == null)
            {
                bookTest = new BookTest { Name = testName, Book = book };
                book.BookTests.Add(bookTest);
            }
        }

        // Yeni oluşturulan Book/BookTest için ID'leri almak üzere kaydet.
        if (book.Id == 0 || bookTest.Id == 0)
        {
            await _context.SaveChangesAsync();
        }

        return (book.Id, bookTest.Id, null);
    }

    // ---- Resim yardımcıları ----

    private static bool HasAnyImage(List<IFormFile>? images, string? minioImages)
    {
        var hasFiles = images != null && images.Any(i => i != null && i.Length > 0);
        return hasFiles || !string.IsNullOrWhiteSpace(minioImages);
    }

    private async Task AddImagesAsync(int studyItemId, List<IFormFile>? images, string? minioImages, int startSortOrder, string operation)
    {
        var sortOrder = startSortOrder;

        foreach (var image in images ?? new List<IFormFile>())
        {
            if (image == null || image.Length == 0)
            {
                continue;
            }

            var extension = Path.GetExtension(image.FileName) ?? string.Empty;
            var objectName = $"pages/{studyItemId}/{Guid.NewGuid()}{extension}";

            using var stream = image.OpenReadStream();
            var url = await _minioService.UploadFileAsync(stream, objectName, "study-pages", image.ContentType);

            _context.StudyItemImages.Add(new StudyItemImage
            {
                StudyItemId = studyItemId,
                ImageUrl = url,
                SortOrder = sortOrder,
                FileName = image.FileName
            });

            sortOrder += 1;
        }

        if (string.IsNullOrEmpty(minioImages))
        {
            return;
        }

        try
        {
            using var jsonDoc = JsonDocument.Parse(minioImages);
            var jsonArray = jsonDoc.RootElement;

            foreach (var item in jsonArray.EnumerateArray())
            {
                var bookName = item.GetProperty("bookName").GetString() ?? string.Empty;
                var pageNumber = item.GetProperty("pageNumber").GetInt32();
                var minioUrl = item.GetProperty("minioUrl").GetString() ?? string.Empty;

                _context.StudyItemImages.Add(new StudyItemImage
                {
                    StudyItemId = studyItemId,
                    ImageUrl = minioUrl,
                    SortOrder = sortOrder,
                    FileName = $"{bookName}/page_{pageNumber}.webp"
                });

                sortOrder += 1;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid MinioImages JSON on study item {Operation}; skipping image import", operation);
        }
    }

    private static StudyItemDto MapToDto(StudyItem item)
    {
        var images = item.Images
            .Where(i => !i.IsDeleted)
            .OrderBy(i => i.SortOrder)
            .Select(i => new StudyItemImageDto
            {
                Id = i.Id,
                ImageUrl = i.ImageUrl,
                SortOrder = i.SortOrder,
                FileName = i.FileName
            })
            .ToList();

        return new StudyItemDto
        {
            Id = item.Id,
            Title = item.Title,
            Description = item.Description,
            GradeId = item.GradeId,
            SubjectId = item.SubjectId,
            TopicId = item.TopicId,
            SubTopicId = item.SubTopicId,
            IsPublished = item.IsPublished,
            CreatedByUserId = item.CreatedByUserId,
            CreatedByName = item.CreatedByName,
            CreatedByRole = item.CreatedByRole,
            CreateTime = item.CreateTime,
            ContentType = item.ContentType,
            Url = item.Url,
            Platform = item.Platform,
            BookId = item.BookId,
            BookName = item.Book?.Name,
            BookTestId = item.BookTestId,
            BookTestName = item.BookTest?.Name,
            StartPage = item.StartPage,
            EndPage = item.EndPage,
            ImageCount = images.Count,
            CoverImageUrl = images.FirstOrDefault()?.ImageUrl,
            Images = images
        };
    }

    private static string GetFileNameFromUrl(string imageUrl)
    {
        if (Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
        {
            var fileName = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return fileName;
            }
        }

        return "external-image";
    }
}

public class MinioImageInfo
{
    public string BookName { get; set; } = string.Empty;
    public int PageNumber { get; set; }
    public string MinioUrl { get; set; } = string.Empty;
}
