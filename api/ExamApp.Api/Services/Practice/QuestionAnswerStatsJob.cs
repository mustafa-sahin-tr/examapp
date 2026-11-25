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

    /// <summary>Issue #527: an answer is slow from this many times the average time on.</summary>
    [Range(1.1, 20)]
    public double TimeSlowFactor { get; set; } = 2.0;

    /// <summary>Issue #527: a wrong answer is rushed below this share of the average time …</summary>
    [Range(0.01, 0.9)]
    public double TimeRushedFactor { get; set; } = 0.25;

    /// <summary>… and at most this many seconds.</summary>
    [Range(1, 600)]
    public int TimeRushedMaxSeconds { get; set; } = 15;

    /// <summary>Issue #527: a question's own average needs this many timed answers; below it the subtopic average is used.</summary>
    [Range(1, 1_000)]
    public int TimeMinAnswers { get; set; } = 10;

    /// <summary>Issue #533: look-back window of the topic peer stats.</summary>
    [Range(7, 365)]
    public int PeerWindowDays { get; set; } = 90;

    /// <summary>Issue #533: a student counts in a topic's peer stats from this many answers there.</summary>
    [Range(1, 100)]
    public int PeerMinAnswers { get; set; } = 5;

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
/// #528 adds the first-choice counts (<see cref="QuestionChoiceStat"/>), #533 the grade + topic peer accuracy (<see cref="TopicAnswerStat"/>).
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
        var peerFrom = now.AddDays(-options.CurrentValue.PeerWindowDays);
        var perStudentTopic = new Dictionary<(int StudentId, int TopicId), (int Answered, int Correct)>();
        var written = 0;
        var lastId = 0;
        while (true)
        {
            var questions = await db.Questions.AsNoTracking().IgnoreQueryFilters()
                .Where(q => q.Id > lastId)
                .OrderBy(q => q.Id)
                .Take(batchSize)
                .Select(q => new
                {
                    q.Id,
                    q.IsDeleted,
                    q.CorrectAnswerId,
                    q.InteractionType,
                    // #533: the topic, else the first subtopic's (AnsweredQuestion.EffectiveTopicId).
                    TopicId = q.TopicId ?? q.QuestionSubTopics.Where(st => !st.IsDeleted).OrderBy(st => st.SubTopicId)
                        .Select(st => (int?)st.SubTopic.TopicId).FirstOrDefault(),
                })
                .ToListAsync(ct);
            if (questions.Count == 0)
                break;
            lastId = questions[^1].Id;

            var batchIds = questions.Select(q => q.Id).ToList();
            var known = questions.Where(q => !q.IsDeleted && q.InteractionType != OpenTextGrader.Type).ToDictionary(q => q.Id);
            var answers = await ReadTestAnswersAsync([.. known.Keys], id => (known[id].CorrectAnswerId, !known[id].IsDeleted), now, ct);
            answers.AddRange(await ReadPracticeAnswersAsync([.. known.Keys], ct));

            // #533: every answer in the window, per student and topic.
            foreach (var a in answers.Where(a => a.At >= peerFrom))
            {
                if (known[a.QuestionId].TopicId is not { } topicId)
                    continue;
                var key = (a.StudentId, topicId);
                var (answered, correct) = perStudentTopic.GetValueOrDefault(key);
                perStudentTopic[key] = (answered + 1, correct + (a.Correct ? 1 : 0));
            }

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
        var topicRows = await WriteTopicStatsAsync(perStudentTopic, now, ct);
        logger.LogInformation("Question answer stats: {Rows} questions, {TopicRows} grade topics written.", written, topicRows);
        return written;
    }

    /// <summary>
    /// Issue #533: per grade (the student's current one) and topic, the mean and median accuracy of the students with at least
    /// <c>PeerMinAnswers</c> answers there. The whole table is replaced in one transaction.
    /// </summary>
    private async Task<int> WriteTopicStatsAsync(Dictionary<(int StudentId, int TopicId), (int Answered, int Correct)> perStudentTopic,
        DateTime now, CancellationToken ct)
    {
        var minAnswers = options.CurrentValue.PeerMinAnswers;
        var studentIds = perStudentTopic.Keys.Select(k => k.StudentId).Distinct().ToList();
        var grades = await db.Students.AsNoTracking()
            .Where(s => studentIds.Contains(s.Id) && s.GradeId != null)
            .ToDictionaryAsync(s => s.Id, s => s.GradeId!.Value, ct);
        var rows = perStudentTopic
            .Where(x => x.Value.Answered >= minAnswers && grades.ContainsKey(x.Key.StudentId))
            .GroupBy(x => (GradeId: grades[x.Key.StudentId], x.Key.TopicId), x => 100.0 * x.Value.Correct / x.Value.Answered)
            .Select(g =>
            {
                var sorted = g.OrderBy(v => v).ToList();
                var mid = sorted.Count / 2;
                return new TopicAnswerStat
                {
                    GradeId = g.Key.GradeId,
                    TopicId = g.Key.TopicId,
                    StudentCount = sorted.Count,
                    AccuracyPct = Math.Round(sorted.Average(), 1),
                    MedianPct = Math.Round(sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2, 1),
                    ComputedAtUtc = now,
                };
            })
            .ToList();

        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.TopicAnswerStats.ExecuteDeleteAsync(ct);
            db.TopicAnswerStats.AddRange(rows);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
        db.ChangeTracker.Clear();
        return rows.Count;
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
