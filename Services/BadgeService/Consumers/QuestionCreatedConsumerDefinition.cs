using System;
using BadgeService.Services;
using MassTransit;

namespace BadgeService.Consumers;

/// <summary>
/// Soru sınıflandırma (Claude Haiku) Claude'un hız sınırına takılmasın: toplu kitap aktarımında yüzlerce
/// <c>QuestionCreatedEvent</c> art arda gelir. Kendi kuyruğunda (<see cref="QueueName"/>) çalışır, diğer BadgeService mesajlarını
/// bekletmez. Aynı anda en fazla <see cref="ConcurrentMessageLimit"/> mesaj işlenir; Claude'un
/// geçici hatası (429 / 529 / 5xx / bağlantı, SDK'nın kendi denemelerinden sonra) artan aralıklarla yeniden denenir, tükenirse
/// <c>badge-service_error</c>. Diğer hatalar eskisi gibi yeniden denenmez (taksonomi dışı yanıt vb. tekrar etmekle düzelmez).
/// Tüketici idempotent (sınıflandırma ve etiketler üzerine yazılır).
/// </summary>
public class QuestionCreatedConsumerDefinition : ConsumerDefinition<QuestionCreatedConsumer>
{
    public const string QueueName = "badge-question-classifier";
    public const int ConcurrentMessageLimit = 2;

    internal static readonly TimeSpan[] RetryIntervals =
    [
        TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10),
    ];

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<QuestionCreatedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        consumerConfigurator.ConcurrentMessageLimit = ConcurrentMessageLimit;
        consumerConfigurator.UseMessageRetry(r =>
        {
            r.Handle<Exception>(ClaudeTransient.Is);
            r.Intervals(RetryIntervals);
        });
    }
}
