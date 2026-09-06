using MassTransit;

namespace BadgeService.Consumers;

/// <summary>
/// Retry politikasını SADECE <see cref="WorksheetAccessDecisionConsumer"/>'a scope'lar.
/// Paylaşılan <c>badge-service</c> endpoint'indeki diğer consumer'lar bu ayardan etkilenmez.
///
/// Beklenmeyen hata: 3 kez immediate retry → hâlâ başarısızsa mesaj
/// <c>badge-service_error</c> (dead-letter) kuyruğuna taşınır.
/// </summary>
public class WorksheetAccessDecisionConsumerDefinition : ConsumerDefinition<WorksheetAccessDecisionConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<WorksheetAccessDecisionConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        consumerConfigurator.UseMessageRetry(r => r.Immediate(3));
    }
}
