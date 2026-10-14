using MassTransit;

namespace BadgeService.Consumers;

/// <summary>
/// Retry politikasını SADECE <see cref="BookingParentDecisionConsumer"/>'a scope'lar (issue #439). Beklenmeyen hata: 3 kez
/// immediate retry (sub çözülemedi hariç: retry yok) → <c>badge-service_error</c> (dead-letter).
/// </summary>
public class BookingParentDecisionConsumerDefinition : ConsumerDefinition<BookingParentDecisionConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<BookingParentDecisionConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        consumerConfigurator.UseMessageRetry(r =>
        {
            r.Ignore<RecipientSubUnresolvedException>();
            r.Immediate(3);
        });
    }
}
