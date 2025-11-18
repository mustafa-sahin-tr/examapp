using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
            if (!TryEvaluateRule(definition, questionAggregate, subjectAggregates, activitySummary, out var currentValue, out var targetValue))
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

    private static bool TryEvaluateRule(
        BadgeDefinition definition,
        StudentQuestionAggregate? questionAggregate,
        IReadOnlyList<StudentSubjectAggregate> subjectAggregates,
        ActivitySummary activitySummary,
        out int currentValue,
        out int targetValue)
    {
        currentValue = 0;
        targetValue = 0;

        if (string.IsNullOrWhiteSpace(definition.RuleType) || string.IsNullOrWhiteSpace(definition.RuleConfigJson))
        {
            return false;
        }

        JsonElement config;
        try
        {
            using var document = JsonDocument.Parse(definition.RuleConfigJson);
            config = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return false;
        }

        switch (definition.RuleType.Trim().ToLowerInvariant())
        {
            case "answercount":
                if (!TryReadInt(config, out targetValue, "target", "count"))
                {
                    return false;
                }
                currentValue = questionAggregate?.TotalQuestions ?? 0;
                break;

            case "correctstreak":
                if (!TryReadInt(config, out targetValue, "target", "streak"))
                {
                    return false;
                }
                currentValue = questionAggregate?.BestCorrectStreak ?? 0;
                break;

            case "totalstudytimeminutes":
                if (!TryReadInt(config, out targetValue, "target", "minutes", "targetMinutes"))
                {
                    return false;
                }
                currentValue = (int)Math.Floor((questionAggregate?.TotalTimeSeconds ?? 0) / 60d);
                break;

            case "totalcorrectanswers":
                if (!TryReadInt(config, out targetValue, "target", "count", "correct"))
                {
                    return false;
                }
                currentValue = questionAggregate?.CorrectQuestions ?? 0;
                break;

            case "subjectanswercount":
                if (!TryReadInt(config, out targetValue, "target", "count"))
                {
                    return false;
                }

                if (!TryGetSubjectCriteria(config, out var answerCountSubject))
                {
                    return false;
                }

                currentValue = FindSubjectAggregate(subjectAggregates, answerCountSubject)?.TotalQuestions ?? 0;
                break;

            case "subjectcorrectcount":
                if (!TryReadInt(config, out targetValue, "target", "count", "correct"))
                {
                    return false;
                }

                if (!TryGetSubjectCriteria(config, out var correctSubject))
                {
                    return false;
                }

                currentValue = FindSubjectAggregate(subjectAggregates, correctSubject)?.CorrectQuestions ?? 0;
                break;

            case "subjectstudytimeminutes":
                if (!TryReadInt(config, out targetValue, "target", "minutes", "targetMinutes"))
                {
                    return false;
                }

                if (!TryGetSubjectCriteria(config, out var timeSubject))
                {
                    return false;
                }

                var subjectForTime = FindSubjectAggregate(subjectAggregates, timeSubject);
                currentValue = subjectForTime == null ? 0 : (int)Math.Floor(subjectForTime.TotalTimeSeconds / 60d);
                break;

            case "activedays":
                if (!TryReadInt(config, out targetValue, "target", "days"))
                {
                    return false;
                }
                currentValue = activitySummary.TotalActiveDays;
                break;

            case "dailystreak":
            case "studystreak":
            case "activitystreak":
                if (!TryReadInt(config, out targetValue, "target", "days", "streak"))
                {
                    return false;
                }
                currentValue = activitySummary.BestStreak;
                break;

            default:
                return false;
        }

        targetValue = Math.Max(targetValue, 0);
        currentValue = Math.Max(currentValue, 0);

        return targetValue > 0;
    }

    private static bool TryReadInt(JsonElement element, out int value, params string[] propertyNames)
    {
        value = 0;

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out value))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertyName in propertyNames)
            {
                if (element.TryGetProperty(propertyName, out var property))
                {
                    if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value))
                    {
                        return true;
                    }

                    if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out value))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool TryReadString(JsonElement element, out string? value, params string[] propertyNames)
    {
        value = null;

        if (element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString();
            return !string.IsNullOrWhiteSpace(value);
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertyName in propertyNames)
            {
                if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
                {
                    var candidate = property.GetString();
                    if (!string.IsNullOrWhiteSpace(candidate))
                    {
                        value = candidate;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool TryGetSubjectCriteria(JsonElement config, out SubjectCriteria criteria)
    {
        criteria = new SubjectCriteria(null, null);

        int? subjectId = null;
        string? subjectName = null;

        if (TryReadInt(config, out var subjectIdValue, "subjectId"))
        {
            subjectId = subjectIdValue;
        }

        if (TryReadString(config, out var subjectNameValue, "subjectName", "subject"))
        {
            subjectName = subjectNameValue?.Trim();
        }

        if (subjectId.HasValue || !string.IsNullOrWhiteSpace(subjectName))
        {
            criteria = new SubjectCriteria(subjectId, subjectName);
            return true;
        }

        return false;
    }

    private static StudentSubjectAggregate? FindSubjectAggregate(IReadOnlyList<StudentSubjectAggregate> aggregates, SubjectCriteria criteria)
    {
        if (criteria.SubjectId.HasValue)
        {
            var byId = aggregates.FirstOrDefault(x => x.SubjectId == criteria.SubjectId.Value);
            if (byId != null)
            {
                return byId;
            }
        }

        if (!string.IsNullOrWhiteSpace(criteria.SubjectName))
        {
            return aggregates.FirstOrDefault(x => string.Equals(x.SubjectName, criteria.SubjectName, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private sealed record SubjectCriteria(int? SubjectId, string? SubjectName);
}

