namespace OutboxPublisherService.Publishers;

/// <summary>Tuning for <see cref="OutboxProcessor"/>. Bound from the "Outbox" config section.</summary>
public class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>How often to poll for pending messages.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Rows claimed per poll (each poll runs in its own transaction with FOR UPDATE SKIP LOCKED).</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>Attempts before a message is dead-lettered (left in place, no longer picked up).</summary>
    public int MaxRetries { get; set; } = 10;

    /// <summary>Base delay for exponential backoff between retries.</summary>
    public TimeSpan RetryBackoffBase { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Upper bound on a single backoff delay.</summary>
    public TimeSpan RetryBackoffMax { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Processed rows older than this are purged. Set to zero to disable purging.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>How often the purge runs.</summary>
    public TimeSpan PurgeInterval { get; set; } = TimeSpan.FromHours(1);
}
