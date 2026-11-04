using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.TopicPerformance;

/// <summary>Öğrenci ve veli uçlarının ortak parametre kuralı: iki uç aynı isteği aynı şekilde kabul ya da reddeder.</summary>
public static class TopicPerformanceQuery
{
    public const int DefaultDays = 30;
    public const string DefaultGroupBy = "topic";
    public const int DefaultMinQuestions = 5;

    private const int MaxDays = 90;
    private const int MaxMinQuestions = 1000;

    private static readonly Dictionary<string, TopicPerformanceGroupBy> GroupByValues = new(StringComparer.OrdinalIgnoreCase)
    {
        ["topic"] = TopicPerformanceGroupBy.Topic,
        ["subtopic"] = TopicPerformanceGroupBy.SubTopic,
        ["difficulty"] = TopicPerformanceGroupBy.Difficulty,
        ["interactionType"] = TopicPerformanceGroupBy.InteractionType,
    };

    public sealed record Problem(string ErrorCode, string MessageKey);

    public static bool TryParse(int days, string groupBy, int minQuestions, out TopicPerformanceGroupBy grouping, out Problem? problem)
    {
        grouping = default;
        problem = days is < 1 or > MaxDays ? new Problem("InvalidDays", "student.topicPerformance.invalidDays")
            : !GroupByValues.TryGetValue(groupBy, out grouping) ? new Problem("InvalidGroupBy", "student.topicPerformance.invalidGroupBy")
            : minQuestions is < 1 or > MaxMinQuestions ? new Problem("InvalidMinQuestions", "student.topicPerformance.invalidMinQuestions")
            : null;
        return problem is null;
    }
}
