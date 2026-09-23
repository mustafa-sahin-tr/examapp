using ExamApp.Api.Services.StudentPoints;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Consumers;

/// <summary>
/// Liderlik puan hattının tüketici ucu (issue #225): BadgeService'in outbox'ından
/// (<c>badge-outbox-publisher</c> üzerinden) gelen <see cref="StudentPointsChangedEvent"/>'i
/// <c>StudentPoints</c>'e taşır.
///
/// Neden BadgeService'te değil de exam API'de: yazılan veri (<c>StudentPoints</c>) exam API'nin DB'sinde.
/// Consumer BadgeService'te olsaydı exam API'ye senkron HTTP çağrısı gerekirdi (yasak); DB paylaşımı da yok.
/// Veri sahibinin kendi event'ini tüketmesi outbox kuralına uyan tek yol.
///
/// Idempotency: <see cref="StudentPointsSyncService"/> — versiyonlu (UpdatedAtUtc) mutlak-değer koşullu upsert.
///
/// Hata yolu: öğrenci yok ya da TotalPoints üst sınırı aşıyor → log + ack (retry anlamsız). Beklenmeyen hata (DB erişilemez vb.) fırlatılır →
/// <see cref="StudentPointsChangedConsumerDefinition"/>: 1s/5s/15s aralıklı 3 retry → hâlâ başarısızsa
/// mesaj <c>exam-api_error</c> (dead-letter) kuyruğuna taşınır. Korelasyon: MessageId + UserId loglanır.
/// </summary>
public sealed class StudentPointsChangedConsumer : IConsumer<StudentPointsChangedEvent>
{
    private readonly IStudentPointsSyncService _sync;
    private readonly ILogger<StudentPointsChangedConsumer> _logger;
    private readonly StudentPointsSyncOptions _options;

    public StudentPointsChangedConsumer(
        IStudentPointsSyncService sync,
        ILogger<StudentPointsChangedConsumer> logger,
        IOptions<StudentPointsSyncOptions> options)
    {
        _sync = sync;
        _logger = logger;
        _options = options.Value;
    }

    public async Task Consume(ConsumeContext<StudentPointsChangedEvent> context)
    {
        var evt = context.Message;

        // Üst sınır (gerekçe: StudentPointsSyncOptions.MaxTotalPoints): aşan değer bozuk/kötü niyetli mesaj
        // sayılır → atılır (ack; tekrar denemek değeri düzeltmez), Warning loglanır.
        if (evt.TotalPoints > _options.MaxTotalPoints)
        {
            _logger.LogWarning(
                "StudentPointsChanged üst sınırı aşıyor; mesaj atıldı (MessageId={MessageId}, UserId={UserId}, TotalPoints={TotalPoints}, Max={Max}).",
                context.MessageId, evt.UserId, evt.TotalPoints, _options.MaxTotalPoints);
            return;
        }

        try
        {
            var result = await _sync.ApplyAsync(evt, context.CancellationToken);
            _logger.LogDebug(
                "StudentPointsChanged işlendi: {Result} (MessageId={MessageId}, UserId={UserId}).",
                result, context.MessageId, evt.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "StudentPointsChanged işlenemedi (MessageId={MessageId}, UserId={UserId}, TotalPoints={TotalPoints}); MassTransit retry/dead-letter.",
                context.MessageId, evt.UserId, evt.TotalPoints);
            throw;
        }
    }
}

/// <summary>
/// Retry'ı yalnızca bu consumer'a scope'lar: 1s, 5s, 15s aralıklı 3 deneme (geçici DB kesintisi) →
/// sonra <c>exam-api_error</c> dead-letter kuyruğu.
/// </summary>
public sealed class StudentPointsChangedConsumerDefinition : ConsumerDefinition<StudentPointsChangedConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<StudentPointsChangedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        consumerConfigurator.UseMessageRetry(r => r.Intervals(
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));
    }
}
