using System;
using ExamApp.Foundation.Contracts;
using MassTransit;

namespace BadgeService.Consumers;

/// <summary>
/// Issue #572: aynı kullanıcının <see cref="ActivityCompletedEvent"/>'leri SIRALI işlenir (UserId'ye göre partition,
/// <see cref="AnswerSubmittedConsumerDefinition"/> ile aynı desen ve aynı <c>PartitionKey</c>); böylece bir kullanıcının
/// rozet değerlendirmesi (progress + BadgeEarned) paralel çalışıp çift satır/yarış üretmez. Partitioner yalnız bu mesaj
/// tipine uygulanır.
/// <para>NOT: bu partitioner <see cref="AnswerSubmittedConsumerDefinition"/>'unkinden AYRIDIR (aynı UserId bu iki tipte
/// paralel işlenebilir). Güvenlidir çünkü iki yol ayrık rozet tanımlarına dokunur (etkinlik kuralları yalnız burada,
/// soru kuralları yalnız AnswerSubmitted'ta değerlendirilir). İleride her iki event'ten beslenen bir kural tipi
/// eklenirse ortak bir partition (aynı partitioner örneği) gerekir.</para> Retry: 1s/5s/15s, tükenirse <c>badge-service_error</c> (consumer idempotent olduğundan retry güvenli).
/// </summary>
public class ActivityCompletedConsumerDefinition : ConsumerDefinition<ActivityCompletedConsumer>
{
    public const int PartitionCount = 8;

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ActivityCompletedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        var partitioner = endpointConfigurator.CreatePartitioner(PartitionCount);
        endpointConfigurator.UsePartitioner<ActivityCompletedEvent>(partitioner, ctx => AnswerSubmittedConsumerDefinition.PartitionKey(ctx.Message.UserId));

        consumerConfigurator.UseMessageRetry(r => r.Intervals(
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));
    }
}
