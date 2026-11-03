using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Questions;

public interface IQuestionSourceService
{
    Task<Paged<QuestionSourceItemDto>> ListAsync(string? rightsHolder, QuestionSourceType? sourceType, int page, int pageSize, CancellationToken ct = default);
    Task<int> AssignAsync(QuestionSourceAssignDto request, QuestionSourceType sourceType, CancellationToken ct = default);
    Task<int> SetPublishedAsync(QuestionSourcePublishDto request, CancellationToken ct = default);
}

/// <summary>Issue #690: sorunun kaynağı ve lisansı (yalnız admin).</summary>
public sealed class QuestionSourceService : IQuestionSourceService
{
    private const int MaxPageSize = 100;

    private readonly AppDbContext _context;

    public QuestionSourceService(AppDbContext context) => _context = context;

    public async Task<Paged<QuestionSourceItemDto>> ListAsync(
        string? rightsHolder, QuestionSourceType? sourceType, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = _context.Questions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(rightsHolder))
            query = query.Where(q => q.RightsHolder == rightsHolder);
        if (sourceType.HasValue)
            query = query.Where(q => q.SourceType == sourceType.Value);

        var totalCount = await query.CountAsync(ct);
        var rows = await query
            .OrderBy(q => q.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(q => new { q.Id, q.Text, q.SourceType, q.RightsHolder, q.LicenseReference, q.LicenseEndsAt, q.IsUnpublished })
            .ToListAsync(ct);

        return new Paged<QuestionSourceItemDto>
        {
            PageNumber = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            Items = rows.Select(q => new QuestionSourceItemDto
            {
                QuestionId = q.Id,
                Text = q.Text,
                SourceType = q.SourceType.ToString(),
                RightsHolder = q.RightsHolder,
                LicenseReference = q.LicenseReference,
                LicenseEndsAt = q.LicenseEndsAt,
                IsUnpublished = q.IsUnpublished
            }).ToList()
        };
    }

    public async Task<int> AssignAsync(QuestionSourceAssignDto request, QuestionSourceType sourceType, CancellationToken ct = default)
    {
        var questions = await _context.Questions.Where(q => request.QuestionIds.Contains(q.Id)).ToListAsync(ct);
        foreach (var question in questions)
        {
            question.SourceType = sourceType;
            question.RightsHolder = string.IsNullOrWhiteSpace(request.RightsHolder) ? null : request.RightsHolder.Trim();
            question.LicenseReference = string.IsNullOrWhiteSpace(request.LicenseReference) ? null : request.LicenseReference.Trim();
            question.LicenseEndsAt = request.LicenseEndsAt;
        }

        await _context.SaveChangesAsync(ct);
        return questions.Count;
    }

    public async Task<int> SetPublishedAsync(QuestionSourcePublishDto request, CancellationToken ct = default)
    {
        var holder = request.RightsHolder.Trim();
        var questions = await _context.Questions
            .Where(q => q.RightsHolder == holder && q.IsUnpublished != request.Unpublished)
            .ToListAsync(ct);

        foreach (var question in questions)
            question.IsUnpublished = request.Unpublished;

        _context.QuestionSourceActionLogs.Add(new QuestionSourceActionLog
        {
            RightsHolder = holder,
            Unpublished = request.Unpublished,
            AffectedCount = questions.Count,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim()
        });

        await _context.SaveChangesAsync(ct);
        return questions.Count;
    }
}
