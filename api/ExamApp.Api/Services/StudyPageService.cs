using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services;

public class StudyPageService : IStudyPageService
{
    private readonly AppDbContext _context;
    private readonly IMinIoService _minioService;

    public StudyPageService(AppDbContext context, IMinIoService minioService)
    {
        _context = context;
        _minioService = minioService;
    }

    public async Task<Paged<StudyPageDto>> GetPagedAsync(StudyPageFilterDto filter, UserProfileDto user)
    {
        var query = _context.StudyPages
            .Include(p => p.Images)
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

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(p => p.CreateTime)
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        return new Paged<StudyPageDto>
        {
            PageNumber = filter.PageNumber,
            PageSize = filter.PageSize,
            TotalCount = totalCount,
            Items = items.Select(MapToDto).ToList()
        };
    }

    public async Task<StudyPageDto?> GetByIdAsync(int id, UserProfileDto user)
    {
        var page = await _context.StudyPages
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);

        if (page == null)
        {
            return null;
        }

        if (user.Role == UserRole.Student.ToString() && !page.IsPublished)
        {
            return null;
        }

        return MapToDto(page);
    }

    public async Task<StudyPageDto> CreateAsync(CreateStudyPageRequestDto request, List<IFormFile> images, UserProfileDto user)
    {
        var entity = new StudyPage
        {
            Title = request.Title.Trim(),
            Description = request.Description?.Trim() ?? string.Empty,
            GradeId = request.GradeId,
            SubjectId = request.SubjectId,
            TopicId = request.TopicId,
            SubTopicId = request.SubTopicId,
            IsPublished = request.IsPublished,
            CreatedByUserId = user.Id,
            CreatedByName = user.FullName ?? string.Empty,
            CreatedByRole = user.Role ?? string.Empty
        };

        _context.StudyPages.Add(entity);
        await _context.SaveChangesAsync();

        var sortOrder = 1;
        foreach (var image in images)
        {
            if (image == null || image.Length == 0)
            {
                continue;
            }

            var extension = Path.GetExtension(image.FileName) ?? string.Empty;
            var objectName = $"pages/{entity.Id}/{Guid.NewGuid()}{extension}";

            using var stream = image.OpenReadStream();
            var url = await _minioService.UploadFileAsync(stream, objectName, "study-pages", image.ContentType);

            _context.StudyPageImages.Add(new StudyPageImage
            {
                StudyPageId = entity.Id,
                ImageUrl = url,
                SortOrder = sortOrder,
                FileName = image.FileName
            });

            sortOrder += 1;
        }

        // Handle MinIO images from JSON
        if (!string.IsNullOrEmpty(request.MinioImages))
        {
            try
            {
                // Debug logging
                Console.WriteLine($"MinioImages JSON: {request.MinioImages}");

                using var jsonDoc = JsonDocument.Parse(request.MinioImages);
                var jsonArray = jsonDoc.RootElement;

                Console.WriteLine($"Parsed {jsonArray.GetArrayLength()} MinIO images");

                foreach (var item in jsonArray.EnumerateArray())
                {
                    var bookName = item.GetProperty("bookName").GetString() ?? string.Empty;
                    var pageNumber = item.GetProperty("pageNumber").GetInt32();
                    var minioUrl = item.GetProperty("minioUrl").GetString() ?? string.Empty;

                    Console.WriteLine($"MinIO Image - Book: {bookName}, Page: {pageNumber}, URL: {minioUrl}");

                    _context.StudyPageImages.Add(new StudyPageImage
                    {
                        StudyPageId = entity.Id,
                        ImageUrl = minioUrl,
                        SortOrder = sortOrder,
                        FileName = $"{bookName}/page_{pageNumber}.webp"
                    });

                    sortOrder += 1;
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"JSON parsing error: {ex.Message}");
                // Log error or handle invalid JSON gracefully
            }
        }

        await _context.SaveChangesAsync();

        return await GetByIdAsync(entity.Id, user) ?? MapToDto(entity);
    }

    public async Task<StudyPageDto?> UpdateAsync(int id, UpdateStudyPageRequestDto request, List<IFormFile> newImages, UserProfileDto user)
    {
        var page = await _context.StudyPages
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);

        if (page == null)
        {
            return null;
        }

        if (page.CreatedByUserId != user.Id && user.Role != "Service")
        {
            return null;
        }

        page.Title = request.Title.Trim();
        page.Description = request.Description?.Trim() ?? string.Empty;
        page.GradeId = request.GradeId;
        page.SubjectId = request.SubjectId;
        page.TopicId = request.TopicId;
        page.SubTopicId = request.SubTopicId;
        page.IsPublished = request.IsPublished;

        if (request.RemovedImageIds.Count > 0)
        {
            var toRemove = page.Images.Where(i => request.RemovedImageIds.Contains(i.Id)).ToList();
            _context.StudyPageImages.RemoveRange(toRemove);
        }

        var sortOrder = page.Images.Count == 0 ? 1 : page.Images.Max(i => i.SortOrder) + 1;
        foreach (var image in newImages)
        {
            if (image == null || image.Length == 0)
            {
                continue;
            }

            var extension = Path.GetExtension(image.FileName) ?? string.Empty;
            var objectName = $"pages/{page.Id}/{Guid.NewGuid()}{extension}";

            using var stream = image.OpenReadStream();
            var url = await _minioService.UploadFileAsync(stream, objectName, "study-pages", image.ContentType);

            _context.StudyPageImages.Add(new StudyPageImage
            {
                StudyPageId = page.Id,
                ImageUrl = url,
                SortOrder = sortOrder,
                FileName = image.FileName
            });

            sortOrder += 1;
        }

        // Handle MinIO images from JSON
        if (!string.IsNullOrEmpty(request.MinioImages))
        {
            try
            {
                // Debug logging
                Console.WriteLine($"UPDATE MinioImages JSON: {request.MinioImages}");

                using var jsonDoc = JsonDocument.Parse(request.MinioImages);
                var jsonArray = jsonDoc.RootElement;

                Console.WriteLine($"UPDATE Parsed {jsonArray.GetArrayLength()} MinIO images");

                foreach (var item in jsonArray.EnumerateArray())
                {
                    var bookName = item.GetProperty("bookName").GetString() ?? string.Empty;
                    var pageNumber = item.GetProperty("pageNumber").GetInt32();
                    var minioUrl = item.GetProperty("minioUrl").GetString() ?? string.Empty;

                    Console.WriteLine($"UPDATE MinIO Image - Book: {bookName}, Page: {pageNumber}, URL: {minioUrl}");

                    _context.StudyPageImages.Add(new StudyPageImage
                    {
                        StudyPageId = page.Id,
                        ImageUrl = minioUrl,
                        SortOrder = sortOrder,
                        FileName = $"{bookName}/page_{pageNumber}.webp"
                    });

                    sortOrder += 1;
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"UPDATE JSON parsing error: {ex.Message}");
                // Log error or handle invalid JSON gracefully
            }
        }

        await _context.SaveChangesAsync();

        return await GetByIdAsync(page.Id, user);
    }

    public async Task<AttachStudyPageImageBySubTopicsResultDto> AttachImageBySubTopicsAsync(AttachStudyPageImageBySubTopicsRequestDto request, UserProfileDto user)
    {
        var result = new AttachStudyPageImageBySubTopicsResultDto();

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

        var existingPages = await _context.StudyPages
            .Include(p => p.Images)
            .Where(p => !p.IsDeleted && p.SubTopicId.HasValue && subTopicIds.Contains(p.SubTopicId.Value))
            .ToListAsync();

        var pageBySubTopicId = existingPages
            .GroupBy(p => p.SubTopicId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.CreateTime).First());

        var processedPages = new List<StudyPage>();

        foreach (var subTopicId in subTopicIds)
        {
            if (!pageBySubTopicId.TryGetValue(subTopicId, out var page))
            {
                var subTopic = subTopicById[subTopicId];

                page = new StudyPage
                {
                    Title = subTopic.Name.Trim(),
                    Description = string.Empty,
                    GradeId = subTopic.Topic?.GradeId,
                    SubjectId = subTopic.Topic?.SubjectId,
                    TopicId = subTopic.TopicId,
                    SubTopicId = subTopic.Id,
                    IsPublished = true,
                    CreatedByUserId = user.Id,
                    CreatedByName = user.FullName ?? string.Empty,
                    CreatedByRole = user.Role ?? string.Empty
                };

                _context.StudyPages.Add(page);
                pageBySubTopicId[subTopicId] = page;
                result.CreatedStudyPageCount += 1;
            }
            else
            {
                result.UpdatedStudyPageCount += 1;
            }

            var nextSortOrder = page.Images
                .Where(i => !i.IsDeleted)
                .Select(i => i.SortOrder)
                .DefaultIfEmpty(0)
                .Max() + 1;

            page.Images.Add(new StudyPageImage
            {
                ImageUrl = imageUrl,
                SortOrder = nextSortOrder,
                FileName = GetFileNameFromUrl(imageUrl)
            });

            processedPages.Add(page);
        }

        await _context.SaveChangesAsync();

        result.StudyPages = processedPages
            .GroupBy(p => p.Id)
            .Select(g => MapToDto(g.First()))
            .ToList();
        result.Success = true;
        result.Message = "Image basariyla eklendi.";

        return result;
    }

    public async Task<ResponseBaseDto> DeleteAsync(int id, UserProfileDto user)
    {
        var page = await _context.StudyPages
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);

        if (page == null)
        {
            return new ResponseBaseDto { Success = false, Message = "Calisma sayfasi bulunamadi." };
        }

        if (page.CreatedByUserId != user.Id && user.Role != "Service")
        {
            return new ResponseBaseDto { Success = false, Message = "Bu sayfayi silme yetkiniz yok." };
        }

        _context.StudyPages.Remove(page);
        await _context.SaveChangesAsync();

        return new ResponseBaseDto { Success = true, ObjectId = page.Id };
    }

    private static StudyPageDto MapToDto(StudyPage page)
    {
        var images = page.Images
            .Where(i => !i.IsDeleted)
            .OrderBy(i => i.SortOrder)
            .Select(i => new StudyPageImageDto
            {
                Id = i.Id,
                ImageUrl = i.ImageUrl,
                SortOrder = i.SortOrder,
                FileName = i.FileName
            })
            .ToList();

        return new StudyPageDto
        {
            Id = page.Id,
            Title = page.Title,
            Description = page.Description,
            GradeId = page.GradeId,
            SubjectId = page.SubjectId,
            TopicId = page.TopicId,
            SubTopicId = page.SubTopicId,
            IsPublished = page.IsPublished,
            CreatedByUserId = page.CreatedByUserId,
            CreatedByName = page.CreatedByName,
            CreatedByRole = page.CreatedByRole,
            CreateTime = page.CreateTime,
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
