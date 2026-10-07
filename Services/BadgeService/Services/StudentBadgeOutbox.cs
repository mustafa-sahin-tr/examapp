using System;
using System.Text.Json;
using BadgeService.Entities;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;

namespace BadgeService.Services;

/// <summary>
/// Issue #422: <see cref="StudentBadgeEarnedEvent"/>'i BadgeService outbox'ına ekler. SaveChanges ÇAĞIRMAZ — çağıran
/// (<see cref="BadgeEvaluator"/>) <c>BadgeEarned</c> satırıyla AYNI SaveChanges'te commit eder; rozet kazanılıp event'in
/// kaybolması (ya da tersi) mümkün olmaz. İkon yalnızca allowlist'ten geçmişse taşınır (#149 kuralı).
/// </summary>
public static class StudentBadgeOutbox
{
    public static OutboxMessage Enqueue(BadgeDbContext db, int userId, BadgeDefinition badge, DateTime earnedAtUtc)
    {
        var evt = new StudentBadgeEarnedEvent
        {
            UserId = userId,
            BadgeDefinitionId = badge.Id,
            Name = badge.Name,
            Icon = BadgeIconValidator.IsAllowedIcon(badge.Icon) ? badge.Icon : null,
            EarnedAtUtc = DateTime.SpecifyKind(earnedAtUtc, DateTimeKind.Utc),
        };

        var message = new OutboxMessage
        {
            Type = OutboxEventRegistry.NameFor<StudentBadgeEarnedEvent>(),
            Content = JsonSerializer.Serialize(evt),
            CreatedAt = DateTime.UtcNow,
        };
        db.OutboxMessages.Add(message);
        return message;
    }
}
