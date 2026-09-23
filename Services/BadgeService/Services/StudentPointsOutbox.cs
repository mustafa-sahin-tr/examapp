using System;
using System.Text.Json;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;

namespace BadgeService.Services;

/// <summary>
/// <see cref="StudentPointsChangedEvent"/>'i BadgeService outbox'ına ekler (issue #225).
/// SaveChanges ÇAĞIRMAZ — çağıran, aggregate değişikliğiyle aynı SaveChanges'te commit eder;
/// böylece puan değişip event'in kaybolması (ya da tersi) mümkün olmaz.
/// </summary>
public static class StudentPointsOutbox
{
    public static OutboxMessage Enqueue(BadgeDbContext db, int userId, int totalPoints, DateTime updatedAtUtc)
    {
        var evt = new StudentPointsChangedEvent
        {
            UserId = userId,
            TotalPoints = Math.Max(0, totalPoints),
            UpdatedAtUtc = EventVersion.Normalize(updatedAtUtc),
        };

        var message = new OutboxMessage
        {
            Type = OutboxEventRegistry.NameFor<StudentPointsChangedEvent>(),
            Content = JsonSerializer.Serialize(evt),
            CreatedAt = DateTime.UtcNow,
        };
        db.OutboxMessages.Add(message);
        return message;
    }
}
