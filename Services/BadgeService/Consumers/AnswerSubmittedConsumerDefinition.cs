using System;
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
/// içinde geçerlidir — yatay ölçeklemede aynı kullanıcı iki instance'a düşebilir; bunun için de
/// <c>BadgeDbContext</c>'teki xmin concurrency token'ı + <c>AnswerSubmissionAggregationService</c>'teki
/// <c>ConcurrencyRetry</c> devreye girer (issue #279, item 1).
///
/// Retry (issue #279, item 5 — GÜNCELLENDİ): artık sınırlı retry VAR. issue #243'te retry kasıtlı olarak
/// yoktu çünkü AnswerSubmittedConsumer idempotent değildi (retry puanı iki kez sayabilirdi). Bu artık
/// geçerli değil: <c>AnswerSubmissionAggregationService</c> (a) EventId'li mesajları <c>ProcessedAnswerSubmission</c>
/// ile, (b) TÜM mesajları (EventId'siz eskiler dahil) (TestInstanceId, QuestionId) başına revizyon karşılaştırmasıyla
/// (<c>AnswerPointAward</c>, issue #279 item 4) tekilleştiriyor — bir retry aynı mesajı ikinci kez uygularsa
/// revizyon ilerlemediği için no-op olur. 1s/5s/15s aralıklı 3 deneme (StudentPointsChangedConsumerDefinition
/// ile aynı desen); tükenirse <c>badge-service_error</c> (dead-letter).
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

        consumerConfigurator.UseMessageRetry(r => r.Intervals(
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));
    }

    /// <summary>UserId → deterministik partition anahtarı (MassTransit Guid anahtar bekler).</summary>
    public static Guid PartitionKey(int userId) => new(userId, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}
