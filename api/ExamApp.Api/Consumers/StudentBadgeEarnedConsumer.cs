using ExamApp.Api.Services.Badges;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Consumers;

/// <summary>
/// Issue #422: BadgeService outbox'ından (<c>badge-outbox-publisher</c>) gelen <see cref="StudentBadgeEarnedEvent"/>'i
/// exam DB'deki <c>StudentBadgeProjections</c>'a taşır — veli paneli rozetleri buradan okur. Neden exam API'de: yazılan veri
/// exam DB'de (architecture.md #225 istisnası; StudentPointsChangedConsumer ile aynı gerekçe ve desen).
/// Idempotency: <see cref="StudentBadgeProjectionService"/> (öğrenci + rozet UNIQUE). Öğrenci yok / geçersiz payload → log + ack.
/// Beklenmeyen hata fırlatılır → <see cref="StudentBadgeEarnedConsumerDefinition"/> retry → <c>exam-api_error</c>.
/// </summary>
public sealed class StudentBadgeEarnedConsumer : IConsumer<StudentBadgeEarnedEvent>
{
    private readonly IStudentBadgeProjectionService _projection;
    private readonly ILogger<StudentBadgeEarnedConsumer> _logger;

    public StudentBadgeEarnedConsumer(IStudentBadgeProjectionService projection, ILogger<StudentBadgeEarnedConsumer> logger)
    {
        _projection = projection;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<StudentBadgeEarnedEvent> context)
    {
        var evt = context.Message;
        try
        {
            var result = await _projection.ApplyAsync(evt, context.CancellationToken);
            _logger.LogDebug("StudentBadgeEarned işlendi: {Result} (MessageId={MessageId}, UserId={UserId}, BadgeId={BadgeId}).",
                result, context.MessageId, evt.UserId, evt.BadgeDefinitionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "StudentBadgeEarned işlenemedi (MessageId={MessageId}, UserId={UserId}, BadgeId={BadgeId}); MassTransit retry/dead-letter.",
                context.MessageId, evt.UserId, evt.BadgeDefinitionId);
            throw;
        }
    }
}

/// <summary>Retry yalnız bu consumer'a: 1s, 5s, 15s → sonra <c>exam-api_error</c>.</summary>
public sealed class StudentBadgeEarnedConsumerDefinition : ConsumerDefinition<StudentBadgeEarnedConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<StudentBadgeEarnedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        consumerConfigurator.UseMessageRetry(r => r.Intervals(
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));
    }
}
