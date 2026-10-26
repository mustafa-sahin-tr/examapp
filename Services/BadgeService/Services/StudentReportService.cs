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

    public async Task<BadgeProgressReportDto> GetBadgeProgressAsync(int userId, CancellationToken cancellationToken = default)
    {
        var summary = await _context.StudentQuestionAggregates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

        // Issue #572: yalnız etkinlik çözen öğrencinin soru aggregate'i yoktur ama etkinlik rozet ilerlemesi/kazanımı olabilir;
        // o durumda boş rapora düşme (soru alanları 0 döner). Hiç ilerleme/kazanım yoksa eski boş rapor davranışı aynen.
        if (summary == null
            && !await _context.StudentBadgeProgresses.AnyAsync(x => x.UserId == userId, cancellationToken)
            && !await _context.BadgeEarned.AnyAsync(x => x.UserId == userId, cancellationToken))
        {
            return await BuildEmptyReportAsync(userId, cancellationToken);
        }

        // Code review follow-up (#148, SHOULD-FIX): a badge already earned must stay visible/marked
        // completed even if StudentBadgeProgress.IsCompleted was later recomputed to false (e.g. an admin
        // raised the threshold after the student had already met the old one) — BadgeEarned is the
        // permanent record, not the recomputed progress row. Fetch it first so both the visibility filter
        // and IsCompleted below key off "was this ever earned", not the possibly-stale progress flag.
        var earnedByBadgeId = await _context.BadgeEarned
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToDictionaryAsync(x => x.BadgeDefinitionId, x => x.EarnedDate, cancellationToken);

        var progressRows = await _context.StudentBadgeProgresses
            .AsNoTracking()
            .Include(x => x.BadgeDefinition)
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);

        // Issue #148: deactivated badges are hidden from the catalog, but a badge the student already
        // earned stays visible — deactivation must not erase history.
        var badgeProgress = progressRows
            .Where(x => x.BadgeDefinition.IsActive || earnedByBadgeId.ContainsKey(x.BadgeDefinitionId))
            .OrderBy(x => x.BadgeDefinition.PathKey == null)
            .ThenBy(x => x.BadgeDefinition.PathName)
            .ThenBy(x => x.BadgeDefinition.PathOrder)
            .ThenBy(x => x.BadgeDefinition.Name)
            .Select(x => new BadgeProgressItemDto
            {
                BadgeDefinitionId = x.BadgeDefinitionId,
                Name = x.BadgeDefinition.Name,
                Description = x.BadgeDefinition.Description,
                // Security review (#149, D2): legacy path only if it passes the old format rule.
                IconUrl = BadgeIconValidator.IsValid(x.BadgeDefinition.IconUrl) ? x.BadgeDefinition.IconUrl : null,
                Icon = BadgeIconValidator.IsAllowedIcon(x.BadgeDefinition.Icon) ? x.BadgeDefinition.Icon : null,
                PathKey = x.BadgeDefinition.PathKey,
                PathName = x.BadgeDefinition.PathName,
                PathOrder = x.BadgeDefinition.PathOrder,
                CurrentValue = x.CurrentValue,
                TargetValue = x.TargetValue,
                IsCompleted = x.IsCompleted || earnedByBadgeId.ContainsKey(x.BadgeDefinitionId),
                EarnedDateUtc = earnedByBadgeId.TryGetValue(x.BadgeDefinitionId, out var earnedDate) ? earnedDate : null,
            })
            .ToList();

        // Issue #572: etkinlik rozet ilerlemesi saklı satırdan değil CANLI hesaplanır (seri bozulduysa "5/7" bayat kalmasın);
        // activity-summary ile aynı kaynak. Kazanılmış rozetin değeri düşürülmez.
        var activityToday = ActivityCompletionStats.ToIstanbulDate(DateTime.UtcNow);
        var progressByBadgeId = progressRows.ToDictionary(x => x.BadgeDefinitionId, x => x.BadgeDefinition);
        foreach (var item in badgeProgress)
        {
            if (item.IsCompleted
                || !progressByBadgeId.TryGetValue(item.BadgeDefinitionId, out var def)
                || !BadgeRuleEvaluator.TryParseActivityRule(def, out var rule))
            {
                continue;
            }

            var live = await ActivityCompletionStats.ComputeCurrentAsync(_context, userId, rule, activityToday, cancellationToken);
            item.TargetValue = rule.Target;
            item.CurrentValue = Math.Min(live, rule.Target);
        }

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
                UserId = userId,
                TotalQuestions = summary?.TotalQuestions ?? 0,
                CorrectQuestions = summary?.CorrectQuestions ?? 0,
                AccuracyPercentage = summary == null || summary.TotalQuestions == 0 ? 0 : Math.Round((double)summary.CorrectQuestions / summary.TotalQuestions * 100, 2),
                TotalPoints = summary?.TotalPoints ?? 0,
                CurrentCorrectStreak = summary?.CurrentCorrectStreak ?? 0,
                BestCorrectStreak = summary?.BestCorrectStreak ?? 0,
                TotalTimeSeconds = summary?.TotalTimeSeconds ?? 0,
                TotalActiveDays = activitySummary.TotalActiveDays,
                CurrentActivityStreak = activitySummary.CurrentStreak,
                BestActivityStreak = activitySummary.BestStreak,
                LastAnsweredAtUtc = summary?.LastAnsweredAtUtc,
                LastUpdatedUtc = summary?.LastUpdatedUtc ?? DateTime.UtcNow
            },
            BadgeProgress = badgeProgress,
            SubjectBreakdown = subjects
        };
    }

    /// <summary>
    /// Issue #572: bugünün (Europe/Istanbul) etkinlik özeti + henüz kazanılmamış aktif etkinlik rozetlerinden hedefe
    /// en yakın en çok 3 tanesi (current/target azalan). Hepsi kazanıldıysa <c>Badges</c> boş. Değerler canlı
    /// <see cref="StudentActivityCompletion"/>'dan hesaplanır (seri bozulduysa eski ilerleme satırı gösterilmez).
    /// </summary>
    public async Task<ActivitySummaryDto> GetActivitySummaryAsync(int userId, DateTime? nowUtc = null, CancellationToken cancellationToken = default)
    {
        var today = ActivityCompletionStats.ToIstanbulDate(nowUtc ?? DateTime.UtcNow);

        var summary = new ActivitySummaryDto
        {
            Today = new ActivityTodayDto
            {
                ActivityCount = await ActivityCompletionStats.CountOnDateAsync(_context, userId, today, cancellationToken),
                ActiveMinutes = await ActivityCompletionStats.StudyMinutesAsync(_context, userId, null, today, cancellationToken),
                StreakDays = await ActivityCompletionStats.CurrentDayStreakAsync(_context, userId, null, today, cancellationToken),
            }
        };

        var definitions = (await _context.BadgeDefinitions
                .AsNoTracking()
                .Where(x => x.IsActive)
                .ToListAsync(cancellationToken))
            .Where(x => BadgeRuleEvaluator.IsActivityRuleType(x.RuleType))
            .ToList();
        if (definitions.Count == 0)
        {
            return summary;
        }

        var earnedIds = (await _context.BadgeEarned
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .Select(x => x.BadgeDefinitionId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var candidates = new List<ActivityBadgeProgressDto>();
        foreach (var definition in definitions.Where(d => !earnedIds.Contains(d.Id)))
        {
            if (!BadgeRuleEvaluator.TryParseActivityRule(definition, out var rule))
            {
                continue;
            }

            var current = await ActivityCompletionStats.ComputeCurrentAsync(_context, userId, rule, today, cancellationToken);
            // Eşiği geçmiş ama henüz ödüllenmemiş (ör. admin hedefi düşürdü): "10/10" göstermek yerine atla; sonraki
            // etkinlikte değerlendirici ödüllendirir.
            if (current >= rule.Target)
            {
                continue;
            }

            candidates.Add(new ActivityBadgeProgressDto
            {
                Code = definition.Code,
                Name = definition.Name,
                Current = current,
                Target = rule.Target,
                Earned = false,
            });
        }

        summary.Badges = candidates
            .OrderByDescending(x => (double)x.Current / x.Target)
            .ThenBy(x => x.Target)
            .ThenBy(x => x.Code, StringComparer.Ordinal)
            .Take(3)
            .ToList();
        return summary;
    }

    private async Task<BadgeProgressReportDto> BuildEmptyReportAsync(int userId, CancellationToken cancellationToken)
    {
        var dailyActivities = await _context.StudentDailyActivities
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);

        var activitySummary = ActivityAnalytics.Calculate(dailyActivities);

        // Issue #148: no progress rows yet — nothing earned, so only active definitions are shown.
        var definitions = await _context.BadgeDefinitions
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.PathKey == null)
            .ThenBy(x => x.PathName)
            .ThenBy(x => x.PathOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var badgeProgress = new List<BadgeProgressItemDto>(definitions.Count);
        var emptySubjects = Array.Empty<StudentSubjectAggregate>();

        foreach (var definition in definitions)
        {
            var targetValue = 0;
            if (BadgeRuleEvaluator.TryParseActivityRule(definition, out var activityRule))
            {
                targetValue = activityRule.Target;
            }
            else if (!BadgeRuleEvaluator.TryEvaluateRule(definition, null, emptySubjects, activitySummary, out _, out targetValue))
            {
                continue;
            }

            badgeProgress.Add(new BadgeProgressItemDto
            {
                BadgeDefinitionId = definition.Id,
                Name = definition.Name,
                Description = definition.Description,
                IconUrl = BadgeIconValidator.IsValid(definition.IconUrl) ? definition.IconUrl : null,
                Icon = BadgeIconValidator.IsAllowedIcon(definition.Icon) ? definition.Icon : null,
                PathKey = definition.PathKey,
                PathName = definition.PathName,
                PathOrder = definition.PathOrder,
                CurrentValue = 0,
                TargetValue = targetValue,
                IsCompleted = false,
                EarnedDateUtc = null
            });
        }

        return new BadgeProgressReportDto
        {
            Summary = new StudentSummaryDto
            {
                UserId = userId,
                TotalQuestions = 0,
                CorrectQuestions = 0,
                AccuracyPercentage = 0,
                TotalPoints = 0,
                CurrentCorrectStreak = 0,
                BestCorrectStreak = 0,
                TotalTimeSeconds = 0,
                TotalActiveDays = 0,
                CurrentActivityStreak = 0,
                BestActivityStreak = 0,
                LastAnsweredAtUtc = null,
                LastUpdatedUtc = DateTime.UtcNow
            },
            BadgeProgress = badgeProgress,
            SubjectBreakdown = new List<SubjectAggregateDto>()
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
