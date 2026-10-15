using MassTransit;

namespace BadgeService.Consumers;

/// <summary>
/// Retry politikasını SADECE <see cref="ParentInvitationRequestedConsumer"/>'a scope'lar (issue #440). Beklenmeyen hata
/// (ör. e-posta sağlayıcısı geçici hata): 3 kez immediate retry, sonra <c>badge-service_error</c> (dead-letter).
/// Deterministik hatalar (sub yok, bozuk event) retry'sız doğrudan error kuyruğuna.
/// </summary>
public class ParentInvitationRequestedConsumerDefinition : ConsumerDefinition<ParentInvitationRequestedConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ParentInvitationRequestedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        consumerConfigurator.UseMessageRetry(r =>
        {
            r.Ignore<RecipientSubUnresolvedException>();
            r.Ignore<InvalidParentInvitationException>();
            r.Immediate(3);
        });
    }
}
