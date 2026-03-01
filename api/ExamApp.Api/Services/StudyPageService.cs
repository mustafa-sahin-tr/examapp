using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        if (page.CreatedByUserId != user.Id)
        {
            return null;
        }

        page.Title = request.Title.Trim();
        page.Description = request.Description?.Trim() ?? string.Empty;
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

        await _context.SaveChangesAsync();

        return await GetByIdAsync(page.Id, user);
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

        if (page.CreatedByUserId != user.Id)
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
}
