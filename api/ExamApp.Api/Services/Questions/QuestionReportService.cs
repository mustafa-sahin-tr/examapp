using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.QuestionReports;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Questions;

public class QuestionReportService : IQuestionReportService
{
    private const int MaxPageSize = 50;

    private readonly AppDbContext _context;

    public QuestionReportService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<QuestionReportCreatedDto?> CreateAsync(
        int questionId, CreateQuestionReportDto dto, int studentUserId, CancellationToken ct = default)
    {
        var inSession = await _context.TestInstanceQuestions
            .WithRemovedWorksheetItems()
            .AnyAsync(tiq =>
                tiq.WorksheetInstanceId == dto.TestInstanceId
                && tiq.WorksheetQuestion.QuestionId == questionId
                && tiq.WorksheetInstance.Student.UserId == studentUserId, ct);
        if (!inSession)
            return null;

        if (await AlreadyReportedAsync(questionId, studentUserId, ct))
            return new QuestionReportCreatedDto { AlreadyReported = true };

        _context.QuestionReports.Add(new QuestionReport
        {
            QuestionId = questionId,
            ReporterUserId = studentUserId,
            TestInstanceId = dto.TestInstanceId,
            Reason = Enum.Parse<QuestionReportReason>(dto.Reason, ignoreCase: true),
            Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim(),
        });

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Aynı anda gelen iki istek: unique index ikincisini reddeder, sonuç yine "bildirildi".
            if (!await AlreadyReportedAsync(questionId, studentUserId, ct))
                throw;
            return new QuestionReportCreatedDto { AlreadyReported = true };
        }

        return new QuestionReportCreatedDto { AlreadyReported = false };
    }

    public async Task<QuestionReportPageDto> ListAsync(
        int userId, bool isAdmin, QuestionReportStatus status, int page, int pageSize, CancellationToken ct = default)
    {
        var query = VisibleTo(userId, isAdmin).Where(r => r.Status == status);
        var size = Math.Clamp(pageSize, 1, MaxPageSize);
        var skip = (Math.Max(page, 1) - 1) * size;

        var rows = await query
            .OrderByDescending(r => r.CreateTime)
            .ThenByDescending(r => r.Id)
            .Skip(skip)
            .Take(size)
            .Select(r => new
            {
                r.Id,
                r.QuestionId,
                r.Question.ImageUrl,
                r.Reason,
                r.Note,
                r.Status,
                r.CreateTime,
            })
            .ToListAsync(ct);

        return new QuestionReportPageDto
        {
            TotalCount = await query.CountAsync(ct),
            Items = rows.Select(r => new QuestionReportItemDto
            {
                Id = r.Id,
                QuestionId = r.QuestionId,
                QuestionImageUrl = r.ImageUrl,
                Reason = JsonName(r.Reason),
                Note = r.Note,
                Status = r.Status == QuestionReportStatus.Open ? "open" : "reviewed",
                CreateTime = r.CreateTime,
            }).ToList(),
        };
    }

    public async Task<bool> MarkReviewedAsync(int reportId, int userId, bool isAdmin, CancellationToken ct = default)
    {
        var report = await VisibleTo(userId, isAdmin).FirstOrDefaultAsync(r => r.Id == reportId, ct);
        if (report == null)
            return false;

        report.Status = QuestionReportStatus.Reviewed;
        await _context.SaveChangesAsync(ct);
        return true;
    }

    private Task<bool> AlreadyReportedAsync(int questionId, int studentUserId, CancellationToken ct) =>
        _context.QuestionReports.AsNoTracking().AnyAsync(r => r.QuestionId == questionId && r.ReporterUserId == studentUserId, ct);

    private IQueryable<QuestionReport> VisibleTo(int userId, bool isAdmin)
    {
        if (isAdmin)
            return _context.QuestionReports;

        var ownedQuestions = _context.Questions.Where(QuestionOwnershipGuard.OwnedBy(_context, userId)).Select(q => q.Id);
        return _context.QuestionReports.Where(r => ownedQuestions.Contains(r.QuestionId));
    }

    private static string JsonName(QuestionReportReason reason)
    {
        var name = reason.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
