using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Services.Parents;

namespace ExamApp.Api.Services.TopicPerformance;

public interface ITopicProgressTimelineService
{
    /// <summary>Çağıran, öğrenciye erişim hakkını kendi denetler. <paramref name="subTopicId"/> doluysa ona, değilse <paramref name="topicId"/>'ye bakar.</summary>
    Task<TopicProgressTimelineDto> GetAsync(int studentId, int? topicId, int? subTopicId, int weeks, CancellationToken ct = default);
}

/// <summary>
/// Issue #531: bir konunun ya da alt konunun haftalık doğruluğu. Hangi cevabın sayıldığı <see cref="StudentAnswerReader"/>, konu eşlemesi
/// konu performansıyla aynı. Hafta Pazartesi başlar ve yerel takvime göredir; içinde bulunulan hafta dahildir, yani henüz bitmemiş olabilir.
/// Test cevabı oturumun bittiği haftaya sayılır. Doğruluk cevaplanan sorular üzerindendir; yalnız boş kalan hafta cevapsız sayılır.
/// </summary>
public sealed class TopicProgressTimelineService(AppDbContext db, ILocalDayCalendar calendar) : ITopicProgressTimelineService
{
    /// <summary>Bu sayının altındaki hafta tek başına bir eğilim söylemez; ekranda soluk gösterilir.</summary>
    private const int LowSampleAnswers = 3;

    public async Task<TopicProgressTimelineDto> GetAsync(int studentId, int? topicId, int? subTopicId, int weeks, CancellationToken ct = default)
    {
        var thisWeek = ParentDashboardService.StartOfWeek(calendar.Today);
        var firstWeek = thisWeek.AddDays(-7 * (weeks - 1));
        var answers = await new StudentAnswerReader(db).ReadAsync(
            studentId, calendar.StartOfDayUtc(firstWeek), calendar.StartOfDayUtc(thisWeek.AddDays(7)), ct);

        var counts = answers
            .Where(a => a.Outcome != StudentAnswerOutcome.Blank)
            .Where(a => subTopicId is { } id ? a.Question.SubTopics.Any(s => s.Id == id) : a.Question.EffectiveTopicId == topicId)
            .GroupBy(a => ParentDashboardService.StartOfWeek(DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(a.At, calendar.TimeZone))))
            .ToDictionary(g => g.Key, g => (Answered: g.Count(), Correct: g.Count(a => a.Outcome == StudentAnswerOutcome.Correct)));

        return new TopicProgressTimelineDto
        {
            TopicId = subTopicId is null ? topicId : null,
            SubTopicId = subTopicId,
            Weeks = Enumerable.Range(0, weeks).Select(i =>
            {
                var weekStart = firstWeek.AddDays(7 * i);
                var (answered, correct) = counts.GetValueOrDefault(weekStart);
                return new TopicProgressWeekDto
                {
                    WeekStart = weekStart,
                    Answered = answered,
                    Correct = correct,
                    AccuracyPct = answered > 0 ? Math.Round(correct * 100m / answered, 1) : null,
                    LowSample = answered is > 0 and < LowSampleAnswers,
                };
            }).ToList(),
        };
    }
}
