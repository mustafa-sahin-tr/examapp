using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Issue #614 — öğretmen (ya da admin) bir açık uçlu etkinlik cevabını ilk kez değerlendirince ya da değerlendirmesini değiştirince
/// exam API'nin <c>OpenTextReviewService</c>'i değerlendirme yazımıyla AYNI transaction'da yazar. TEK ALICI: cevabın sahibi öğrenci.
/// BadgeService uygulama içi bildirim üretir; bildirim öğrenciyi etkinliğin o maddesine götürür, geri bildirimi orada okur.
///
/// Güvenlik: id, Keycloak sub ve etkinlik adı; cevap metni, değerlendirme ve geri bildirim taşınmaz (öğrenci etkinlikte okur).
/// </summary>
public class OpenTextReviewedEvent
{
    /// <summary>Idempotency anahtarı — her değerlendirme kaydı için yeni.</summary>
    public Guid EventId { get; set; }

    public int RecipientUserId { get; set; }

    /// <summary>Öğrencinin Keycloak sub'ı; boş olabilir (consumer BadgeService verisinden çözer).</summary>
    public string RecipientKeycloakId { get; set; } = string.Empty;

    /// <summary>Öğrencinin etkinlik oturumu (<c>/activity/{TestInstanceId}</c>).</summary>
    public int TestInstanceId { get; set; }

    /// <summary>Değerlendirilen madde (oturum satırı); etkinlikte o maddeye gidilir.</summary>
    public int InstanceQuestionId { get; set; }

    public int WorksheetId { get; set; }

    public string WorksheetName { get; set; } = string.Empty;

    public DateTime ReviewedAtUtc { get; set; }
}
