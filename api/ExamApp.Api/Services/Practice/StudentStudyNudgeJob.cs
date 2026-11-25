using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Practice;

/// <summary>Issue #519: settings of the study nudge.</summary>
public sealed class StudentStudyNudgeOptions
{
    public const string SectionName = "StudentStudyNudge";

    /// <summary>Hangfire cron (UTC). Default 14:00 UTC (17:00 Istanbul), after school.</summary>
    [Required]
    public string Cron { get; set; } = "0 14 * * *";

    /// <summary>A student is nudged from this many whole days without an answer.</summary>
    [Range(1, 60)]
    public int InactiveDays { get; set; } = 3;

    /// <summary>Beyond this many days the student has left; no nudge (no nagging).</summary>
    [Range(2, 365)]
    public int MaxInactiveDays { get; set; } = 30;

    /// <summary>At most one nudge in this many days.</summary>
    [Range(1, 60)]
    public int MinDaysBetween { get; set; } = 7;

    [Range(1, 5_000)]
    public int BatchSize { get; set; } = 500;
}

public interface IStudentStudyNudgeJob
{
    /// <summary>Returns the number of nudges written. No retry: the next run catches up.</summary>
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    Task<int> RunAsync(CancellationToken ct = default);
}

/// <summary>
/// Issue #519 (assistant Phase 4): students whose last answer (a test session started or a practice answer) is between
/// <c>InactiveDays</c> and <c>MaxInactiveDays</c> days old get one <see cref="StudentStudyNudgeEvent"/>: at most once per inactive
/// spell (the last nudge must be older than the last answer) and once per <c>MinDaysBetween</c>; never when the student turned it
/// off (<see cref="Student.StudyNudgeOptOut"/>). A student who never answered is not nudged. The stamp
/// <see cref="Student.LastStudyNudgeAtUtc"/> is set with a conditional update in the same transaction as the outbox row, so a
/// second run or instance never sends twice. Template text (BadgeService), no LLM.
/// </summary>
public sealed class StudentStudyNudgeJob(AppDbContext db, IOptionsMonitor<StudentStudyNudgeOptions> options, ILogger<StudentStudyNudgeJob> logger)
    : IStudentStudyNudgeJob
{
    public const string RecurringJobId = "student-study-nudge";

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var opts = options.CurrentValue;
        var now = DateTime.UtcNow;
        var spacedBefore = now.AddDays(-opts.MinDaysBetween);
        var sent = 0;
        var lastId = 0;
        while (true)
        {
            var batch = await db.Students.AsNoTracking()
                .Where(s => s.Id > lastId && !s.StudyNudgeOptOut && s.GradeId != null
                    && (s.LastStudyNudgeAtUtc == null || s.LastStudyNudgeAtUtc < spacedBefore))
                .OrderBy(s => s.Id)
                .Select(s => new { s.Id, s.UserId, s.LastStudyNudgeAtUtc })
                .Take(opts.BatchSize)
                .ToListAsync(ct);
            if (batch.Count == 0)
                break;
            lastId = batch[^1].Id;

            var ids = batch.Select(s => s.Id).ToList();
            var lastPractice = await db.PracticeSessionQuestions.AsNoTracking()
                .Where(q => ids.Contains(q.PracticeSession.StudentId) && q.AnsweredAt != null)
                .GroupBy(q => q.PracticeSession.StudentId)
                .Select(g => new { StudentId = g.Key, At = g.Max(q => q.AnsweredAt!.Value) })
                .ToDictionaryAsync(x => x.StudentId, x => x.At, ct);
            var lastTest = await db.TestInstances.AsNoTracking()
                .Where(i => ids.Contains(i.StudentId))
                .GroupBy(i => i.StudentId)
                .Select(g => new { StudentId = g.Key, At = g.Max(i => i.StartTime) })
                .ToDictionaryAsync(x => x.StudentId, x => x.At, ct);

            foreach (var s in batch)
            {
                DateTime? last = new[] { lastPractice.GetValueOrDefault(s.Id), lastTest.GetValueOrDefault(s.Id) }.Max();
                if (last is not { } lastAt || lastAt == default)
                    continue;
                var days = (int)(now - lastAt).TotalDays;
                if (days < opts.InactiveDays || days > opts.MaxInactiveDays)
                    continue;
                if (s.LastStudyNudgeAtUtc is { } previous && previous >= lastAt)
                    continue; // already nudged in this inactive spell
                if (await NudgeAsync(s.Id, s.UserId, s.LastStudyNudgeAtUtc, days, now, ct))
                    sent++;
            }
        }
        logger.LogInformation("Study nudge: {Count} students nudged.", sent);
        return sent;
    }

    private async Task<bool> NudgeAsync(int studentId, int userId, DateTime? previous, int days, DateTime now, CancellationToken ct) =>
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Still opted in and not stamped by a parallel run since we read it.
            var stamped = await db.Students
                .Where(s => s.Id == studentId && !s.StudyNudgeOptOut && s.LastStudyNudgeAtUtc == previous)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.LastStudyNudgeAtUtc, now), ct);
            if (stamped == 0)
            {
                await tx.RollbackAsync(ct);
                return false;
            }
            db.OutboxMessages.Add(new OutboxMessage
            {
                Type = OutboxEventRegistry.NameFor<StudentStudyNudgeEvent>(),
                Content = JsonSerializer.Serialize(new StudentStudyNudgeEvent
                {
                    EventId = Guid.NewGuid(),
                    StudentId = studentId,
                    RecipientUserId = userId,
                    InactiveDays = days,
                    CreatedAtUtc = DateTime.SpecifyKind(now, DateTimeKind.Utc),
                }),
                CreatedAt = now,
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        });
}
