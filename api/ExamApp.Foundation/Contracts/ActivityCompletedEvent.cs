using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Issue #572 (epic #569) — bir öğrenci bir etkinliği (<c>Worksheet.Kind = Activity</c>) tamamladığında exam API'nin
/// <c>TestSessionService.EndTest</c>'i tarafından, Started → Completed koşullu yazımı gerçekten 1 satır değiştirdiyse, durum
/// değişikliğiyle AYNI transaction'da outbox'a TEK bir tane yazılır. Tekrarlanan bitirme çağrısı (zaten Completed → idempotent
/// başarı) ikinci event yazmaz. Etkinlik puansızdır: bu event puan / XP / liderlik / soru-ders aggregate'i tetiklemez; yalnız
/// etkinlik rozetleri (sayı, süre, seri) için tüketilir. Doğru/yanlış sayısı bilerek taşınmaz (rozet doğruluğa bağlı değil).
///
/// Idempotency: <see cref="EventId"/> (outbox satırının Id'si). Sayı rozeti için iş anahtarı öğrenci + etkinlik + deneme =
/// (<see cref="UserId"/>, <see cref="ActivityId"/>, <see cref="WorksheetInstanceId"/>); aynı etkinliğin tekrarı sayı rozetinde
/// (<see cref="UserId"/>, <see cref="ActivityId"/>) ile tekilleştirilir.
/// </summary>
public class ActivityCompletedEvent
{
    /// <summary>Producer-assigned correlation id = outbox satırının <c>OutboxMessage.Id</c>'si (bkz. <see cref="AnswerSubmittedEvent.EventId"/>).</summary>
    public Guid EventId { get; set; } = Guid.Empty;

    /// <summary>Öğrencinin uygulama kullanıcı id'si (BadgeService rozet/ilerleme kayıtlarını buna göre tutar).</summary>
    public int UserId { get; set; }

    /// <summary>Exam DB <c>Students.Id</c>.</summary>
    public int StudentId { get; set; }

    /// <summary>Öğrencinin Keycloak sub'ı — bildirim hedefi (bkz. <see cref="AnswerSubmittedEvent.ClientId"/>); çözülemediyse null.</summary>
    public string? ClientId { get; set; }

    /// <summary>Etkinlik id'si = <c>Worksheets.Id</c>.</summary>
    public int ActivityId { get; set; }

    /// <summary>Deneme = <c>WorksheetInstances.Id</c>.</summary>
    public int WorksheetInstanceId { get; set; }

    /// <summary>Oturumun sayıldığı, öğrenciye görünür <c>WorksheetAssignments.Id</c>; serbest çözümde null.</summary>
    public int? AssignmentId { get; set; }

    /// <summary><c>Worksheet.SubjectId</c>; boşsa oturum maddelerinde en sık geçen soru dersi; o da yoksa null.</summary>
    public int? SubjectId { get; set; }

    /// <summary><c>Worksheet.TopicId</c>.</summary>
    public int? TopicId { get; set; }

    /// <summary>Oturumdaki madde sayısı (boş bırakılanlar dahil).</summary>
    public int ItemCount { get; set; }

    /// <summary>
    /// Tamamlanma anında cevaplı madde sayısı: <c>SelectedAnswerId</c> ya da <c>AnswerPayload</c>'ı dolu olan maddeler
    /// (boş bırakılanlar hariç; 0 olabilir, event yine yazılır). Doğruluk bilgisi değildir — doğru/yanlış sayısı taşınmaz.
    /// BadgeService sayı/seri rozetinde "maddelerin en az yarısı (en az 1) cevaplı" eşiğini buna göre uygular.
    /// </summary>
    public int AnsweredItemCount { get; set; }

    /// <summary>
    /// Etkin çalışma süresi (sn): istemcinin ölçtüğü değer (2 dk'dan uzun hareketsizlik hariç), sunucuda
    /// <c>[0, min(etkinlik üst sınırı, oturum başlangıcından beri geçen süre)]</c> aralığına kırpılmış.
    /// </summary>
    public int ActiveSeconds { get; set; }

    /// <summary>Tamamlanma anı (UTC) = yazılan <c>WorksheetInstance.EndTime</c>.</summary>
    public DateTime CompletedAtUtc { get; set; }
}
