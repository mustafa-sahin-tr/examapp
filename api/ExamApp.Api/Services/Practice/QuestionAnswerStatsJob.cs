using System.ComponentModel.DataAnnotations;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Services.Worksheets;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Practice;

/// <summary>Issue #526: settings of the <c>global</c> hard questions and their nightly stats job.</summary>
public sealed class HardQuestionOptions
{
    public const string SectionName = "HardQuestions";

    /// <summary>A question needs this many distinct students' answers to show (k-anonymity and noise).</summary>
    [Range(1, 10_000)]
    public int MinStudents { get; set; } = 20;

    /// <summary>Hangfire cron of the stats job (UTC). Default 03:00 UTC (06:00 Istanbul).</summary>
    [Required]
    public string StatsCron { get; set; } = "0 3 * * *";

    /// <summary>Issue #528: a subtopic is a trap pattern from this many wrong questions on.</summary>
    [Range(2, 100)]
    public int TrapMinMistakes { get; set; } = 3;

    /// <summary>Questions read per batch.</summary>
    [Range(1, 10_000)]
    public int BatchSize { get; set; } = 1_000;
}

public interface IQuestionAnswerStatsJob
{
    /// <summary>Recomputes every question's stats; returns the number of rows written. The next run retries.</summary>
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    Task<int> RunAsync(CancellationToken ct = default);
}

/// <summary>
/// Issue #526: per question, the distinct students who answered it and how many got it right, from finished test sessions and
/// practice answers. A student counts once, with their earliest answer (solving again does not inflate the rate). A blank or
/// skipped answer is not an answer (#785). A running test session is never read. Activities and open-text questions are out
/// (as in the topic report, #497). The rows of a batch are replaced in one transaction; a question nobody answered has no row.
/// </summary>
public sealed class QuestionAnswerStatsJob(AppDbContext db, IOptionsMonitor<HardQuestionOptions> options, ILogger<QuestionAnswerStatsJob> logger)
    : IQuestionAnswerStatsJob
{
    public const string RecurringJobId = "question-answer-stats";

    private sealed record FirstAnswer(int QuestionId, int StudentId, DateTime At, bool Correct, int? SelectedAnswerId);

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var batchSize = options.CurrentValue.BatchSize;
        var now = DateTime.UtcNow;
        var written = 0;
        var lastId = 0;
        while (true)
        {
            var questions = await db.Questions.AsNoTracking().IgnoreQueryFilters()
                .Where(q => q.Id > lastId)
                .OrderBy(q => q.Id)
                .Take(batchSize)
                .Select(q => new { q.Id, q.IsDeleted, q.CorrectAnswerId, q.InteractionType })
                .ToListAsync(ct);
            if (questions.Count == 0)
                break;
            lastId = questions[^1].Id;

            var batchIds = questions.Select(q => q.Id).ToList();
            var known = questions.Where(q => !q.IsDeleted && q.InteractionType != OpenTextGrader.Type).ToDictionary(q => q.Id);
            var answers = await ReadTestAnswersAsync([.. known.Keys], id => (known[id].CorrectAnswerId, !known[id].IsDeleted), now, ct);
            answers.AddRange(await ReadPracticeAnswersAsync([.. known.Keys], ct));

            var firsts = answers
                .GroupBy(a => (a.QuestionId, a.StudentId))
                .Select(g => g.OrderBy(a => a.At).First())
                .ToList();
            var rows = firsts
                .GroupBy(a => a.QuestionId)
                .Select(g => new QuestionAnswerStat
                {
                    QuestionId = g.Key,
                    StudentCount = g.Count(),
                    CorrectCount = g.Count(a => a.Correct),
                    ComputedAtUtc = now,
                })
                .ToList();
            // #528: the choice of each first answer (a choice question; an open payload has none).
            var choices = firsts
                .Where(a => a.SelectedAnswerId is not null)
                .GroupBy(a => (a.QuestionId, AnswerId: a.SelectedAnswerId!.Value))
                .Select(g => new QuestionChoiceStat { QuestionId = g.Key.QuestionId, AnswerId = g.Key.AnswerId, StudentCount = g.Count(), ComputedAtUtc = now })
                .ToList();

            // The retrying execution strategy runs a user transaction only as one retriable unit.
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await db.QuestionAnswerStats.Where(s => batchIds.Contains(s.QuestionId)).ExecuteDeleteAsync(ct);
                await db.QuestionChoiceStats.Where(c => batchIds.Contains(c.QuestionId)).ExecuteDeleteAsync(ct);
                db.QuestionAnswerStats.AddRange(rows);
                db.QuestionChoiceStats.AddRange(choices);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            });
            db.ChangeTracker.Clear();
            written += rows.Count;
        }
        logger.LogInformation("Question answer stats: {Rows} questions written.", written);
        return written;
    }

    private async Task<List<FirstAnswer>> ReadTestAnswersAsync(List<int> questionIds, Func<int, (int? CorrectAnswerId, bool Known)> question,
        DateTime now, CancellationToken ct)
    {
        // Session-row read rule (#684): an item removed later still counts; so does a deleted test (no navigation filter).
        var rows = await db.TestInstanceQuestions.AsNoTracking().WithRemovedWorksheetItems()
            .Where(r => questionIds.Contains(r.WorksheetQuestion.QuestionId))
            .Where(r => r.SelectedAnswerId != null || r.AnswerPayload != null)
            .Where(r => !db.Worksheets.IgnoreQueryFilters().Any(w => w.Id == r.WorksheetInstance.WorksheetId && w.Kind == WorksheetKind.Activity))
            .Select(r => new
            {
                r.WorksheetQuestion.QuestionId,
                r.WorksheetInstance.StudentId,
                r.WorksheetInstance.Status,
                r.WorksheetInstance.StartTime,
                r.WorksheetInstance.EndTime,
                r.WorksheetInstance.MaxDurationSeconds,
                r.SelectedAnswerId,
                HasPayload = r.AnswerPayload != null,
                r.IsCorrect,
            })
            .ToListAsync(ct);

        return rows
            // Exam integrity: a running session's answer can still change and is never read.
            .Where(r => TestTimeLimit.EffectiveStatus(r.Status, r.StartTime, r.MaxDurationSeconds, now) != WorksheetInstanceStatus.Started)
            .Select(r =>
            {
                var (correctAnswerId, known) = question(r.QuestionId);
                return new FirstAnswer(r.QuestionId, r.StudentId,
                    r.EndTime ?? TestTimeLimit.EndsAt(r.StartTime, r.MaxDurationSeconds) ?? r.StartTime,
                    WorksheetScoring.IsCorrect(r.SelectedAnswerId, correctAnswerId, r.HasPayload, r.IsCorrect, questionKnown: known),
                    r.SelectedAnswerId);
            })
            .ToList();
    }

    private async Task<List<FirstAnswer>> ReadPracticeAnswersAsync(List<int> questionIds, CancellationToken ct) =>
        await db.PracticeSessionQuestions.AsNoTracking()
            .Where(r => questionIds.Contains(r.QuestionId) && r.AnsweredAt != null && !r.IsSkipped)
            .Select(r => new FirstAnswer(r.QuestionId, r.PracticeSession.StudentId, r.AnsweredAt!.Value, r.IsCorrect, r.SelectedAnswerId))
            .ToListAsync(ct);
}
