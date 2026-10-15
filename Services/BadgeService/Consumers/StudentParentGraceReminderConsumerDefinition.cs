using MassTransit;

namespace BadgeService.Consumers;

/// <summary>Retry politikasını SADECE <see cref="StudentParentGraceReminderConsumer"/>'a scope'lar (issue #441): 3 immediate retry, sonra <c>badge-service_error</c>; sub yok = retry'sız.</summary>
public class StudentParentGraceReminderConsumerDefinition : ConsumerDefinition<StudentParentGraceReminderConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<StudentParentGraceReminderConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        consumerConfigurator.UseMessageRetry(r =>
        {
            r.Ignore<RecipientSubUnresolvedException>();
            r.Immediate(3);
        });
    }
}
