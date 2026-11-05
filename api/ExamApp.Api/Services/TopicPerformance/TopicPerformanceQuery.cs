using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.TopicPerformance;

/// <summary>Öğrenci ve veli uçlarının ortak parametre kuralı: iki uç aynı isteği aynı şekilde kabul ya da reddeder.</summary>
public static class TopicPerformanceQuery
{
    public const int DefaultDays = 30;
    public const string DefaultGroupBy = "topic";
    public const int DefaultMinQuestions = 5;
    public const int DefaultTimelineWeeks = 12;

    /// <summary>
    /// Issue #767: veli ucunun alt sınırı. Daha küçük grup tek tek cevabın doğru / yanlış olduğunu gösterir; öğrencinin
    /// "Velim ne görüyor?" listesi bunu vaat etmez. Öğrencinin kendi ucu 1'i kabul eder.
    /// </summary>
    public const int ParentMinQuestions = DefaultMinQuestions;

    private const int MaxDays = 90;
    private const int MaxMinQuestions = 1000;
    private const int MaxTimelineWeeks = 26;

    private static readonly Dictionary<string, TopicPerformanceGroupBy> GroupByValues = new(StringComparer.OrdinalIgnoreCase)
    {
        ["topic"] = TopicPerformanceGroupBy.Topic,
        ["subtopic"] = TopicPerformanceGroupBy.SubTopic,
        ["difficulty"] = TopicPerformanceGroupBy.Difficulty,
        ["interactionType"] = TopicPerformanceGroupBy.InteractionType,
    };

    public sealed record Problem(string ErrorCode, string MessageKey);

    /// <param name="minimumMinQuestions">En küçük kabul edilen <paramref name="minQuestions"/>: öğrenci 1, veli <see cref="ParentMinQuestions"/>.</param>
    public static bool TryParse(int days, string groupBy, int minQuestions, out TopicPerformanceGroupBy grouping, out Problem? problem,
        int minimumMinQuestions = 1)
    {
        grouping = default;
        problem = days is < 1 or > MaxDays ? new Problem("InvalidDays", "student.topicPerformance.invalidDays")
            : !GroupByValues.TryGetValue(groupBy, out grouping) ? new Problem("InvalidGroupBy", "student.topicPerformance.invalidGroupBy")
            : minQuestions < minimumMinQuestions || minQuestions > MaxMinQuestions
                ? new Problem("InvalidMinQuestions", "student.topicPerformance.invalidMinQuestions")
            : null;
        return problem is null;
    }

    /// <summary>Issue #531: tam olarak bir konu ya da alt konu.</summary>
    public static bool TryParseTimeline(int? topicId, int? subTopicId, int weeks, out Problem? problem)
    {
        problem = (topicId is null) == (subTopicId is null) ? new Problem("InvalidTimelineTarget", "student.topicPerformance.invalidTimelineTarget")
            : weeks is < 1 or > MaxTimelineWeeks ? new Problem("InvalidWeeks", "student.topicPerformance.invalidWeeks")
            : null;
        return problem is null;
    }
}
