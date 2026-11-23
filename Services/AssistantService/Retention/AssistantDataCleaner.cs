using AssistantService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AssistantService.Retention;

public sealed record PurgeResult(int Messages, int Conversations, int PendingActions, int UsageDays)
{
    public int Total => Messages + Conversations + PendingActions + UsageDays;
}

/// <summary>
/// Issue #505: hard deletes of assistant data. Nothing is soft-deleted: the content is the student's own words and the
/// replies about their results (#496 decision 2: kept <see cref="AssistantOptions.RetentionDays"/> days).
/// Messages, pending actions and feedback go with their conversation (FK cascade); the direct deletes below cover the
/// rows that age out inside a conversation that is still in use.
/// </summary>
public sealed class AssistantDataCleaner(AssistantDbContext db, IOptionsMonitor<AssistantOptions> options, TimeProvider clock)
{
    /// <summary>Retention purge: everything older than the retention window, conversations by their last activity.</summary>
    public async Task<PurgeResult> PurgeExpiredAsync(CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow().UtcDateTime.AddDays(-options.CurrentValue.RetentionDays);
        var cutoffDay = DateOnly.FromDateTime(cutoff);

        var messages = await db.Messages.Where(m => m.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
        var pending = await db.PendingActions.Where(p => p.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
        var conversations = await db.Conversations.Where(c => c.UpdatedAt < cutoff).ExecuteDeleteAsync(ct);
        var usage = await db.Usage.Where(u => u.Day < cutoffDay).ExecuteDeleteAsync(ct);
        return new PurgeResult(messages, conversations, pending, usage);
    }

    /// <summary>
    /// Student reset (#396 → <c>StudentProgressResetEvent</c>): the user's conversations started before the reset line.
    /// Idempotent (a repeated event deletes nothing more). A missing line (<c>default</c>) deletes all of them.
    /// Usage counters stay: they hold no content and keep the daily budget honest.
    /// </summary>
    public async Task<int> EraseUserConversationsAsync(string userSub, DateTime createdBeforeUtc, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userSub);
        var before = createdBeforeUtc == default ? DateTime.MaxValue : DateTime.SpecifyKind(createdBeforeUtc, DateTimeKind.Utc);

        var conversations = await db.Conversations.Where(c => c.UserSub == userSub && c.CreatedAt < before).ExecuteDeleteAsync(ct);
        // Defensive: pending actions and feedback are keyed by the user too; none should be left without a conversation.
        await db.PendingActions.Where(p => p.UserSub == userSub && p.CreatedAt < before).ExecuteDeleteAsync(ct);
        await db.Feedback.Where(f => f.UserSub == userSub && f.CreatedAt < before).ExecuteDeleteAsync(ct);
        return conversations;
    }
}
