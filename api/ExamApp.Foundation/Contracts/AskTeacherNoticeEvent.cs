using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Issue #1111 — "Öğretmene sor" durum değişikliği. Exam API'nin <c>AskTeacherService</c>'i durumu yazan işlemle AYNI transaction'da
/// yazar: yeni soru (<see cref="Asked"/>) muhatap öğretmene, cevap (<see cref="Answered"/>) ve süre dolumu (<see cref="Expired"/>)
/// soran öğrenciye. TEK ALICI / event. BadgeService uygulama içi bildirim üretir.
///
/// Güvenlik: soru notu, cevap metni ve görsel taşınmaz; alıcı bildirimden soruyu açar ve orada okur.
/// </summary>
public class AskTeacherNoticeEvent
{
    public const string Asked = "Asked";
    public const string Answered = "Answered";
    public const string Expired = "Expired";

    /// <summary>Idempotency anahtarı — her durum değişikliği için yeni.</summary>
    public Guid EventId { get; set; }

    /// <summary><see cref="Asked"/>, <see cref="Answered"/> ya da <see cref="Expired"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>AskTeacherQuestions.Id.</summary>
    public int QuestionId { get; set; }

    /// <summary>Karşı tarafın kısa görünen adı ("Ad S."): öğretmene öğrenci, öğrenciye öğretmen; çözülemediyse boş.</summary>
    public string CounterpartName { get; set; } = string.Empty;

    /// <summary>Cevap süresinin sonu (UTC).</summary>
    public DateTime DueAtUtc { get; set; }

    public int RecipientUserId { get; set; }

    /// <summary>Alıcının Keycloak sub'ı; boş olabilir (consumer BadgeService verisinden çözer).</summary>
    public string RecipientKeycloakId { get; set; } = string.Empty;
}
