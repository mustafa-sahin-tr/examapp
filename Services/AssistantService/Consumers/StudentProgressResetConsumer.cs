using AssistantService.Retention;
using ExamApp.Foundation.Contracts;
using MassTransit;

namespace AssistantService.Consumers;

/// <summary>
/// Issue #505: exam API's <c>StudentResetJob</c> (#396) → outbox → <see cref="StudentProgressResetEvent"/>. The student's
/// assistant conversations are in this service's DB, so the consumer is here (architecture.md, #225 rule; queue
/// <c>assistant-service</c>, dead-letter <c>assistant-service_error</c>). Idempotency: the delete is by user and reset line,
/// a repeat deletes nothing more. An event without a sub is logged and acknowledged; other errors retry, then dead-letter.
/// </summary>
public sealed class StudentProgressResetConsumer(AssistantDataCleaner cleaner, ILogger<StudentProgressResetConsumer> logger)
    : IConsumer<StudentProgressResetEvent>
{
    public async Task Consume(ConsumeContext<StudentProgressResetEvent> context)
    {
        var evt = context.Message;
        if (string.IsNullOrWhiteSpace(evt.KeycloakId))
        {
            logger.LogWarning("StudentProgressReset without a Keycloak id ignored (EventId={EventId}, UserId={UserId}).", evt.EventId, evt.UserId);
            return;
        }

        try
        {
            var deleted = await cleaner.EraseUserConversationsAsync(evt.KeycloakId, evt.ResetAtUtc, context.CancellationToken);
            logger.LogInformation("StudentProgressReset: {Count} assistant conversations deleted (EventId={EventId}, UserId={UserId}).",
                deleted, evt.EventId, evt.UserId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "StudentProgressReset failed (EventId={EventId}, UserId={UserId}); MassTransit retry / dead-letter.",
                evt.EventId, evt.UserId);
            throw;
        }
    }
}

/// <summary>Retry: 1 s, 5 s, 15 s, then <c>assistant-service_error</c>.</summary>
public sealed class StudentProgressResetConsumerDefinition : ConsumerDefinition<StudentProgressResetConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<StudentProgressResetConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        consumerConfigurator.UseMessageRetry(r => r.Intervals(
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));
    }
}
