using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OutboxPublisherService.Data;
using System.Text.Json;

namespace OutboxPublisherService.Publishers;

/// <summary>
/// Polls the shared <c>OutboxMessages</c> table and publishes each pending message to the bus.
///
/// Safe to run as multiple instances: each poll claims a batch with
/// <c>FOR UPDATE SKIP LOCKED</c> inside a transaction, so two publishers never grab the
/// same row. Failed publishes are retried with exponential backoff and dead-lettered
/// (left in place, no longer picked up) after <see cref="OutboxOptions.MaxRetries"/>
/// attempts. Processed rows are purged after <see cref="OutboxOptions.Retention"/>.
/// </summary>
public class OutboxProcessor : BackgroundService
{
    private const int MaxErrorLength = 4000;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OutboxProcessor> _logger;
    private readonly OutboxOptions _options;

    private DateTime _lastPurgeUtc = DateTime.MinValue;

    public OutboxProcessor(
        IServiceProvider serviceProvider,
        ILogger<OutboxProcessor> logger,
        IOptions<OutboxOptions> options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "OutboxProcessor started: poll {Poll}s, batch {Batch}, maxRetries {MaxRetries}",
            _options.PollInterval.TotalSeconds, _options.BatchSize, _options.MaxRetries);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessBatchAsync(stoppingToken);
                await PurgeIfDueAsync(stoppingToken);

                // Drain quickly while there is a full batch waiting; otherwise back off to the poll interval.
                if (processed < _options.BatchSize)
                    await Task.Delay(_options.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox poll failed; retrying after {Delay}s", _options.PollInterval.TotalSeconds);
                await Task.Delay(_options.PollInterval, stoppingToken);
            }
        }
    }

    private async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Un-composed FromSqlRaw runs verbatim, so FOR UPDATE SKIP LOCKED is preserved.
        var messages = await db.OutboxMessages
            .FromSqlRaw(
                """
                SELECT "Id", "Type", "Content", "CreatedAt", "ProcessedAt", "RetryCount", "NextAttemptAt", "Error"
                FROM "OutboxMessages"
                WHERE "ProcessedAt" IS NULL
                  AND "RetryCount" < {0}
                  AND ("NextAttemptAt" IS NULL OR "NextAttemptAt" <= {1})
                ORDER BY "CreatedAt"
                LIMIT {2}
                FOR UPDATE SKIP LOCKED
                """,
                _options.MaxRetries, DateTime.UtcNow, _options.BatchSize)
            .ToListAsync(ct);

        if (messages.Count == 0)
        {
            await tx.RollbackAsync(ct);
            return 0;
        }

        foreach (var message in messages)
        {
            await TryPublishAsync(publisher, message, ct);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return messages.Count;
    }

    private async Task TryPublishAsync(IPublishEndpoint publisher, OutboxMessage message, CancellationToken ct)
    {
        var eventType = OutboxEventRegistry.Resolve(message.Type);
        if (eventType == null)
        {
            DeadLetter(message, $"Unresolved event type: '{message.Type}'");
            return;
        }

        try
        {
            var @event = JsonSerializer.Deserialize(message.Content, eventType)
                ?? throw new InvalidOperationException("Deserialized event was null.");

            await publisher.Publish(@event, eventType, ct);

            message.ProcessedAt = DateTime.UtcNow;
            message.Error = null;
        }
        catch (Exception ex)
        {
            RecordFailure(message, ex);
        }
    }

    private void RecordFailure(OutboxMessage message, Exception ex)
    {
        message.RetryCount++;
        message.Error = Truncate(ex.Message);

        if (message.RetryCount >= _options.MaxRetries)
        {
            message.NextAttemptAt = null;
            _logger.LogError(ex,
                "Outbox message {Id} ({Type}) dead-lettered after {Attempts} attempts",
                message.Id, message.Type, message.RetryCount);
            return;
        }

        var delay = ComputeBackoff(message.RetryCount);
        message.NextAttemptAt = DateTime.UtcNow + delay;
        _logger.LogWarning(ex,
            "Outbox message {Id} ({Type}) failed (attempt {Attempt}/{Max}); retrying in {Delay}",
            message.Id, message.Type, message.RetryCount, _options.MaxRetries, delay);
    }

    private void DeadLetter(OutboxMessage message, string reason)
    {
        message.RetryCount = _options.MaxRetries;
        message.NextAttemptAt = null;
        message.Error = Truncate(reason);
        _logger.LogError("Outbox message {Id} dead-lettered: {Reason}", message.Id, reason);
    }

    private TimeSpan ComputeBackoff(int attempt)
    {
        // Base * 2^(attempt-1), capped.
        var factor = Math.Pow(2, Math.Min(attempt - 1, 20));
        var seconds = _options.RetryBackoffBase.TotalSeconds * factor;
        return seconds >= _options.RetryBackoffMax.TotalSeconds
            ? _options.RetryBackoffMax
            : TimeSpan.FromSeconds(seconds);
    }

    private async Task PurgeIfDueAsync(CancellationToken ct)
    {
        if (_options.Retention <= TimeSpan.Zero)
            return;
        if (DateTime.UtcNow - _lastPurgeUtc < _options.PurgeInterval)
            return;

        _lastPurgeUtc = DateTime.UtcNow;
        var cutoff = DateTime.UtcNow - _options.Retention;

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var deleted = await db.OutboxMessages
            .Where(x => x.ProcessedAt != null && x.ProcessedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
            _logger.LogInformation("Purged {Count} processed outbox rows older than {Cutoff:o}", deleted, cutoff);
    }

    private static string Truncate(string value) =>
        value.Length <= MaxErrorLength ? value : value[..MaxErrorLength];
}
