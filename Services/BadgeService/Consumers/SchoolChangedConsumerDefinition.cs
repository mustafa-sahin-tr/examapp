using MassTransit;

namespace BadgeService.Consumers;

/// <summary>
/// Retry politikasını SADECE <see cref="SchoolChangedConsumer"/>'a scope'lar (issue #542). Beklenmeyen hata: 3 kez immediate retry
/// (sub çözülemedi hariç: retry yok) → hâlâ başarısızsa mesaj <c>badge-service_error</c> (dead-letter) kuyruğuna taşınır.
/// </summary>
public class SchoolChangedConsumerDefinition : ConsumerDefinition<SchoolChangedConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<SchoolChangedConsumer> consumerConfigurator,
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
