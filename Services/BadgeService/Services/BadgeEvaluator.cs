using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BadgeService.Entities;
using BadgeService.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Services;

public class BadgeEvaluator
{
    private readonly BadgeDbContext _context;
    private readonly IHubContext<BadgeNotificationHub> _hub;

    public BadgeEvaluator(BadgeDbContext context, IHubContext<BadgeNotificationHub> hub)
    {
        _context = context;
        _hub = hub;
    }

    public async Task EvaluateAnswerSubmittedAsync(int userId, string clientId, CancellationToken cancellationToken = default)
    {
        var questionAggregate = await _context.StudentQuestionAggregates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

        var subjectAggregates = await _context.StudentSubjectAggregates
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);

        var dailyActivities = await _context.StudentDailyActivities
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);

        var activitySummary = ActivityAnalytics.Calculate(dailyActivities);

        var badgeDefinitions = await _context.BadgeDefinitions
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (badgeDefinitions.Count == 0)
        {
            return;
        }

        var earnedBadgeIds = (await _context.BadgeEarned
            .Where(x => x.UserId == userId)
            .Select(x => x.BadgeDefinitionId)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var progressEntities = await _context.StudentBadgeProgresses
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);

        var progressMap = progressEntities.ToDictionary(x => x.BadgeDefinitionId);
        var now = DateTime.UtcNow;
        var newlyEarned = new List<BadgeDefinition>();

        foreach (var definition in badgeDefinitions)
        {
            if (!BadgeRuleEvaluator.TryEvaluateRule(definition, questionAggregate, subjectAggregates, activitySummary, out var currentValue, out var targetValue))
            {
                continue;
            }

            if (!progressMap.TryGetValue(definition.Id, out var progress))
            {
                progress = new StudentBadgeProgress
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    BadgeDefinitionId = definition.Id,
                    TargetValue = targetValue
                };
                _context.StudentBadgeProgresses.Add(progress);
                progressMap[definition.Id] = progress;
            }
            else
            {
                progress.TargetValue = targetValue;
            }

            progress.CurrentValue = Math.Min(currentValue, progress.TargetValue);
            progress.IsCompleted = progress.CurrentValue >= progress.TargetValue;
            progress.LastUpdatedUtc = now;

            if (progress.IsCompleted && !earnedBadgeIds.Contains(definition.Id))
            {
                _context.BadgeEarned.Add(new BadgeEarned
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    BadgeDefinitionId = definition.Id,
                    EarnedDate = now
                });

                newlyEarned.Add(definition);
                earnedBadgeIds.Add(definition.Id);
            }
        }

        if (_context.ChangeTracker.HasChanges())
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        foreach (var badge in newlyEarned)
        {
            await _hub.Clients.User(clientId).SendAsync("BadgeEarned", new
            {
                BadgeName = badge.Name,
                Description = badge.Description,
                IconUrl = badge.IconUrl
            }, cancellationToken);
        }
    }
}
