using System.ComponentModel.DataAnnotations;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Services.TopicPerformance;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Practice;

/// <summary>Issue #534: karışık mini denemenin boyutu ve kaynak payları.</summary>
public sealed class MixedReviewOptions : IValidatableObject
{
    public const string SectionName = "PracticeMixedReview";

    [Range(1, 50)]
    public int Count { get; set; } = 15;

    [Range(0, 100)]
    public int DuePct { get; set; } = 40;

    [Range(0, 100)]
    public int MistakePct { get; set; } = 30;

    [Range(0, 100)]
    public int WeakTopicPct { get; set; } = 30;

    [Range(1, 90)]
    public int WeakTopicDays { get; set; } = 30;

    [Range(1, 100)]
    public int WeakTopicMinQuestions { get; set; } = 5;

    /// <summary>Zayıf konular kartıyla (#521) aynı eşik.</summary>
    [Range(1, 100)]
    public int WeakTopicMaxAccuracyPct { get; set; } = 70;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DuePct + MistakePct + WeakTopicPct != 100)
            yield return new ValidationResult("Shares must add up to 100.", [nameof(DuePct), nameof(MistakePct), nameof(WeakTopicPct)]);
    }
}

/// <summary>
/// Issue #534: üç kaynaktan sabit listeli oturum. "Zor sorular" kaynağı (#526) henüz yok; yerine öğrencinin yanlış defteri
/// kullanılır. Zayıf konudan yalnız hiç cevaplanmamış soru alınır. Bütün kaynaklar soruyu öğrenciye açmadan önce engelli listeyi
/// (bitmemiş test ve atama, bugünün günlük seti) düşer: oturum doğru şıkkı gösterdiği için sorular önceden çözdürülmüş olurdu.
/// </summary>
internal static class MixedReviewQueries
{
    /// <summary>Bellek sınırı: seçim bu kadar aday içinden rastgele yapılır.</summary>
    private const int MaxWeakTopicCandidates = 500;

    internal static async Task<List<int>> LoadAsync(
        AppDbContext context,
        ILocalDayCalendar calendar,
        ITopicPerformanceService topicPerformance,
        MixedReviewOptions options,
        StudentProfileDto student,
        IReadOnlyCollection<int> subjectIds,
        int count,
        CancellationToken ct)
    {
        var due = await DueReviewQueries.LoadAsync(context, calendar, student, subjectIds, ct);
        var mistakes = await MistakeNotebookQueries.LoadAsync(context, calendar, student, subjectIds, Array.Empty<int>(), ct);
        var weak = await LoadWeakTopicQuestionIdsAsync(context, calendar, topicPerformance, options, student, subjectIds, ct);

        return MixedReviewComposer.Compose(
            due.Select(d => d.QuestionId).ToList(),
            mistakes.Select(m => m.QuestionId).ToList(),
            weak,
            count,
            options,
            Random.Shared);
    }

    private static async Task<List<int>> LoadWeakTopicQuestionIdsAsync(
        AppDbContext context,
        ILocalDayCalendar calendar,
        ITopicPerformanceService topicPerformance,
        MixedReviewOptions options,
        StudentProfileDto student,
        IReadOnlyCollection<int> subjectIds,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var performance = await topicPerformance.GetAsync(
            student.Id, now.AddDays(-options.WeakTopicDays), now, subjectIds.Count == 1 ? subjectIds.First() : null,
            TopicPerformanceGroupBy.Topic, options.WeakTopicMinQuestions, ct);
        var topicIds = performance.Items
            .Where(item => item.TopicId.HasValue && item.AccuracyPct < options.WeakTopicMaxAccuracyPct)
            .Select(item => item.TopicId!.Value)
            .ToList();
        if (topicIds.Count == 0)
            return [];

        var answered = (await DueReviewQueries.LoadAttemptsAsync(context, student.Id, ct)).Keys.ToList();
        var blocked = await DueReviewQueries.LoadBlockedAsync(context, calendar, student, ct);
        var ids = await PracticeQuestionPool.Build(context, student.GradeId!.Value, subjectIds, topicIds)
            .Where(q => !answered.Contains(q.Id))
            .OrderBy(q => q.Id)
            .Select(q => q.Id)
            .Take(MaxWeakTopicCandidates)
            .ToListAsync(ct);

        var candidates = ids.Where(id => !blocked.Contains(id)).ToArray();
        Random.Shared.Shuffle(candidates);
        return candidates.ToList();
    }
}

internal static class MixedReviewComposer
{
    /// <summary>
    /// Her kaynak payını kendi sırasıyla doldurur, aynı soru iki kez girmez. Eksik kalan pay diğer kaynaklardan tamamlanır;
    /// kaynaklar yetmezse liste kısa kalır. Sıra karıştırılır: amaç konuların iç içe gelmesidir.
    /// </summary>
    internal static List<int> Compose(
        IReadOnlyList<int> due, IReadOnlyList<int> mistakes, IReadOnlyList<int> weak, int count, MixedReviewOptions options, Random random)
    {
        var selected = new List<int>(count);
        var seen = new HashSet<int>();

        void Take(IReadOnlyList<int> source, int limit)
        {
            var taken = 0;
            foreach (var id in source)
            {
                if (taken == limit || selected.Count == count)
                    return;
                if (seen.Add(id))
                {
                    selected.Add(id);
                    taken++;
                }
            }
        }

        var dueQuota = count * options.DuePct / 100;
        var mistakeQuota = count * options.MistakePct / 100;
        Take(due, dueQuota);
        Take(mistakes, mistakeQuota);
        Take(weak, count - dueQuota - mistakeQuota);
        Take(due, count);
        Take(mistakes, count);
        Take(weak, count);

        var result = selected.ToArray();
        random.Shuffle(result);
        return result.ToList();
    }
}
