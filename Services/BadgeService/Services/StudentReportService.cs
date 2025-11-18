using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BadgeService.Entities;
using BadgeService.Models;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Services;

public class StudentReportService
{
    private readonly BadgeDbContext _context;

    public StudentReportService(BadgeDbContext context)
    {
        _context = context;
    }

    public async Task<BadgeProgressReportDto?> GetBadgeProgressAsync(int userId, CancellationToken cancellationToken = default)
    {
        var summary = await _context.StudentQuestionAggregates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

        if (summary == null)
        {
            return null;
        }

        var badgeProgress = await _context.StudentBadgeProgresses
            .AsNoTracking()
            .Include(x => x.BadgeDefinition)
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.BadgeDefinition.PathKey == null)
            .ThenBy(x => x.BadgeDefinition.PathName)
            .ThenBy(x => x.BadgeDefinition.PathOrder)
            .ThenBy(x => x.BadgeDefinition.Name)
            .Select(x => new BadgeProgressItemDto
            {
                BadgeDefinitionId = x.BadgeDefinitionId,
                Name = x.BadgeDefinition.Name,
                Description = x.BadgeDefinition.Description,
                IconUrl = x.BadgeDefinition.IconUrl,
                PathKey = x.BadgeDefinition.PathKey,
                PathName = x.BadgeDefinition.PathName,
                PathOrder = x.BadgeDefinition.PathOrder,
                CurrentValue = x.CurrentValue,
                TargetValue = x.TargetValue,
                IsCompleted = x.IsCompleted,
                EarnedDateUtc = _context.BadgeEarned
                    .Where(be => be.UserId == userId && be.BadgeDefinitionId == x.BadgeDefinitionId)
                    .Select(be => (DateTime?)be.EarnedDate)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var dailyActivities = await _context.StudentDailyActivities
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);

        var activitySummary = ActivityAnalytics.Calculate(dailyActivities);

        var subjects = await _context.StudentSubjectAggregates
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.TotalQuestions)
            .Select(x => new SubjectAggregateDto
            {
                SubjectId = x.SubjectId,
                SubjectName = x.SubjectName,
                TotalQuestions = x.TotalQuestions,
                CorrectQuestions = x.CorrectQuestions,
                AccuracyPercentage = x.TotalQuestions == 0 ? 0 : Math.Round((double)x.CorrectQuestions / x.TotalQuestions * 100, 2),
                TotalPoints = x.TotalPoints,
                TotalTimeSeconds = x.TotalTimeSeconds
            })
            .ToListAsync(cancellationToken);

        return new BadgeProgressReportDto
        {
            Summary = new StudentSummaryDto
            {
                UserId = summary.UserId,
                TotalQuestions = summary.TotalQuestions,
                CorrectQuestions = summary.CorrectQuestions,
                AccuracyPercentage = summary.TotalQuestions == 0 ? 0 : Math.Round((double)summary.CorrectQuestions / summary.TotalQuestions * 100, 2),
                TotalPoints = summary.TotalPoints,
                CurrentCorrectStreak = summary.CurrentCorrectStreak,
                BestCorrectStreak = summary.BestCorrectStreak,
                TotalTimeSeconds = summary.TotalTimeSeconds,
                TotalActiveDays = activitySummary.TotalActiveDays,
                CurrentActivityStreak = activitySummary.CurrentStreak,
                BestActivityStreak = activitySummary.BestStreak,
                LastAnsweredAtUtc = summary.LastAnsweredAtUtc,
                LastUpdatedUtc = summary.LastUpdatedUtc
            },
            BadgeProgress = badgeProgress,
            SubjectBreakdown = subjects
        };
    }

    public async Task<ActivityReportDto> GetActivityReportAsync(int userId, DateTime? startUtc, DateTime? endUtc, CancellationToken cancellationToken = default)
    {
        var end = endUtc?.Date ?? DateTime.UtcNow.Date;
        var start = startUtc?.Date ?? end.AddDays(-29);

        if (start > end)
        {
            (start, end) = (end, start);
        }

        var allActivities = await _context.StudentDailyActivities
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);

        var activitySummary = ActivityAnalytics.Calculate(allActivities);

        var days = allActivities
            .Where(x => x.ActivityDate >= start && x.ActivityDate <= end)
            .OrderBy(x => x.ActivityDate)
            .Select(x => new ActivityDayDto
            {
                DateUtc = x.ActivityDate,
                QuestionCount = x.QuestionCount,
                CorrectCount = x.CorrectCount,
                TotalTimeSeconds = x.TotalTimeSeconds,
                TotalPoints = x.TotalPoints,
                ActivityScore = x.ActivityScore
            })
            .ToList();

        return new ActivityReportDto
        {
            UserId = userId,
            StartDateUtc = start,
            EndDateUtc = end,
            TotalActiveDays = activitySummary.TotalActiveDays,
            CurrentStreakDays = activitySummary.CurrentStreak,
            BestStreakDays = activitySummary.BestStreak,
            Days = days
        };
    }
}
