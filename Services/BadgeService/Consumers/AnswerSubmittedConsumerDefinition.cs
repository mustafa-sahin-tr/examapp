using ExamApp.Foundation.Contracts;
using MassTransit;

namespace BadgeService.Consumers;

/// <summary>
/// issue #225: aynı kullanıcının <see cref="AnswerSubmittedEvent"/>'leri SIRALI işlenir (UserId'ye göre
/// partition). Paralel işlemede aggregate güncellemeleri birbirini ezebiliyor ve
/// <see cref="StudentPointsChangedEvent"/> versiyonu (zaman damgası) commit sırasıyla uyuşmayıp exam API'deki
/// XP eski değerde takılabiliyordu. Farklı kullanıcılar <see cref="PartitionCount"/> kadar paralel işlenir.
///
/// Partitioner yalnızca AnswerSubmittedEvent'e uygulanır (mesaj tipine özel filtre); paylaşılan
/// <c>badge-service</c> endpoint'indeki diğer consumer'lar etkilenmez. Kapsam: tek BadgeService instance'ı
/// içinde geçerlidir — yatay ölçeklemede aynı kullanıcı iki instance'a düşebilir (bkz. rapor/açık kalanlar).
///
/// Retry bilinçli olarak YOK (önceki davranış korunur): AnswerSubmittedConsumer idempotent değil, retry
/// puanı iki kez sayabilir. Hata → <c>badge-service_error</c>.
/// </summary>
public class AnswerSubmittedConsumerDefinition : ConsumerDefinition<AnswerSubmittedConsumer>
{
    public const int PartitionCount = 8;

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<AnswerSubmittedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        var partitioner = endpointConfigurator.CreatePartitioner(PartitionCount);
        endpointConfigurator.UsePartitioner<AnswerSubmittedEvent>(partitioner, ctx => PartitionKey(ctx.Message.UserId));
    }

    /// <summary>UserId → deterministik partition anahtarı (MassTransit Guid anahtar bekler).</summary>
    public static Guid PartitionKey(int userId) => new(userId, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}
