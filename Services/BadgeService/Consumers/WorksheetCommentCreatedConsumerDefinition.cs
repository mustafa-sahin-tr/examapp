using MassTransit;

namespace BadgeService.Consumers;

/// <summary>
/// Retry politikasını SADECE <see cref="WorksheetCommentCreatedConsumer"/>'a scope'lar.
/// Beklenmeyen hata: 3 kez immediate retry → hâlâ başarısızsa mesaj <c>badge-service_error</c>
/// (dead-letter) kuyruğuna taşınır.
/// </summary>
public class WorksheetCommentCreatedConsumerDefinition : ConsumerDefinition<WorksheetCommentCreatedConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<WorksheetCommentCreatedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        consumerConfigurator.UseMessageRetry(r => r.Immediate(3));
    }
}
