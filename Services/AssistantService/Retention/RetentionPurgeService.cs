using Microsoft.Extensions.Options;

namespace AssistantService.Retention;

/// <summary>
/// Issue #505: runs <see cref="AssistantDataCleaner.PurgeExpiredAsync"/> shortly after start and then every
/// <see cref="AssistantOptions.PurgeIntervalMinutes"/>. Runs whether or not the assistant is switched on (old data must
/// still go). A failed pass is logged and retried on the next tick. Deletes are idempotent, so two instances are harmless.
/// </summary>
public sealed class RetentionPurgeService(
    IServiceScopeFactory scopes,
    IOptionsMonitor<AssistantOptions> options,
    TimeProvider clock,
    ILogger<RetentionPurgeService> logger) : BackgroundService
{
    internal static readonly TimeSpan StartDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartDelay, clock, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunOnceAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromMinutes(options.CurrentValue.PurgeIntervalMinutes), clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<AssistantDataCleaner>().PurgeExpiredAsync(ct);
            if (result.Total > 0)
                logger.LogInformation(
                    "Assistant retention purge: {Conversations} conversations, {Messages} messages, {PendingActions} pending actions, {UsageDays} usage rows deleted.",
                    result.Conversations, result.Messages, result.PendingActions, result.UsageDays);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Assistant retention purge failed; retrying on the next tick.");
        }
    }
}
