using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Services.TopicPerformance;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Practice;

/// <summary>
/// Issue #528: subtopics where the student keeps falling for the same trap. A question's popular distractor is the wrong choice
/// most students picked with their first answer (nightly <see cref="QuestionChoiceStat"/>, only for questions at least
/// <c>HardQuestions:MinStudents</c> students answered; a tie has none): an aggregate, never another student's own choice.
/// The student's latest wrong choice per question is compared with it. A subtopic (a question without subtopics: its topic) is a
/// pattern when the student got at least <c>TrapMinMistakes</c> of its questions wrong and picked the popular distractor in most
/// of them. Answers: the topic report reader (finished tests and practice, a running test is never read); questions blocked like
/// the mistake notebook (#698: a running or open assigned test, today's daily set) are left out, so every example can be explained
/// with #510. The correct choice is never returned.
/// </summary>
internal static class RepeatedTrapQueries
{
    public const int MaxExamples = 3;

    private readonly record struct GroupKey(int TopicId, int? SubTopicId);

    private sealed record WrongPick(StudentAnswer Answer, int PopularDistractorId);

    internal static async Task<List<RepeatedTrapDto>> LoadAsync(AppDbContext context, ILocalDayCalendar calendar, StudentProfileDto student,
        HardQuestionFilter filter, int minStudents, int minMistakes, CancellationToken ct)
    {
        var to = DateTime.UtcNow;
        var blocked = await DueReviewQueries.LoadBlockedAsync(context, calendar, student, ct);
        // The student's latest wrong choice of each question.
        var latestWrong = (await new StudentAnswerReader(context).ReadAsync(student.Id, to.AddDays(-Math.Clamp(filter.Days, 1, HardQuestionQueries.MaxDays)), to, ct))
            .Where(a => filter.SubjectIds.Count == 0 || (a.Question.SubjectId is { } s && filter.SubjectIds.Contains(s)))
            .Where(a => filter.TopicId is null || a.Question.EffectiveTopicId == filter.TopicId)
            .Where(a => !blocked.Contains(a.Question.Id) && !a.Question.Deleted)
            .GroupBy(a => a.Question.Id)
            .Select(g => g.OrderBy(a => a.At).Last())
            .Where(a => a.Outcome == StudentAnswerOutcome.Wrong && a.SelectedAnswerId is not null && a.Question.EffectiveTopicId is not null)
            .ToList();
        if (latestWrong.Count < minMistakes)
            return [];

        var ids = latestWrong.Select(a => a.Question.Id).ToList();
        var counts = await context.QuestionChoiceStats.AsNoTracking()
            .Where(c => ids.Contains(c.QuestionId)
                && context.QuestionAnswerStats.Any(s => s.QuestionId == c.QuestionId && s.StudentCount >= minStudents))
            .Select(c => new { c.QuestionId, c.AnswerId, c.StudentCount })
            .ToListAsync(ct);
        var popular = counts
            .GroupBy(c => c.QuestionId)
            .Select(g =>
            {
                var correct = latestWrong.First(a => a.Question.Id == g.Key).Question.CorrectAnswerId;
                var wrong = g.Where(c => c.AnswerId != correct).OrderByDescending(c => c.StudentCount).ToList();
                return (QuestionId: g.Key, AnswerId: wrong.Count > 0 && (wrong.Count == 1 || wrong[0].StudentCount > wrong[1].StudentCount)
                    ? wrong[0].AnswerId : (int?)null);
            })
            .Where(p => p.AnswerId is not null)
            .ToDictionary(p => p.QuestionId, p => p.AnswerId!.Value);

        var patterns = latestWrong
            .SelectMany(a => KeysOf(a.Question).Select(key => (Key: key, Answer: a)))
            .GroupBy(x => x.Key, x => x.Answer)
            .Select(g =>
            {
                var traps = g.Where(a => popular.TryGetValue(a.Question.Id, out var d) && d == a.SelectedAnswerId)
                    .OrderByDescending(a => a.At).ThenBy(a => a.Question.Id).ToList();
                return (g.Key, Mistakes: g.Count(), Traps: traps);
            })
            .Where(p => p.Mistakes >= minMistakes && p.Traps.Count * 2 > p.Mistakes)
            .ToList();
        if (patterns.Count == 0)
            return [];

        var exampleIds = patterns.SelectMany(p => p.Traps.Take(MaxExamples)).Select(a => a.Question.Id).Distinct().ToList();
        var questions = await context.Questions.AsNoTracking()
            .Where(q => exampleIds.Contains(q.Id))
            .Select(q => new
            {
                q.Id,
                q.ImageUrl,
                q.Text,
                Choices = q.Answers.Where(a => !a.IsDeleted).OrderBy(a => a.Order).ThenBy(a => a.Id).Select(a => new { a.Id, a.Text }).ToList(),
            })
            .AsSplitQuery()
            .ToDictionaryAsync(q => q.Id, ct);

        var topicIds = patterns.Select(p => p.Key.TopicId).Distinct().ToList();
        var topics = await context.Topics.AsNoTracking().IgnoreQueryFilters()
            .Where(t => topicIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Name, t.SubjectId, SubjectName = t.Subject.Name })
            .ToDictionaryAsync(t => t.Id, ct);

        return patterns
            .OrderByDescending(p => (double)p.Traps.Count / p.Mistakes)
            .ThenByDescending(p => p.Traps.Count)
            .ThenBy(p => p.Key.TopicId).ThenBy(p => p.Key.SubTopicId)
            .Select(p =>
            {
                var topic = topics.GetValueOrDefault(p.Key.TopicId);
                var subTopicName = p.Key.SubTopicId is { } id
                    ? p.Traps.SelectMany(a => a.Question.SubTopics).First(s => s.Id == id).Name
                    : null;
                var examples = p.Traps.Where(a => questions.ContainsKey(a.Question.Id)).Take(MaxExamples).Select(a =>
                {
                    var q = questions[a.Question.Id];
                    var index = q.Choices.FindIndex(c => c.Id == a.SelectedAnswerId);
                    return new RepeatedTrapExampleDto(
                        a.Question.Id,
                        index >= 0 ? ((char)('A' + index)).ToString() : null,
                        index >= 0 && !string.IsNullOrWhiteSpace(q.Choices[index].Text) ? q.Choices[index].Text : null,
                        a.At,
                        string.IsNullOrWhiteSpace(q.ImageUrl) ? null : q.ImageUrl,
                        string.IsNullOrWhiteSpace(q.Text) ? null : q.Text);
                }).ToList();
                return new RepeatedTrapDto(topic?.SubjectId, topic?.SubjectName, p.Key.TopicId, topic?.Name, p.Key.SubTopicId, subTopicName,
                    p.Mistakes, p.Traps.Count, (int)Math.Round(100.0 * p.Traps.Count / p.Mistakes), examples);
            })
            .ToList();
    }

    /// <summary>A question counts under each of its subtopics; one without subtopics under its topic.</summary>
    private static IEnumerable<GroupKey> KeysOf(AnsweredQuestion question) =>
        question.SubTopics.Count > 0
            ? question.SubTopics.Select(s => new GroupKey(s.TopicId, s.Id))
            : [new GroupKey(question.EffectiveTopicId!.Value, null)];
}
