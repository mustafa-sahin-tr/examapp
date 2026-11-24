using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Services.TopicPerformance;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Practice;

/// <summary>Issue #526: scope of the student's hard questions (<c>mine</c> mode).</summary>
public sealed record HardQuestionFilter(int Days, IReadOnlyCollection<int> SubjectIds, int? TopicId, int? SubTopicId);

/// <summary>
/// Issue #526 (assistant Phase 2): the questions the student struggled with most, to show and solve again.
/// Answers: the topic report reader (#497 / #510; finished tests and practice, a running test is never read, reset respected).
/// A miss is a wrong or blank answer. Order: most misses first, then a miss after the last correct answer, then the latest
/// attempt; a question whose latest attempt is correct goes last. Only questions the practice pool can serve (the student's
/// grade, Normal visibility, published) and none blocked like the mistake notebook (#698: a running test, an open assigned test
/// not finished, today's unanswered daily set). The correct choice is never returned: the assistant explains through #510.
/// </summary>
internal static class HardQuestionQueries
{
    public const int DefaultDays = 90;
    public const int MaxDays = 90;
    public const int DefaultTake = 10;
    public const int MaxTake = 30;

    private sealed record Ranked(AnsweredQuestion Question, int Attempts, int Misses, bool MissedAfterCorrect, bool LastCorrect, DateTime LastAt);

    internal static async Task<List<HardQuestionDto>> LoadMineAsync(
        AppDbContext context, ILocalDayCalendar calendar, StudentProfileDto student, HardQuestionFilter filter, CancellationToken ct)
    {
        if (student.GradeId is not { } gradeId)
            return [];

        var to = DateTime.UtcNow;
        var answers = await new StudentAnswerReader(context).ReadAsync(student.Id, to.AddDays(-Math.Clamp(filter.Days, 1, MaxDays)), to, ct);

        var ranked = answers
            .Where(a => filter.SubjectIds.Count == 0 || (a.Question.SubjectId is { } s && filter.SubjectIds.Contains(s)))
            .Where(a => filter.TopicId is null || a.Question.EffectiveTopicId == filter.TopicId)
            .Where(a => filter.SubTopicId is null || a.Question.SubTopics.Any(st => st.Id == filter.SubTopicId))
            .GroupBy(a => a.Question.Id)
            .Select(Rank)
            .OfType<Ranked>()
            .ToList();
        if (ranked.Count == 0)
            return [];

        var blocked = await DueReviewQueries.LoadBlockedAsync(context, calendar, student, ct);
        var candidateIds = ranked.Select(r => r.Question.Id).Where(id => !blocked.Contains(id)).ToList();
        var questions = await PracticeQuestionPool.Build(context, gradeId, [], [])
            .Where(q => candidateIds.Contains(q.Id))
            .Select(q => new
            {
                q.Id,
                SubjectName = q.Subject != null ? q.Subject.Name : null,
                q.ImageUrl,
                q.Text,
            })
            .ToDictionaryAsync(q => q.Id, ct);

        var topicIds = ranked.Select(r => r.Question.EffectiveTopicId).OfType<int>().Distinct().ToList();
        var topics = await context.Topics.AsNoTracking().Where(t => topicIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        return ranked
            .Where(r => questions.ContainsKey(r.Question.Id))
            .OrderBy(r => r.LastCorrect)
            .ThenByDescending(r => r.Misses)
            .ThenByDescending(r => r.MissedAfterCorrect)
            .ThenByDescending(r => r.LastAt)
            .ThenBy(r => r.Question.Id)
            .Select(r =>
            {
                var q = questions[r.Question.Id];
                return new HardQuestionDto(
                    r.Question.Id,
                    r.Question.SubjectId,
                    q.SubjectName,
                    r.Question.EffectiveTopicId,
                    r.Question.EffectiveTopicId is { } t ? topics.GetValueOrDefault(t) : null,
                    r.Question.SubTopics.Select(st => st.Name).Distinct().ToList(),
                    r.Attempts,
                    r.Misses,
                    r.MissedAfterCorrect,
                    r.LastCorrect,
                    r.LastAt,
                    string.IsNullOrWhiteSpace(q.ImageUrl) ? null : q.ImageUrl,
                    string.IsNullOrWhiteSpace(q.Text) ? null : q.Text);
            })
            .ToList();
    }

    /// <summary>Null when the student never missed the question.</summary>
    private static Ranked? Rank(IGrouping<int, StudentAnswer> attempts)
    {
        var ordered = attempts.OrderBy(a => a.At).ToList();
        var misses = ordered.Count(a => a.Outcome != StudentAnswerOutcome.Correct);
        if (misses == 0)
            return null;
        var lastCorrect = ordered.FindLastIndex(a => a.Outcome == StudentAnswerOutcome.Correct);
        var missedAfterCorrect = lastCorrect >= 0 && lastCorrect < ordered.Count - 1;
        var last = ordered[^1];
        return new Ranked(last.Question, ordered.Count, misses, missedAfterCorrect, last.Outcome == StudentAnswerOutcome.Correct, last.At);
    }
}
