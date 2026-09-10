using ExamApp.Foundation.Contracts;
using MassTransit;

namespace BadgeService.Consumers;

/// <summary>
/// Bağımsız (okula bağlı olmayan) özel ders öğretmeni kaydı/geçişi akışının tüketici ucu.
/// Öğretmen <c>TeacherService.Save</c> içinde bağımsız olarak kaydolduğunda (ilk kayıt) veya
/// okula-bağlıdan bağımsıza geçtiğinde exam API'nin yazdığı
/// <see cref="IndependentTeacherRegisteredEvent"/>'i alır.
///
/// BİLİNÇLİ OLARAK MİNİMAL: Bu consumer şimdilik sadece bilgi amaçlı loglama yapar. Admin
/// kullanıcı(lar)ını hedefleyen bir <see cref="Entities.Notification"/> satırı YAZILMAZ ve SignalR
/// push YAPILMAZ — sistemde "Admin" sadece bir Keycloak realm rolü, DB'de admin UserId/KeycloakId
/// listesi tutulmuyor; bu doğru şekilde hedeflenecek altyapı henüz yok. Admin onay ekranı akışı
/// (bağımsız öğretmenin ApprovalStatus'unu admin'in onaylaması) ayrı bir issue'da ele alınacak;
/// o issue'da bu consumer admin bildirimi üretecek şekilde genişletilecek. "Neden Notification
/// yazmıyor" sorusunun cevabı budur.
///
/// Idempotency: Bu consumer'ın hiçbir yan etkisi (DB yazımı, push, dış çağrı) olmadığından — sadece
/// log satırı ürettiğinden — ayrıca bir idempotency kontrolüne gerek yoktur. Aynı mesaj iki kez
/// gelirse log iki kez yazılır, bu zararsızdır.
///
/// Hata yolu: beklenmeyen hata (örn. logger arızası) fırlatılırsa MassTransit
/// <see cref="IndependentTeacherRegisteredConsumerDefinition"/>'daki retry politikasına göre
/// yeniden dener; hâlâ başarısızsa mesaj <c>badge-service_error</c> (dead-letter) kuyruğuna taşınır.
/// Sessiz yutma yok.
/// </summary>
public class IndependentTeacherRegisteredConsumer : IConsumer<IndependentTeacherRegisteredEvent>
{
    private readonly ILogger<IndependentTeacherRegisteredConsumer> _logger;

    public IndependentTeacherRegisteredConsumer(ILogger<IndependentTeacherRegisteredConsumer> logger)
    {
        _logger = logger;
    }

    public Task Consume(ConsumeContext<IndependentTeacherRegisteredEvent> context)
    {
        var e = context.Message;

        _logger.LogInformation(
            "IndependentTeacherRegistered alındı. TeacherId={TeacherId}, UserId={UserId}, IsNewRegistration={IsNewRegistration}, RegisteredAt={RegisteredAt}",
            e.TeacherId, e.UserId, e.IsNewRegistration, e.RegisteredAt);

        return Task.CompletedTask;
    }
}
