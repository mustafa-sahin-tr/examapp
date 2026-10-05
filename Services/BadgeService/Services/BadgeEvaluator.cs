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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BadgeService.Services;

public class BadgeEvaluator
{
    public const string NotificationType = "BadgeEarned";

    private readonly BadgeDbContext _context;
    private readonly IHubContext<BadgeNotificationHub> _hub;
    private readonly IUserLocaleResolver _localeResolver;
    private readonly INotificationTextFactory _texts;
    private readonly ILogger<BadgeEvaluator> _logger;

    public BadgeEvaluator(
        BadgeDbContext context,
        IHubContext<BadgeNotificationHub> hub,
        IUserLocaleResolver localeResolver,
        INotificationTextFactory texts,
        ILogger<BadgeEvaluator>? logger = null)
    {
        _logger = logger ?? NullLogger<BadgeEvaluator>.Instance;
        _context = context;
        _hub = hub;
        _localeResolver = localeResolver;
        _texts = texts;
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

        // Issue #148: deactivated badges are never (re-)evaluated or newly awarded. Re-read on every
        // call (AsNoTracking, no caching) so an admin's edit/deactivate via BadgeDefinitionAdminService
        // takes effect on the very next evaluation — no cache to invalidate.
        var badgeDefinitions = await _context.BadgeDefinitions
            .AsNoTracking()
            .Where(x => x.IsActive)
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
            // Code review follow-up (#148, SHOULD-FIX): once a badge has been earned, raising its
            // threshold later must never "un-earn" it — BadgeEarned is the permanent record; a
            // recomputed CurrentValue dipping back below the new TargetValue must not flip IsCompleted
            // back to false (StudentReportService also treats "earned" as authoritative, but keeping this
            // in sync avoids progress.IsCompleted silently disagreeing with BadgeEarned).
            progress.IsCompleted = progress.CurrentValue >= progress.TargetValue || earnedBadgeIds.Contains(definition.Id);
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

        // Issue #146: kalıcı bildirim, BadgeEarned satırıyla AYNI SaveChanges'te (atomik) yazılır —
        // rozet var ama bildirim yok (ya da tersi) durumu oluşamaz. Retry/duplicate teslimde rozet zaten
        // earnedBadgeIds'te olduğundan newlyEarned boş kalır → ikinci bildirim üretilmez; ayrıca
        // (UserId, SourceBadgeDefinitionId) filtreli unique index'i son savunma hattıdır.
        if (newlyEarned.Count > 0)
        {
            await AddBadgeEarnedNotificationsAsync(userId, clientId, newlyEarned, now, cancellationToken);
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
                // Security review (#149, D2): same rule as the notification data below.
                IconUrl = BadgeIconValidator.IsValid(badge.IconUrl) ? badge.IconUrl : null,
                // Issue #149: allowlisted Material Symbols name (null if unset/unknown) — UI prefers it over IconUrl.
                Icon = BadgeIconValidator.IsAllowedIcon(badge.Icon) ? badge.Icon : null
            }, cancellationToken);
        }
    }

    private async Task AddBadgeEarnedNotificationsAsync(
        int userId, string clientId, List<BadgeDefinition> newlyEarned, DateTime now, CancellationToken ct)
    {
        // Keycloak sub yoksa satır sahipsiz (API sub ile filtreler, SignalR hedefleyemez) ve unique index
        // yüzünden sonradan üretilemez olurdu; rozet yine kazanılır, bildirim yazılmaz. PII yok: yalnız userId.
        if (string.IsNullOrWhiteSpace(clientId))
        {
            _logger.LogWarning(
                "BadgeEarned bildirimi atlandı: ClientId (Keycloak sub) boş. UserId={UserId}, Rozet sayısı={Count}",
                userId, newlyEarned.Count);
            return;
        }

        var ids = newlyEarned.Select(b => b.Id).ToList();
        // Savunma: bildirimi zaten var olan rozet için (ör. BadgeEarned elle silinip yeniden kazanıldı)
        // ikinci satır eklenmez — unique index ihlali tüm SaveChanges'i düşürürdü.
        var alreadyNotified = (await _context.Notifications
                .AsNoTracking()
                .Where(n => n.UserId == userId && n.Type == NotificationType && n.SourceBadgeDefinitionId != null
                            && ids.Contains(n.SourceBadgeDefinitionId.Value))
                .Select(n => n.SourceBadgeDefinitionId!.Value)
                .ToListAsync(ct))
            .ToHashSet();

        var keycloakId = clientId;
        var culture = await _localeResolver.ResolveAsync(userId, keycloakId, ct);

        foreach (var badge in newlyEarned.Where(b => !alreadyNotified.Contains(b.Id)))
        {
            var text = _texts.Build(NotificationType, culture, badge.Name);
            _context.Notifications.Add(new Notification
            {
                UserId = userId,
                UserKeycloakId = keycloakId,
                Type = NotificationType,
                Title = text.Title,
                Body = text.Body,
                // PII yok: yalnızca UI'ın derin link/ikon kurması için rozet tanımı alanları.
                Data = JsonSerializer.Serialize(new
                {
                    badgeDefinitionId = badge.Id,
                    badgeCode = badge.Code,
                    // Seed/eski satırlar doğrulanmamış olabilir; dış URL takip pikseline dönüşmesin.
                    iconUrl = BadgeIconValidator.IsValid(badge.IconUrl) ? badge.IconUrl : null,
                    // Issue #149: icon name only (no PII); allowlist-checked like iconUrl.
                    icon = BadgeIconValidator.IsAllowedIcon(badge.Icon) ? badge.Icon : null
                }),
                SourceBadgeDefinitionId = badge.Id,
                IsRead = false,
                CreatedAt = now
            });
        }
    }
}
