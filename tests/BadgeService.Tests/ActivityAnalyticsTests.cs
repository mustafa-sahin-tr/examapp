using BadgeService.Entities;
using BadgeService.Services;

namespace BadgeService.Tests;

public class ActivityAnalyticsTests
{
    private static StudentDailyActivity Day(DateTime date, int questions = 1)
        => new() { ActivityDate = date, QuestionCount = questions };

    [Fact]
    public void Null_input_is_empty()
        => ActivityAnalytics.Calculate(null!).ShouldBe(ActivitySummary.Empty);

    [Fact]
    public void Days_with_no_activity_do_not_count()
    {
        var summary = ActivityAnalytics.Calculate(new[]
        {
            new StudentDailyActivity { ActivityDate = DateTime.UtcNow.Date, QuestionCount = 0, TotalTimeSeconds = 0, TotalPoints = 0 },
        });
        summary.ShouldBe(ActivitySummary.Empty);
    }

    [Fact]
    public void Counts_distinct_active_days()
    {
        var d = DateTime.UtcNow.Date;
        var summary = ActivityAnalytics.Calculate(new[]
        {
            Day(d.AddDays(-10)), Day(d.AddDays(-10)), Day(d.AddDays(-3)), Day(d.AddDays(-1)),
        });
        summary.TotalActiveDays.ShouldBe(3);
    }

    [Fact]
    public void Current_streak_counts_back_from_today()
    {
        var d = DateTime.UtcNow.Date;
        var summary = ActivityAnalytics.Calculate(new[] { Day(d), Day(d.AddDays(-1)), Day(d.AddDays(-2)), Day(d.AddDays(-5)) });
        summary.CurrentStreak.ShouldBe(3);
    }

    [Fact]
    public void Current_streak_is_zero_when_today_is_inactive()
    {
        var d = DateTime.UtcNow.Date;
        var summary = ActivityAnalytics.Calculate(new[] { Day(d.AddDays(-1)), Day(d.AddDays(-2)) });
        summary.CurrentStreak.ShouldBe(0);
    }

    [Fact]
    public void Best_streak_is_the_longest_consecutive_run_anywhere()
    {
        var d = DateTime.UtcNow.Date;
        var summary = ActivityAnalytics.Calculate(new[]
        {
            // a 4-day run in the past, then a 1-day gap, then 2 days
            Day(d.AddDays(-20)), Day(d.AddDays(-19)), Day(d.AddDays(-18)), Day(d.AddDays(-17)),
            Day(d.AddDays(-10)), Day(d.AddDays(-9)),
        });
        summary.BestStreak.ShouldBe(4);
    }
}
