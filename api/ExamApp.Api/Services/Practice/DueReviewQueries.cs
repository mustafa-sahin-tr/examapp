using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Dashboard;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Practice;

/// <summary>Tekrar listesi sorguları; pratik oturumu (#529) ve günlük set (#697) ortak kullanır.</summary>
internal static class DueReviewQueries
{
    /// <summary>
    /// Issue #529: tekrar zamanı gelen sorular. Geçmiş, pratik cevapları ve tamamlanmış testlerdir (etkinlik puansız, sayılmaz).
    /// Havuz kuralından geçmeyen, bitmemiş test/atamada olan ve bugünün günlük setinde bekleyen soru dönmez: tekrar
    /// oturumu doğru şıkkı gösterdiği için bunlar önceden çözdürülmüş olurdu (security O1, #99).
    /// </summary>
    internal static async Task<List<DueReviewItemDto>> LoadAsync(
        AppDbContext context, ILocalDayCalendar calendar, StudentProfileDto student, IReadOnlyCollection<int> subjectIds, CancellationToken ct)
    {
        var attempts = await LoadAttemptsAsync(context, student.Id, ct);

        var today = calendar.Today;
        var statuses = attempts
            .Select(a => (QuestionId: a.Key, Status: DueReviewSchedule.Evaluate(a.Value, calendar.TimeZone)))
            .Where(x => x.Status is { } status && status.DueDay <= today)
            .ToDictionary(x => x.QuestionId, x => x.Status!.Value);

        if (statuses.Count == 0)
            return new List<DueReviewItemDto>();

        var blocked = await LoadBlockedAsync(context, calendar, student, ct);
        var candidateIds = statuses.Keys.Where(id => !blocked.Contains(id)).ToList();

        var questions = await PracticeQuestionPool.Build(context, student.GradeId!.Value, subjectIds, Array.Empty<int>())
            .Where(q => candidateIds.Contains(q.Id))
            .Select(q => new
            {
                q.Id,
                q.SubjectId,
                SubjectName = q.Subject != null ? q.Subject.Name : null,
                TopicName = q.Topic != null ? q.Topic.Name : null
            })
            .ToListAsync(ct);

        return questions
            .Select(q => new DueReviewItemDto
            {
                QuestionId = q.Id,
                SubjectId = q.SubjectId,
                SubjectName = q.SubjectName,
                TopicName = q.TopicName,
                DueSince = calendar.StartOfDayUtc(statuses[q.Id].DueDay),
                Step = statuses[q.Id].Step,
                WrongCount = statuses[q.Id].WrongCount
            })
            .OrderBy(i => i.DueSince)
            .ThenByDescending(i => i.WrongCount)
            .ThenBy(i => i.QuestionId)
            .ToList();
    }

    /// <summary>Öğrencinin soru başına cevapları: pratik ve tamamlanmış testler. Yanlış ve boş cevap doğru sayılmaz.</summary>
    internal static async Task<Dictionary<int, List<DueReviewAttempt>>> LoadAttemptsAsync(
        AppDbContext context, int studentId, CancellationToken ct)
    {
        var practice = await context.PracticeSessionQuestions
            .AsNoTracking()
            .Where(p => p.PracticeSession.StudentId == studentId && p.AnsweredAt != null)
            .Select(p => new { p.QuestionId, At = p.AnsweredAt!.Value, p.IsCorrect })
            .ToListAsync(ct);

        var tests = await context.TestInstanceQuestions
            .AsNoTracking()
            .Where(q => q.WorksheetInstance.StudentId == studentId
                && q.WorksheetInstance.Status == WorksheetInstanceStatus.Completed
                && q.WorksheetInstance.Worksheet.Kind == WorksheetKind.Test)
            .Select(q => new
            {
                q.WorksheetQuestion.QuestionId,
                At = q.UpdateTime ?? q.WorksheetInstance.EndTime ?? q.WorksheetInstance.StartTime,
                q.IsCorrect
            })
            .ToListAsync(ct);

        return practice
            .Select(p => (p.QuestionId, Attempt: new DueReviewAttempt(p.At, p.IsCorrect)))
            .Concat(tests.Select(t => (t.QuestionId, Attempt: new DueReviewAttempt(t.At, t.IsCorrect))))
            .GroupBy(a => a.QuestionId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.Attempt).ToList());
    }

    internal static async Task<HashSet<int>> LoadBlockedAsync(
        AppDbContext context, ILocalDayCalendar calendar, StudentProfileDto student, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var completed = context.TestInstances
            .Where(i => i.StudentId == student.Id && i.Status == WorksheetInstanceStatus.Completed)
            .Select(i => i.WorksheetId);

        var started = context.TestInstances
            .Where(i => i.StudentId == student.Id && i.Status == WorksheetInstanceStatus.Started)
            .Select(i => i.WorksheetId);

        var assigned = context.WorksheetAssignments
            .Where(a => a.StartAt <= now && (a.EndAt == null || a.EndAt > now))
            .Where(a => a.StudentId == student.Id
                || a.GradeId == student.GradeId
                || a.IsPlatformWide
                || (student.SchoolId != null && a.SchoolId == student.SchoolId))
            .Where(a => !completed.Contains(a.WorksheetId))
            .Select(a => a.WorksheetId);

        var blocked = await context.TestQuestions
            .AsNoTracking()
            .Where(wq => started.Contains(wq.TestId) || assigned.Contains(wq.TestId))
            .Select(wq => wq.QuestionId)
            .ToListAsync(ct);

        blocked.AddRange(await DailySetQueries.LoadTodaysUnansweredQuestionIdsAsync(context, calendar, student.Id, ct));
        return blocked.ToHashSet();
    }
}
