using MassTransit;

namespace BadgeService.Consumers;

/// <summary>
/// Retry politikasını SADECE <see cref="ParentHomeworkOverdueConsumer"/>'a scope'lar (issue #423). Beklenmeyen hata: 3 kez immediate (sub çözülemedi hariç: retry yok)
/// retry → hâlâ başarısızsa mesaj <c>badge-service_error</c> (dead-letter) kuyruğuna taşınır.
/// </summary>
public class ParentHomeworkOverdueConsumerDefinition : ConsumerDefinition<ParentHomeworkOverdueConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ParentHomeworkOverdueConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        consumerConfigurator.UseMessageRetry(r =>
        {
            // Sub çözülemedi = deterministik hata: retry sonucu değiştirmez, doğrudan error kuyruğuna (replay edilebilir).
            r.Ignore<RecipientSubUnresolvedException>();
            r.Immediate(3);
        });
    }
}
