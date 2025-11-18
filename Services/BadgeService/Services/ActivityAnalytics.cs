using System;
using System.Collections.Generic;
using System.Linq;
using BadgeService.Entities;

namespace BadgeService.Services;

internal static class ActivityAnalytics
{
    internal static ActivitySummary Calculate(IEnumerable<StudentDailyActivity> activities)
    {
        if (activities == null)
        {
            return ActivitySummary.Empty;
        }

        var distinctDays = activities
            .Where(x => x != null && (x.QuestionCount > 0 || x.TotalTimeSeconds > 0 || x.TotalPoints > 0))
            .Select(x => x.ActivityDate.Date)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        if (distinctDays.Count == 0)
        {
            return ActivitySummary.Empty;
        }

        var daySet = distinctDays.ToHashSet();
        var today = DateTime.UtcNow.Date;
        var currentStreak = 0;
        var cursor = today;

        while (daySet.Contains(cursor))
        {
            currentStreak++;
            cursor = cursor.AddDays(-1);
        }

        var bestStreak = 0;
        var streak = 0;
        DateTime? previous = null;
        foreach (var day in distinctDays)
        {
            if (previous.HasValue && day == previous.Value.AddDays(1))
            {
                streak++;
            }
            else
            {
                streak = 1;
            }

            if (streak > bestStreak)
            {
                bestStreak = streak;
            }

            previous = day;
        }

        return new ActivitySummary(distinctDays.Count, currentStreak, bestStreak);
    }
}

internal sealed record ActivitySummary(int TotalActiveDays, int CurrentStreak, int BestStreak)
{
    internal static ActivitySummary Empty { get; } = new(0, 0, 0);
}
