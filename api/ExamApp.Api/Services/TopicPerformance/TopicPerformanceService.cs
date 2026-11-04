using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Services.Worksheets;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.TopicPerformance;

public interface ITopicPerformanceService
{
    /// <summary>Çağıran, öğrenciye erişim hakkını kendi denetler: bu servis kimlik bilmez, veli ve öğretmen uçları da kullanır.</summary>
    Task<TopicPerformanceDto> GetAsync(int studentId, DateTime from, DateTime to, int? subjectId, TopicPerformanceGroupBy groupBy, int minQuestions, CancellationToken ct = default);
}

/// <summary>
/// Issue #497: bitmiş test oturumları ve pratik cevaplarından konu, alt konu, zorluk ya da soru tipi başına performans.
/// Doğru, yanlış, boş kuralı <see cref="WorksheetScoring"/>: sonuç ekranıyla aynı sayılar. Süren test oturumu hiç okunmaz.
/// Etkinlik puansızdır, açık uçlu soru puanlanmaz; ikisi de dışarıda. Sonradan silinen test ya da madde cevabı düşürmez.
/// Silinen sorunun test cevabı yanlış sayılır (sonuç ekranıyla aynı), pratik cevabı kayıtlı sonucuyla kalır.
/// Doğruluk cevaplanan sorular üzerindendir: terk edilmiş testin boşları konuyu zayıf göstermez, ayrı sayılır.
/// Öğrenci sıfırlamasından önceki cevaplar okunmaz; sıfırlama pratik satırlarını silmediği için tarihle ayrılır.
/// </summary>
public sealed class TopicPerformanceService(AppDbContext db) : ITopicPerformanceService
{
    private const string DefaultInteractionType = "mcq";

    private enum Outcome { Correct, Wrong, Blank }

    private sealed record SubTopicFacts(int Id, string Name, int TopicId);

    private sealed record QuestionFacts(int Id, bool Deleted, int? SubjectId, int? TopicId, int? CorrectAnswerId, int Difficulty, string? InteractionType, List<SubTopicFacts> SubTopics);

    /// <summary>Cevabın kendisi; sonucu sorunun doğru şıkkı okunduktan sonra hesaplanır.</summary>
    private sealed record RawAnswer(int QuestionId, DateTime At, int TimeTaken, Func<QuestionFacts, Outcome> Grade);

    private readonly record struct GroupKey(int? TopicId, int? SubTopicId, string? Band, string? Interaction);

    private sealed record Attempt(QuestionFacts Question, bool Current, Outcome Outcome, int TimeTaken);

    public async Task<TopicPerformanceDto> GetAsync(int studentId, DateTime from, DateTime to, int? subjectId, TopicPerformanceGroupBy groupBy, int minQuestions, CancellationToken ct = default)
    {
        var previousFrom = from - (to - from);
        var resetAt = await db.Students.AsNoTracking()
            .Where(s => s.Id == studentId)
            .Select(s => s.ProgressResetAtUtc)
            .FirstOrDefaultAsync(ct);
        var earliest = resetAt > previousFrom ? resetAt.Value : previousFrom;

        var answers = await ReadTestAnswersAsync(studentId, earliest, to, ct);
        answers.AddRange(await ReadPracticeAnswersAsync(studentId, earliest, to, ct));

        var questions = await ReadQuestionsAsync(answers.Select(a => a.QuestionId).Distinct().ToList(), ct);
        var topicIds = questions.Values.Select(q => q.TopicId).OfType<int>()
            .Concat(questions.Values.SelectMany(q => q.SubTopics).Select(s => s.TopicId))
            .Distinct().ToList();
        // Silinen konu da adıyla görünür: geçmiş cevabın konusu o gün neyse odur.
        var topics = await db.Topics.AsNoTracking().IgnoreQueryFilters()
            .Where(t => topicIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Name, t.SubjectId })
            .ToDictionaryAsync(t => t.Id, ct);
        var subjectIds = topics.Values.Select(t => t.SubjectId).Concat(questions.Values.Select(q => q.SubjectId).OfType<int>()).Distinct().ToList();
        var subjects = await db.Subjects.AsNoTracking().IgnoreQueryFilters()
            .Where(s => subjectIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        int? TopicOf(QuestionFacts q) => q.TopicId ?? q.SubTopics.OrderBy(s => s.Id).Select(s => (int?)s.TopicId).FirstOrDefault();
        int? SubjectOf(QuestionFacts q) => TopicOf(q) is { } topicId && topics.TryGetValue(topicId, out var topic) ? topic.SubjectId : q.SubjectId;

        var attempts = answers
            .Where(a => questions.ContainsKey(a.QuestionId))
            .Select(a => (Answer: a, Question: questions[a.QuestionId]))
            .Where(x => x.Question.InteractionType != OpenTextGrader.Type)
            .Select(x => new Attempt(x.Question, x.Answer.At >= from, x.Answer.Grade(x.Question), x.Answer.TimeTaken))
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
                .Where(a => a.Current && a.Outcome != Outcome.Blank && TopicOf(a.Question) is null && (subjectId is null || SubjectOf(a.Question) == subjectId))
                .Select(a => a.Question.Id)
                .Distinct()
                .Count(),
        };
    }

    private static IEnumerable<GroupKey> KeysOf(QuestionFacts question, TopicPerformanceGroupBy groupBy, Func<QuestionFacts, int?> topicOf)
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
        var answered = current.Where(a => a.Outcome != Outcome.Blank).ToList();
        // Bu dönemde cevaplanmayan grup hakkında söylenecek bir şey yok: yalnız önceki pencerede kalan ya da tümü boş olan.
        if (answered.Count == 0)
            return null;

        var previous = attempts.Where(a => !a.Current && a.Outcome != Outcome.Blank).ToList();
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
            Correct = current.Count(a => a.Outcome == Outcome.Correct),
            Wrong = current.Count(a => a.Outcome == Outcome.Wrong),
            Blank = current.Count(a => a.Outcome == Outcome.Blank),
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
        attempts.Count(a => a.Outcome == Outcome.Correct) * 100m / attempts.Count;

    private async Task<List<RawAnswer>> ReadTestAnswersAsync(int studentId, DateTime from, DateTime to, CancellationToken ct)
    {
        // Süre dolup kapanmamış oturumun EndTime'ı boştur: başlangıcı süre sınırı kadar geriden aranır.
        var earliestStart = from.AddSeconds(-WorksheetDurationLimits.MaxSeconds);
        var instances = await db.TestInstances.AsNoTracking()
            .Where(i => i.StudentId == studentId)
            // Gezinme değil: silinen testin cevapları öğrencinin geçmişinden düşmesin.
            .Where(i => !db.Worksheets.IgnoreQueryFilters().Any(w => w.Id == i.WorksheetId && w.Kind == WorksheetKind.Activity))
            .Where(i => i.EndTime >= from || (i.EndTime == null && i.StartTime >= earliestStart))
            .Select(i => new { i.Id, i.Status, i.StartTime, i.EndTime, i.MaxDurationSeconds })
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var finishedAt = instances
            // Sınav bütünlüğü: süren oturumun cevabı öğrenciye de asistana da doğrusunu söylemez.
            .Where(i => TestTimeLimit.EffectiveStatus(i.Status, i.StartTime, i.MaxDurationSeconds, now) != WorksheetInstanceStatus.Started)
            .Select(i => (i.Id, At: i.EndTime ?? TestTimeLimit.EndsAt(i.StartTime, i.MaxDurationSeconds) ?? i.StartTime))
            .Where(x => x.At >= from && x.At < to)
            .ToDictionary(x => x.Id, x => x.At);
        var ids = finishedAt.Keys.ToList();

        // Oturum satırından okuma kuralı (#684): sonradan kaldırılan madde de sayılır.
        var rows = await db.TestInstanceQuestions.AsNoTracking().WithRemovedWorksheetItems()
            .Where(r => ids.Contains(r.WorksheetInstanceId))
            .Select(r => new { r.WorksheetInstanceId, r.WorksheetQuestion.QuestionId, r.SelectedAnswerId, HasPayload = r.AnswerPayload != null, r.IsCorrect, r.TimeTaken })
            .ToListAsync(ct);

        return rows.Select(r => new RawAnswer(r.QuestionId, finishedAt[r.WorksheetInstanceId], r.TimeTaken, question =>
            WorksheetScoring.IsBlank(r.SelectedAnswerId, r.HasPayload) ? Outcome.Blank
            : WorksheetScoring.IsCorrect(r.SelectedAnswerId, question.CorrectAnswerId, r.HasPayload, r.IsCorrect, questionKnown: !question.Deleted) ? Outcome.Correct
            : Outcome.Wrong)).ToList();
    }

    private async Task<List<RawAnswer>> ReadPracticeAnswersAsync(int studentId, DateTime from, DateTime to, CancellationToken ct)
    {
        // Gösterilip cevaplanmamış pratik sorusu sayılmaz; yalnız atlanan boştur.
        var rows = await db.PracticeSessionQuestions.AsNoTracking()
            .Where(r => r.PracticeSession.StudentId == studentId && (r.PracticeSession.EndTime == null || r.PracticeSession.EndTime >= from))
            .Where(r => r.AnsweredAt != null || r.IsSkipped)
            .Where(r => (r.AnsweredAt ?? r.ShownAt) >= from && (r.AnsweredAt ?? r.ShownAt) < to)
            .Select(r => new { r.QuestionId, At = r.AnsweredAt ?? r.ShownAt, r.IsSkipped, r.IsCorrect, r.TimeTaken })
            .ToListAsync(ct);

        return rows.Select(r => new RawAnswer(r.QuestionId, r.At, r.TimeTaken, _ =>
            r.IsSkipped ? Outcome.Blank : r.IsCorrect ? Outcome.Correct : Outcome.Wrong)).ToList();
    }

    private async Task<Dictionary<int, QuestionFacts>> ReadQuestionsAsync(List<int> ids, CancellationToken ct)
    {
        // Silinen soru da okunur: boş değilse yanlış sayılır (WorksheetScoring), konusu yine bellidir.
        var rows = await db.Questions.AsNoTracking().IgnoreQueryFilters()
            .Where(q => ids.Contains(q.Id))
            .Select(q => new
            {
                q.Id,
                q.IsDeleted,
                q.SubjectId,
                q.TopicId,
                q.CorrectAnswerId,
                q.DifficultyLevel,
                q.InteractionType,
                SubTopics = q.QuestionSubTopics.Where(s => !s.IsDeleted).Select(s => new SubTopicFacts(s.SubTopicId, s.SubTopic.Name, s.SubTopic.TopicId)).ToList(),
            })
            .AsSplitQuery()
            .ToListAsync(ct);

        return rows.ToDictionary(q => q.Id, q => new QuestionFacts(q.Id, q.IsDeleted, q.SubjectId, q.TopicId, q.CorrectAnswerId, q.DifficultyLevel, q.InteractionType, q.SubTopics.DistinctBy(s => s.Id).ToList()));
    }
}
