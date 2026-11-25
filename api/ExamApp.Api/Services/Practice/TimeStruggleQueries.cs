using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Services.TopicPerformance;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Practice;

/// <summary>
/// Issue #527: questions where the student lost time (<c>slow</c>: their latest answer took at least <c>TimeSlowFactor</c> times
/// the average, right or wrong) or answered wrong in a rush (<c>rushed</c>: below <c>TimeRushedFactor</c> of the average and at
/// most <c>TimeRushedMaxSeconds</c>). The average is computed at request time from the other students' timed answers (finished or
/// expired tests, practice); with fewer than <c>TimeMinAnswers</c> on the question itself the average of its subtopics' questions
/// is used, and with too few there the question is skipped. A time of 0 or over <see cref="MaxValidSeconds"/> is meaningless
/// and never counts. Same scope as #526 <c>mine</c>: practice pool questions, nothing blocked like the mistake notebook.
/// </summary>
internal static class TimeStruggleQueries
{
    public const string Slow = "slow";
    public const string Rushed = "rushed";
    public const int MaxValidSeconds = 30 * 60;

    private sealed record Average(int Seconds, string Scope);

    internal static async Task<List<TimeStruggleDto>> LoadAsync(AppDbContext context, ILocalDayCalendar calendar, StudentProfileDto student,
        string kind, HardQuestionFilter filter, HardQuestionOptions options, CancellationToken ct)
    {
        if (student.GradeId is not { } gradeId)
            return [];

        var to = DateTime.UtcNow;
        var latest = (await new StudentAnswerReader(context).ReadAsync(student.Id, to.AddDays(-Math.Clamp(filter.Days, 1, HardQuestionQueries.MaxDays)), to, ct))
            .Where(a => filter.SubjectIds.Count == 0 || (a.Question.SubjectId is { } s && filter.SubjectIds.Contains(s)))
            .Where(a => filter.TopicId is null || a.Question.EffectiveTopicId == filter.TopicId)
            .Where(a => filter.SubTopicId is null || a.Question.SubTopics.Any(st => st.Id == filter.SubTopicId))
            .Where(a => a.Outcome != StudentAnswerOutcome.Blank)
            .GroupBy(a => a.Question.Id)
            .Select(g => g.OrderBy(a => a.At).Last())
            .Where(a => a.TimeTaken is > 0 and <= MaxValidSeconds)
            .Where(a => kind != Rushed || a.Outcome == StudentAnswerOutcome.Wrong)
            .ToList();
        if (latest.Count == 0)
            return [];

        var blocked = await DueReviewQueries.LoadBlockedAsync(context, calendar, student, ct);
        var candidateIds = latest.Select(a => a.Question.Id).Where(id => !blocked.Contains(id)).ToList();
        var questions = await PracticeQuestionPool.Build(context, gradeId, [], [])
            .Where(q => candidateIds.Contains(q.Id))
            .Select(q => new { q.Id, SubjectName = q.Subject != null ? q.Subject.Name : null, q.ImageUrl, q.Text })
            .ToDictionaryAsync(q => q.Id, ct);
        latest = latest.Where(a => questions.ContainsKey(a.Question.Id)).ToList();
        if (latest.Count == 0)
            return [];

        var averages = await LoadAveragesAsync(context, student.Id, latest.Select(a => a.Question).ToList(), options.TimeMinAnswers, to, ct);
        var topicIds = latest.Select(a => a.Question.EffectiveTopicId).OfType<int>().Distinct().ToList();
        var topics = await context.Topics.AsNoTracking().Where(t => topicIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        return latest
            .Select(a => (Answer: a, Average: averages.GetValueOrDefault(a.Question.Id)))
            .Where(x => x.Average is { Seconds: > 0 })
            .Select(x => (x.Answer, x.Average!, Ratio: (double)x.Answer.TimeTaken / x.Average!.Seconds))
            .Where(x => kind == Slow
                ? x.Ratio >= options.TimeSlowFactor
                : x.Ratio <= options.TimeRushedFactor && x.Answer.TimeTaken <= options.TimeRushedMaxSeconds)
            .OrderBy(x => kind == Slow ? -x.Ratio : x.Ratio)
            .ThenByDescending(x => x.Answer.At)
            .ThenBy(x => x.Answer.Question.Id)
            .Select(x =>
            {
                var a = x.Answer;
                var q = questions[a.Question.Id];
                return new TimeStruggleDto(
                    a.Question.Id, a.Question.SubjectId, q.SubjectName,
                    a.Question.EffectiveTopicId, a.Question.EffectiveTopicId is { } t ? topics.GetValueOrDefault(t) : null,
                    a.Question.SubTopics.Select(st => st.Name).Distinct().ToList(),
                    a.TimeTaken, x.Item2.Seconds, x.Item2.Scope, a.Outcome == StudentAnswerOutcome.Correct, a.At,
                    string.IsNullOrWhiteSpace(q.ImageUrl) ? null : q.ImageUrl,
                    string.IsNullOrWhiteSpace(q.Text) ? null : q.Text);
            })
            .ToList();
    }

    /// <summary>Per question: its own average, else the average over its subtopics' questions; none with too few answers.</summary>
    private static async Task<Dictionary<int, Average>> LoadAveragesAsync(AppDbContext context, int studentId, List<AnsweredQuestion> questions, int minAnswers,
        DateTime now, CancellationToken ct)
    {
        var ids = questions.Select(q => q.Id).ToList();
        var subTopicIds = questions.SelectMany(q => q.SubTopics).Select(s => s.Id).Distinct().ToList();
        var siblings = await context.QuestionSubTopics.AsNoTracking()
            .Where(qs => subTopicIds.Contains(qs.SubTopicId))
            .Select(qs => new { qs.QuestionId, qs.SubTopicId })
            .ToListAsync(ct);
        var allIds = ids.Concat(siblings.Select(s => s.QuestionId)).Distinct().ToList();
        var perQuestion = await LoadTimeSumsAsync(context, studentId, allIds, now, ct);

        var perSubTopic = siblings
            .GroupBy(s => s.SubTopicId)
            .ToDictionary(g => g.Key, g => g.Select(s => perQuestion.GetValueOrDefault(s.QuestionId)).Aggregate((Sum: 0L, Count: 0),
                (acc, x) => (acc.Sum + x.Sum, acc.Count + x.Count)));

        var result = new Dictionary<int, Average>();
        foreach (var q in questions)
        {
            var own = perQuestion.GetValueOrDefault(q.Id);
            if (own.Count >= minAnswers)
            {
                result[q.Id] = new Average((int)Math.Round((double)own.Sum / own.Count), "question");
                continue;
            }
            var sub = q.SubTopics.Select(s => perSubTopic.GetValueOrDefault(s.Id))
                .Aggregate((Sum: 0L, Count: 0), (acc, x) => (acc.Sum + x.Sum, acc.Count + x.Count));
            if (sub.Count >= minAnswers)
                result[q.Id] = new Average((int)Math.Round((double)sub.Sum / sub.Count), "subTopic");
        }
        return result;
    }

    /// <summary>
    /// Sum and count of other students' valid answer times per question, from closed tests and practice (an aggregate in SQL).
    /// The student's own answers are left out: a slow student would otherwise pull the average towards themselves.
    /// </summary>
    private static async Task<Dictionary<int, (long Sum, int Count)>> LoadTimeSumsAsync(AppDbContext context, int studentId, List<int> questionIds,
        DateTime now, CancellationToken ct)
    {
        // A test session still running (status Started and inside its time limit) never counts (#526 exam integrity).
        var tests = await context.TestInstanceQuestions.AsNoTracking().WithRemovedWorksheetItems()
            .Where(r => questionIds.Contains(r.WorksheetQuestion.QuestionId) && r.WorksheetInstance.StudentId != studentId)
            .Where(r => r.SelectedAnswerId != null || r.AnswerPayload != null)
            .Where(r => r.TimeTaken > 0 && r.TimeTaken <= MaxValidSeconds)
            .Where(r => r.WorksheetInstance.Status != WorksheetInstanceStatus.Started
                || (r.WorksheetInstance.MaxDurationSeconds != null
                    && r.WorksheetInstance.StartTime.AddSeconds(r.WorksheetInstance.MaxDurationSeconds.Value) < now))
            .GroupBy(r => r.WorksheetQuestion.QuestionId)
            .Select(g => new { QuestionId = g.Key, Sum = g.Sum(r => (long)r.TimeTaken), Count = g.Count() })
            .ToListAsync(ct);
        var practice = await context.PracticeSessionQuestions.AsNoTracking()
            .Where(r => questionIds.Contains(r.QuestionId) && r.PracticeSession.StudentId != studentId && r.AnsweredAt != null && !r.IsSkipped)
            .Where(r => r.TimeTaken > 0 && r.TimeTaken <= MaxValidSeconds)
            .GroupBy(r => r.QuestionId)
            .Select(g => new { QuestionId = g.Key, Sum = g.Sum(r => (long)r.TimeTaken), Count = g.Count() })
            .ToListAsync(ct);
        return tests.Concat(practice)
            .GroupBy(x => x.QuestionId)
            .ToDictionary(g => g.Key, g => (g.Sum(x => x.Sum), g.Sum(x => x.Count)));
    }
}
