using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.QuestionReports;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Questions;

public class QuestionReportService : IQuestionReportService
{
    private const int MaxPageSize = 50;
    private const int NotesLimit = 3;
    private const int TestNamesLimit = 2;

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
        var visible = VisibleTo(userId, isAdmin).Where(r => r.Status == status);
        var size = Math.Clamp(pageSize, 1, MaxPageSize);
        var skip = (Math.Max(page, 1) - 1) * size;

        // #865: satır = soru; aynı sorunun aynı durumdaki bildirimleri birleşir.
        var groups = visible
            .GroupBy(r => r.QuestionId)
            .Select(g => new { QuestionId = g.Key, LastTime = g.Max(r => r.CreateTime), LastId = g.Max(r => r.Id) });

        var pageGroups = await groups
            .OrderByDescending(g => g.LastTime)
            .ThenByDescending(g => g.LastId)
            .Skip(skip)
            .Take(size)
            .ToListAsync(ct);
        var total = await groups.CountAsync(ct);
        if (pageGroups.Count == 0)
            return new QuestionReportPageDto { TotalCount = total };

        var questionIds = pageGroups.Select(g => g.QuestionId).ToList();

        var reports = await visible
            .Where(r => questionIds.Contains(r.QuestionId))
            .OrderByDescending(r => r.CreateTime)
            .ThenByDescending(r => r.Id)
            .Select(r => new
            {
                r.Id,
                r.QuestionId,
                r.Reason,
                r.Note,
                r.CreateTime,
                r.Outcome,
                r.OutcomeNote,
                r.ReviewedAt,
                r.ReviewedByUserId,
            })
            .ToListAsync(ct);

        var images = await _context.Questions
            .Where(q => questionIds.Contains(q.Id))
            .Select(q => new { q.Id, q.ImageUrl })
            .ToDictionaryAsync(q => q.Id, q => q.ImageUrl, ct);

        var tests = (await _context.TestQuestions
                .Where(tq => questionIds.Contains(tq.QuestionId))
                .Select(tq => new { tq.QuestionId, tq.TestId, tq.Worksheet.Name })
                .Distinct()
                .ToListAsync(ct))
            .GroupBy(t => t.QuestionId)
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.Name).Select(t => t.Name).ToList());

        // Cevap verilmiş (şık ya da etkileşim cevabı) satırların farklı öğrencileri.
        var solved = await _context.TestInstanceQuestions
            .Where(tiq => questionIds.Contains(tiq.WorksheetQuestion.QuestionId)
                && (tiq.SelectedAnswerId != null || tiq.AnswerPayload != null))
            .Select(tiq => new { tiq.WorksheetQuestion.QuestionId, tiq.WorksheetInstance.StudentId })
            .Distinct()
            .GroupBy(x => x.QuestionId)
            .Select(g => new { QuestionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.QuestionId, x => x.Count, ct);

        var byQuestion = reports.ToLookup(r => r.QuestionId);
        return new QuestionReportPageDto
        {
            TotalCount = total,
            Items = pageGroups.Select(g =>
            {
                var rows = byQuestion[g.QuestionId].ToList();
                var last = rows[0];
                var testNames = tests.GetValueOrDefault(g.QuestionId) ?? new List<string>();
                return new QuestionReportItemDto
                {
                    Id = last.Id,
                    QuestionId = g.QuestionId,
                    QuestionImageUrl = images.GetValueOrDefault(g.QuestionId),
                    Reason = JsonName(last.Reason),
                    Reasons = rows.Select(r => JsonName(r.Reason)).Distinct().ToList(),
                    Note = last.Note,
                    Notes = rows.Where(r => r.Note != null).Select(r => r.Note!).Take(NotesLimit).ToList(),
                    ReportCount = rows.Count,
                    TestNames = testNames.Take(TestNamesLimit).ToList(),
                    TestCount = testNames.Count,
                    AnsweredCount = solved.GetValueOrDefault(g.QuestionId),
                    Status = status == QuestionReportStatus.Open ? "open" : "reviewed",
                    CreateTime = last.CreateTime,
                    Outcome = last.Outcome switch
                    {
                        QuestionReportOutcome.Fixed => "fixed",
                        QuestionReportOutcome.NoIssue => "noIssue",
                        _ => null,
                    },
                    OutcomeNote = last.OutcomeNote,
                    ReviewedAt = last.ReviewedAt,
                    ReviewedByMe = last.ReviewedByUserId == userId,
                };
            }).ToList(),
        };
    }

    public async Task<bool> MarkReviewedAsync(
        int reportId, int userId, bool isAdmin, ReviewQuestionReportDto dto, CancellationToken ct = default)
    {
        var report = await VisibleTo(userId, isAdmin).FirstOrDefaultAsync(r => r.Id == reportId, ct);
        if (report == null)
            return false;

        // #865: karar sorunun tüm bekleyen bildirimlerini kapatır (satır soru başına birleşik).
        var related = await VisibleTo(userId, isAdmin)
            .Where(r => r.QuestionId == report.QuestionId && r.Status == QuestionReportStatus.Open && r.Id != report.Id)
            .ToListAsync(ct);

        var outcome = Enum.Parse<QuestionReportOutcome>(dto.Outcome, ignoreCase: true);
        var note = outcome == QuestionReportOutcome.NoIssue && !string.IsNullOrWhiteSpace(dto.Note) ? dto.Note.Trim() : null;
        var now = DateTime.UtcNow;
        foreach (var r in related.Append(report))
        {
            r.Status = QuestionReportStatus.Reviewed;
            r.Outcome = outcome;
            r.OutcomeNote = note;
            r.ReviewedByUserId = userId;
            r.ReviewedAt = now;
        }

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
