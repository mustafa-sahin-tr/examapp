using MassTransit;

namespace BadgeService.Consumers;

/// <summary>
/// Retry politikasını SADECE <see cref="WorksheetCommentRepliedConsumer"/>'a scope'lar.
/// Beklenmeyen hata: 3 kez immediate retry → hâlâ başarısızsa mesaj <c>badge-service_error</c>
/// (dead-letter) kuyruğuna taşınır.
/// </summary>
public class WorksheetCommentRepliedConsumerDefinition : ConsumerDefinition<WorksheetCommentRepliedConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<WorksheetCommentRepliedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        consumerConfigurator.UseMessageRetry(r => r.Immediate(3));
    }
}
