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

    /// <summary><c>global</c> mode: how far back the student's own answers are read to leave out what they already solved.</summary>
    private const int OwnHistoryYears = 5;

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

    /// <summary>
    /// <c>global</c> mode: pool questions (as above, not blocked) that at least <paramref name="minStudents"/> distinct students
    /// answered, lowest correct rate first (nightly <see cref="QuestionAnswerStat"/>; no aggregate at request time). A question the
    /// student answered correctly last is left out. Only the aggregate percent leaves: no other student's data.
    /// </summary>
    /// <param name="onlyIds">Practice validation: restrict to these ids and return every match (no take).</param>
    internal static async Task<(int Total, List<HardQuestionDto> Items)> LoadGlobalAsync(
        AppDbContext context, ILocalDayCalendar calendar, StudentProfileDto student, HardQuestionFilter filter, int minStudents,
        int take, IReadOnlyCollection<int>? onlyIds, CancellationToken ct)
    {
        if (student.GradeId is not { } gradeId)
            return (0, []);

        var to = DateTime.UtcNow;
        var own = (await new StudentAnswerReader(context).ReadAsync(student.Id, to.AddYears(-OwnHistoryYears), to, ct))
            .GroupBy(a => a.Question.Id)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.At).ToList());
        var solved = own.Where(o => o.Value[^1].Outcome == StudentAnswerOutcome.Correct).Select(o => o.Key).ToList();
        var blocked = (await DueReviewQueries.LoadBlockedAsync(context, calendar, student, ct)).ToList();

        var pool = PracticeQuestionPool.Build(context, gradeId, filter.SubjectIds,
                filter.TopicId is { } topicId ? [topicId] : [], filter.SubTopicId is { } subTopicId ? [subTopicId] : null)
            .Where(q => !blocked.Contains(q.Id) && !solved.Contains(q.Id));
        if (onlyIds is not null)
            pool = pool.Where(q => onlyIds.Contains(q.Id));
        var ranked = pool
            .Join(context.QuestionAnswerStats.Where(s => s.StudentCount >= minStudents), q => q.Id, s => s.QuestionId, (q, s) => new { q, s })
            .OrderBy(x => (double)x.s.CorrectCount / x.s.StudentCount)
            .ThenByDescending(x => x.s.StudentCount)
            .ThenBy(x => x.q.Id);

        var total = await ranked.CountAsync(ct);
        var page = await (onlyIds is null ? ranked.Take(take) : ranked)
            .Select(x => new
            {
                x.q.Id,
                x.q.SubjectId,
                SubjectName = x.q.Subject != null ? x.q.Subject.Name : null,
                x.q.TopicId,
                TopicName = x.q.Topic != null ? x.q.Topic.Name : null,
                SubTopicNames = x.q.QuestionSubTopics.Where(st => !st.IsDeleted).Select(st => st.SubTopic.Name).ToList(),
                x.q.ImageUrl,
                x.q.Text,
                x.s.CorrectCount,
                x.s.StudentCount,
            })
            .AsSplitQuery()
            .ToListAsync(ct);

        return (total, page.Select(q =>
        {
            var attempts = own.GetValueOrDefault(q.Id) ?? [];
            var mine = attempts.Count > 0 ? Rank(attempts.GroupBy(a => a.Question.Id).Single()) : null;
            return new HardQuestionDto(
                q.Id, q.SubjectId, q.SubjectName, q.TopicId, q.TopicName, q.SubTopicNames.Distinct().ToList(),
                attempts.Count,
                mine?.Misses ?? 0,
                mine?.MissedAfterCorrect ?? false,
                false,
                attempts.Count > 0 ? attempts[^1].At : null,
                string.IsNullOrWhiteSpace(q.ImageUrl) ? null : q.ImageUrl,
                string.IsNullOrWhiteSpace(q.Text) ? null : q.Text,
                (int)Math.Round(100.0 * q.CorrectCount / q.StudentCount));
        }).ToList());
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
