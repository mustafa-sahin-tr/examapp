using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.TopicPerformance;

public interface ITopicPerformanceService
{
    /// <summary>Çağıran, öğrenciye erişim hakkını kendi denetler: bu servis kimlik bilmez, veli ve öğretmen uçları da kullanır.</summary>
    Task<TopicPerformanceDto> GetAsync(int studentId, DateTime from, DateTime to, int? subjectId, TopicPerformanceGroupBy groupBy, int minQuestions, CancellationToken ct = default);
}

/// <summary>
/// Issue #497: konu, alt konu, zorluk ya da soru tipi başına performans. Hangi cevabın sayıldığı <see cref="StudentAnswerReader"/>.
/// Doğruluk cevaplanan sorular üzerindendir: terk edilmiş testin boşları konuyu zayıf göstermez, ayrı sayılır.
/// </summary>
public sealed class TopicPerformanceService(AppDbContext db) : ITopicPerformanceService
{
    private const string DefaultInteractionType = "mcq";

    private readonly record struct GroupKey(int? TopicId, int? SubTopicId, string? Band, string? Interaction);

    private sealed record Attempt(AnsweredQuestion Question, bool Current, StudentAnswerOutcome Outcome, int TimeTaken);

    public async Task<TopicPerformanceDto> GetAsync(int studentId, DateTime from, DateTime to, int? subjectId, TopicPerformanceGroupBy groupBy, int minQuestions, CancellationToken ct = default)
    {
        var previousFrom = from - (to - from);
        var answers = await new StudentAnswerReader(db).ReadAsync(studentId, previousFrom, to, ct);
        var questions = answers.Select(a => a.Question).Distinct().ToList();
        var topicIds = questions.Select(q => q.TopicId).OfType<int>()
            .Concat(questions.SelectMany(q => q.SubTopics).Select(s => s.TopicId))
            .Distinct().ToList();
        // Silinen konu da adıyla görünür: geçmiş cevabın konusu o gün neyse odur.
        var topics = await db.Topics.AsNoTracking().IgnoreQueryFilters()
            .Where(t => topicIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Name, t.SubjectId })
            .ToDictionaryAsync(t => t.Id, ct);
        var subjectIds = topics.Values.Select(t => t.SubjectId).Concat(questions.Select(q => q.SubjectId).OfType<int>()).Distinct().ToList();
        var subjects = await db.Subjects.AsNoTracking().IgnoreQueryFilters()
            .Where(s => subjectIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        int? TopicOf(AnsweredQuestion q) => q.EffectiveTopicId;
        int? SubjectOf(AnsweredQuestion q) => TopicOf(q) is { } topicId && topics.TryGetValue(topicId, out var topic) ? topic.SubjectId : q.SubjectId;

        var attempts = answers
            .Select(a => new Attempt(a.Question, a.At >= from, a.Outcome, a.TimeTaken))
            .ToList();

        var items = attempts
            .SelectMany(a => KeysOf(a.Question, groupBy, TopicOf).Select(key => (Key: key, Attempt: a)))
            // Ders anahtarın konusundan okunur: başka dersin alt konusu da işaretli soru o dersin listesine sızmaz.
            .Where(x => subjectId is null || (x.Key.TopicId is { } id && topics.TryGetValue(id, out var keyTopic) ? keyTopic.SubjectId : SubjectOf(x.Attempt.Question)) == subjectId)
            .GroupBy(x => x.Key, x => x.Attempt)
            .Select(group => BuildItem(group.Key, group.ToList(), key => key.TopicId is { } id && topics.TryGetValue(id, out var t) ? (id, t.Name, t.SubjectId) : null, subjects))
            .OfType<TopicPerformanceItemDto>()
            // Eşik cevaplanan soruya bakar: boş satır doğruluğa kanıt olmaz.
            .Where(item => item.Correct + item.Wrong >= minQuestions)
            .OrderBy(item => item.AccuracyPct)
            .ThenByDescending(item => item.Total)
            .ThenBy(item => item.TopicName)
            .ThenBy(item => item.SubTopicName)
            .ThenBy(item => item.DifficultyBand)
            .ThenBy(item => item.InteractionType)
            .ToList();

        return new TopicPerformanceDto
        {
            From = from,
            To = to,
            GroupBy = groupBy,
            Items = items,
            UnclassifiedCount = attempts
                .Where(a => a.Current && a.Outcome != StudentAnswerOutcome.Blank && TopicOf(a.Question) is null && (subjectId is null || SubjectOf(a.Question) == subjectId))
                .Select(a => a.Question.Id)
                .Distinct()
                .Count(),
        };
    }

    private static IEnumerable<GroupKey> KeysOf(AnsweredQuestion question, TopicPerformanceGroupBy groupBy, Func<AnsweredQuestion, int?> topicOf)
    {
        switch (groupBy)
        {
            case TopicPerformanceGroupBy.Difficulty:
                yield return new GroupKey(null, null, DifficultyBands.Of(question.Difficulty), null);
                break;
            case TopicPerformanceGroupBy.InteractionType:
                yield return new GroupKey(null, null, null, question.InteractionType ?? DefaultInteractionType);
                break;
            case TopicPerformanceGroupBy.SubTopic when question.SubTopics.Count > 0:
                // Birden çok alt konulu soru her birinde sayılır: öğrenci o alt konuyu da çözmüş olur.
                foreach (var subTopic in question.SubTopics)
                    yield return new GroupKey(subTopic.TopicId, subTopic.Id, null, null);
                break;
            default:
                if (topicOf(question) is { } topicId)
                    yield return new GroupKey(topicId, null, null, null);
                break;
        }
    }

    private static TopicPerformanceItemDto? BuildItem(GroupKey key, List<Attempt> attempts, Func<GroupKey, (int Id, string Name, int SubjectId)?> topicOf, Dictionary<int, string> subjects)
    {
        var current = attempts.Where(a => a.Current).ToList();
        var answered = current.Where(a => a.Outcome != StudentAnswerOutcome.Blank).ToList();
        // Bu dönemde cevaplanmayan grup hakkında söylenecek bir şey yok: yalnız önceki pencerede kalan ya da tümü boş olan.
        if (answered.Count == 0)
            return null;

        var previous = attempts.Where(a => !a.Current && a.Outcome != StudentAnswerOutcome.Blank).ToList();
        var topic = topicOf(key);
        var accuracy = Accuracy(answered);
        var previousAccuracy = previous.Count > 0 ? Accuracy(previous) : (decimal?)null;

        return new TopicPerformanceItemDto
        {
            SubjectId = topic?.SubjectId,
            SubjectName = topic is { } t && subjects.TryGetValue(t.SubjectId, out var subjectName) ? subjectName : null,
            TopicId = topic?.Id,
            TopicName = topic?.Name,
            SubTopicId = key.SubTopicId,
            SubTopicName = key.SubTopicId is { } id ? current.SelectMany(a => a.Question.SubTopics).First(s => s.Id == id).Name : null,
            DifficultyBand = key.Band,
            InteractionType = key.Interaction,
            Correct = current.Count(a => a.Outcome == StudentAnswerOutcome.Correct),
            Wrong = current.Count(a => a.Outcome == StudentAnswerOutcome.Wrong),
            Blank = current.Count(a => a.Outcome == StudentAnswerOutcome.Blank),
            Total = current.Count,
            AccuracyPct = Math.Round(accuracy, 1),
            AvgTimeSec = Math.Round((decimal)answered.Sum(a => a.TimeTaken) / answered.Count, 1),
            PrevAnswered = previous.Count,
            PrevAccuracyPct = previousAccuracy is { } shown ? Math.Round(shown, 1) : null,
            // Yuvarlanmamış değerlerden: 33,3 ile 66,7'nin farkı −33,3'tür, −33,4 değil.
            Trend = previousAccuracy is { } before ? Math.Round(accuracy - before, 1) : null,
        };
    }

    private static decimal Accuracy(List<Attempt> attempts) =>
        attempts.Count(a => a.Outcome == StudentAnswerOutcome.Correct) * 100m / attempts.Count;
}
