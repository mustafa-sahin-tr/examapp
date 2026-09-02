using MassTransit;

namespace BadgeService.Consumers;

/// <summary>
/// Retry politikasını SADECE <see cref="WorksheetReminderDueConsumer"/>'a scope'lar.
/// Paylaşılan <c>badge-service</c> endpoint'indeki diğer consumer'lar (AnswerSubmitted,
/// QuestionCreated) bu ayardan etkilenmez.
///
/// Beklenmeyen hata: 3 kez immediate retry → hâlâ başarısızsa mesaj
/// <c>badge-service_error</c> (dead-letter) kuyruğuna taşınır.
/// </summary>
public class WorksheetReminderDueConsumerDefinition : ConsumerDefinition<WorksheetReminderDueConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<WorksheetReminderDueConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        consumerConfigurator.UseMessageRetry(r => r.Immediate(3));
    }
}
