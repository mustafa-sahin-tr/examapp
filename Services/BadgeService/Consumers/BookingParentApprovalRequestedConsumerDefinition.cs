using MassTransit;

namespace BadgeService.Consumers;

/// <summary>
/// Retry politikasını SADECE <see cref="BookingParentApprovalRequestedConsumer"/>'a scope'lar (issue #439). Beklenmeyen hata: 3 kez
/// immediate retry (sub çözülemedi hariç: retry yok) → <c>badge-service_error</c> (dead-letter).
/// </summary>
public class BookingParentApprovalRequestedConsumerDefinition : ConsumerDefinition<BookingParentApprovalRequestedConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<BookingParentApprovalRequestedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        consumerConfigurator.UseMessageRetry(r =>
        {
            r.Ignore<RecipientSubUnresolvedException>();
            r.Immediate(3);
        });
    }
}
