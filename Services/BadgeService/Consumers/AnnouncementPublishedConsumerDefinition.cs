using MassTransit;

namespace BadgeService.Consumers;

/// <summary>
/// Retry politikasını SADECE <see cref="AnnouncementPublishedConsumer"/>'a scope'lar (issue #1281). Beklenmeyen hata (DB, eşzamanlı
/// teslimde 23505): 3 kez immediate retry → hâlâ başarısızsa <c>badge-service_error</c>. Yeniden deneme yazılmış bildirimleri atlar.
/// </summary>
public class AnnouncementPublishedConsumerDefinition : ConsumerDefinition<AnnouncementPublishedConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<AnnouncementPublishedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        consumerConfigurator.UseMessageRetry(r => r.Immediate(3));
    }
}
