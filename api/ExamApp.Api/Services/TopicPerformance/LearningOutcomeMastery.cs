using System.ComponentModel.DataAnnotations;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Parents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.TopicPerformance;

/// <summary><c>LearningOutcomeMastery</c> ayarları (issue #696): seviye kuralları koda gömülü değil.</summary>
public sealed class LearningOutcomeMasteryOptions : IValidatableObject
{
    public const string SectionName = "LearningOutcomeMastery";

    /// <summary>Bu kadar günden eski cevap okunmaz: geçen seneki sonuç bugünkü seviyeyi söylemez, okunan satır da sınırlı kalır.</summary>
    [Range(1, 3650)]
    public int LookbackDays { get; set; } = 365;

    /// <summary>Seviye yalnız her kazanımın son bu kadar cevabından hesaplanır: öğrenci ilerleyince eski yanlışlar düşer.</summary>
    [Range(1, 1000)]
    public int RecentAnswers { get; set; } = 20;

    [Range(1, 100)]
    public int ProficientPct { get; set; } = 70;

    [Range(1, 100)]
    public int MasteredPct { get; set; } = 90;

    [Range(1, 1000)]
    public int MasteredMinAnswers { get; set; } = 10;

    /// <summary>Bundan az cevaba dayanan seviye "kesin değil" işaretlenir.</summary>
    [Range(1, 1000)]
    public int ReliableMinAnswers { get; set; } = 5;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MasteredPct < ProficientPct)
            yield return new ValidationResult("MasteredPct, ProficientPct'ten küçük olamaz.", [nameof(MasteredPct)]);
        if (MasteredMinAnswers > RecentAnswers)
            yield return new ValidationResult("MasteredMinAnswers, RecentAnswers'tan büyükse ustalaştı seviyesine hiç ulaşılamaz.", [nameof(MasteredMinAnswers)]);
    }
}

public interface ILearningOutcomeMasteryService
{
    /// <summary>Çağıran, öğrenciye erişim hakkını kendi denetler: bu servis kimlik bilmez, veli ve öğretmen uçları da kullanır.</summary>
    Task<LearningOutcomeMasteryDto> GetAsync(int studentId, DateTime to, int? subjectId, CurriculumVersion? curriculum, CancellationToken ct = default);
}

/// <summary>
/// Issue #696: kazanım başına seviye. Hangi cevabın sayıldığı <see cref="StudentAnswerReader"/>; boş cevap seviyeye girmez.
/// Kazanımı olan soru her kazanımında sayılır. Kazanımı olmayan soru alt konularına, alt konusu da yoksa konusuna düşer: eşleme
/// tamamlanana kadar rapor boş kalmasın. Öğrencinin sınıfındaki çözülmemiş kazanım ve alt konu "başlanmadı" görünür; kazanımı
/// tanımlı alt konu çözülmedikçe ayrı listelenmez, yoksa aynı içerik iki kez başlanmadı görünür.
/// Aynı sorunun yeniden çözümü yeni bir cevaptır: öğrencinin bugünkü durumunu o söyler.
/// </summary>
public sealed class LearningOutcomeMasteryService(AppDbContext db, IOptionsMonitor<LearningOutcomeMasteryOptions> options) : ILearningOutcomeMasteryService
{
    private readonly record struct Bucket(MasteryItemKind Kind, int Id);

    public async Task<LearningOutcomeMasteryDto> GetAsync(int studentId, DateTime to, int? subjectId, CurriculumVersion? curriculum, CancellationToken ct = default)
    {
        var rules = options.CurrentValue;
        var gradeId = await db.Students.AsNoTracking()
            .Where(s => s.Id == studentId)
            .Select(s => s.GradeId)
            .FirstOrDefaultAsync(ct);

        var answers = (await new StudentAnswerReader(db).ReadAsync(studentId, to.AddDays(-rules.LookbackDays), to, ct))
            .Where(a => a.Outcome != StudentAnswerOutcome.Blank)
            .ToList();
        var questionIds = answers.Select(a => a.Question.Id).Distinct().ToList();
        var outcomesByQuestion = (await db.QuestionLearningOutcomes.AsNoTracking()
                .Where(l => questionIds.Contains(l.QuestionId))
                .Select(l => new { l.QuestionId, l.LearningOutcomeId })
                .ToListAsync(ct))
            .ToLookup(l => l.QuestionId, l => l.LearningOutcomeId);

        var answersByBucket = answers
            .SelectMany(a => BucketsOf(a.Question, outcomesByQuestion[a.Question.Id]).Select(bucket => (Bucket: bucket, Answer: a)))
            .ToLookup(x => x.Bucket, x => x.Answer);
        int[] AnsweredIds(MasteryItemKind kind) => answersByBucket.Select(g => g.Key).Where(b => b.Kind == kind).Select(b => b.Id).ToArray();
        var answeredOutcomes = AnsweredIds(MasteryItemKind.Outcome);
        var answeredSubTopics = AnsweredIds(MasteryItemKind.SubTopic);
        var answeredTopics = AnsweredIds(MasteryItemKind.Topic);

        var outcomes = await db.LearningOutcomes.AsNoTracking()
            .Where(o => answeredOutcomes.Contains(o.Id) || o.GradeId == gradeId)
            .Where(o => curriculum == null || o.CurriculumVersion == curriculum)
            .Select(o => new
            {
                o.Id,
                o.Code,
                o.Description,
                o.CurriculumVersion,
                o.SubjectId,
                o.SubTopicId,
                SubTopicName = o.SubTopic != null ? o.SubTopic.Name : null,
                TopicId = o.SubTopic != null ? o.SubTopic.TopicId : (int?)null,
            })
            .ToListAsync(ct);
        var subTopicsWithOutcomes = outcomes.Select(o => o.SubTopicId).OfType<int>().ToHashSet();

        // Silinen alt konu ve konu da adıyla görünür: geçmiş cevabın konusu o gün neyse odur (konu performansıyla aynı).
        var subTopics = await db.SubTopics.AsNoTracking().IgnoreQueryFilters()
            .Where(s => answeredSubTopics.Contains(s.Id) || (!s.IsDeleted && !s.Topic.IsDeleted && s.Topic.GradeId == gradeId))
            .Select(s => new { s.Id, s.Name, s.TopicId })
            .ToListAsync(ct);
        subTopics = subTopics.Where(s => answeredSubTopics.Contains(s.Id) || !subTopicsWithOutcomes.Contains(s.Id)).ToList();

        var topicIds = answeredTopics
            .Concat(subTopics.Select(s => s.TopicId))
            .Concat(outcomes.Select(o => o.TopicId).OfType<int>())
            .Distinct().ToList();
        var topics = await db.Topics.AsNoTracking().IgnoreQueryFilters()
            .Where(t => topicIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => new { t.Name, t.SubjectId }, ct);
        var subjectIds = topics.Values.Select(t => t.SubjectId)
            .Concat(outcomes.Select(o => o.SubjectId).OfType<int>())
            .Concat(answers.Select(a => a.Question.SubjectId).OfType<int>())
            .Distinct().ToList();
        var subjects = await db.Subjects.AsNoTracking().IgnoreQueryFilters()
            .Where(s => subjectIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        LearningOutcomeMasteryItemDto Item(Bucket bucket, int? itemSubjectId, int? topicId)
        {
            var recent = answersByBucket[bucket].OrderByDescending(a => a.At).Take(rules.RecentAnswers).ToList();
            var correct = recent.Count(a => a.Outcome == StudentAnswerOutcome.Correct);
            var subject = itemSubjectId ?? (topicId is { } id && topics.TryGetValue(id, out var topic) ? (int?)topic.SubjectId : null);
            return new LearningOutcomeMasteryItemDto
            {
                Kind = bucket.Kind,
                SubjectId = subject,
                SubjectName = subject is { } s && subjects.TryGetValue(s, out var subjectName) ? subjectName : null,
                TopicId = topicId,
                TopicName = topicId is { } t && topics.TryGetValue(t, out var named) ? named.Name : null,
                Level = LevelOf(recent.Count, correct, rules),
                IsTentative = recent.Count > 0 && recent.Count < rules.ReliableMinAnswers,
                Answered = recent.Count,
                Correct = correct,
                AccuracyPct = recent.Count > 0 ? Math.Round(correct * 100m / recent.Count, 1) : null,
            };
        }

        var items = outcomes
            .Select(o =>
            {
                var item = Item(new Bucket(MasteryItemKind.Outcome, o.Id), o.SubjectId, o.TopicId);
                item.LearningOutcomeId = o.Id;
                item.Code = o.Code;
                item.Statement = o.Description;
                item.CurriculumVersion = o.CurriculumVersion;
                item.SubTopicId = o.SubTopicId;
                item.SubTopicName = o.SubTopicName;
                return item;
            })
            .Concat(subTopics.Select(s =>
            {
                var item = Item(new Bucket(MasteryItemKind.SubTopic, s.Id), null, s.TopicId);
                item.SubTopicId = s.Id;
                item.SubTopicName = s.Name;
                return item;
            }))
            .Concat(answeredTopics.Select(id => Item(new Bucket(MasteryItemKind.Topic, id), null, id)))
            .Where(item => subjectId is null || item.SubjectId == subjectId)
            .OrderBy(item => item.SubjectName, StringComparer.Ordinal)
            .ThenBy(item => item.TopicName, StringComparer.Ordinal)
            .ThenBy(item => item.SubTopicName, StringComparer.Ordinal)
            .ThenBy(item => item.Code, StringComparer.Ordinal)
            .ToList();

        return new LearningOutcomeMasteryDto
        {
            To = to,
            Items = items,
            UnclassifiedCount = answers
                .Where(a => !BucketsOf(a.Question, outcomesByQuestion[a.Question.Id]).Any() && (subjectId is null || a.Question.SubjectId == subjectId))
                .Select(a => a.Question.Id)
                .Distinct()
                .Count(),
            Rules = new LearningOutcomeMasteryRulesDto
            {
                RecentAnswers = rules.RecentAnswers,
                ProficientPct = rules.ProficientPct,
                MasteredPct = rules.MasteredPct,
                MasteredMinAnswers = rules.MasteredMinAnswers,
                ReliableMinAnswers = rules.ReliableMinAnswers,
            },
        };
    }

    private static IEnumerable<Bucket> BucketsOf(AnsweredQuestion question, IEnumerable<int> outcomeIds)
    {
        var outcomes = outcomeIds.Distinct().ToList();
        if (outcomes.Count > 0)
            return outcomes.Select(id => new Bucket(MasteryItemKind.Outcome, id));
        if (question.SubTopics.Count > 0)
            return question.SubTopics.Select(s => new Bucket(MasteryItemKind.SubTopic, s.Id));
        return question.TopicId is { } topicId ? [new Bucket(MasteryItemKind.Topic, topicId)] : [];
    }

    internal static LearningOutcomeMasteryLevel LevelOf(int answered, int correct, LearningOutcomeMasteryOptions rules)
    {
        if (answered == 0)
            return LearningOutcomeMasteryLevel.NotStarted;
        var accuracy = correct * 100m / answered;
        if (accuracy >= rules.MasteredPct && answered >= rules.MasteredMinAnswers)
            return LearningOutcomeMasteryLevel.Mastered;
        return accuracy >= rules.ProficientPct ? LearningOutcomeMasteryLevel.Proficient : LearningOutcomeMasteryLevel.Familiar;
    }
}

public interface IParentLearningOutcomeMasteryService
{
    /// <returns>Veli bu çocuğa Active bağlı değilse <c>null</c> (çağıran 404'e çevirir).</returns>
    Task<LearningOutcomeMasteryDto?> GetAsync(int parentUserId, int studentId, int? subjectId, CurriculumVersion? curriculum, CancellationToken ct = default);
}

/// <summary>Issue #696: veli uçlarının sırası: kapı, erişim kaydı, veri. Öğrenci kimliği kapıdan geçen bağlantıdan okunur.</summary>
public sealed class ParentLearningOutcomeMasteryService(
    IParentChildAccess access,
    IParentAccessAuditLog audit,
    ILearningOutcomeMasteryService mastery) : IParentLearningOutcomeMasteryService
{
    public async Task<LearningOutcomeMasteryDto?> GetAsync(int parentUserId, int studentId, int? subjectId, CurriculumVersion? curriculum, CancellationToken ct = default)
    {
        var grant = await access.EnsureActiveChildAsync(parentUserId, studentId, ct);
        if (grant is null)
            return null;

        // Veri okunmadan önce: okuma hata verse de erişim denemesi kayıtta kalır.
        await audit.RecordAsync(grant.ParentId, grant.StudentId, ParentAccessEndpoints.ChildLearningOutcomeMastery, ct: ct);

        // Saate kesilir: diğer veli uçlarıyla aynı kural (ParentTopicPerformanceService).
        var now = DateTime.UtcNow;
        var to = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
        return await mastery.GetAsync(grant.StudentId, to, subjectId, curriculum, ct);
    }
}
