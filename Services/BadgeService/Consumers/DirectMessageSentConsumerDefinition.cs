using MassTransit;

namespace BadgeService.Consumers;

/// <summary>
/// Retry politikasını SADECE <see cref="DirectMessageSentConsumer"/>'a scope'lar.
/// Beklenmeyen hata: 3 kez immediate retry -> hâlâ başarısızsa mesaj <c>badge-service_error</c> (dead-letter) kuyruğuna taşınır.
/// </summary>
public class DirectMessageSentConsumerDefinition : ConsumerDefinition<DirectMessageSentConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<DirectMessageSentConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        consumerConfigurator.UseMessageRetry(r => r.Immediate(3));
    }
}
