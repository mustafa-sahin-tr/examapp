using MassTransit;

namespace BadgeService.Consumers;

/// <summary>
/// Retry politikasını SADECE <see cref="BookingTeacherUnavailableConsumer"/>'a scope'lar (issue #298).
/// Beklenmeyen hata: 3 kez immediate retry → hâlâ başarısızsa mesaj <c>badge-service_error</c> kuyruğuna taşınır.
/// </summary>
public class BookingTeacherUnavailableConsumerDefinition : ConsumerDefinition<BookingTeacherUnavailableConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<BookingTeacherUnavailableConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        consumerConfigurator.UseMessageRetry(r => r.Immediate(3));
    }
}
