using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Issue #419 (epic #407 V1) — veli, öğrencinin ürettiği davet kodunu kullanıp bağlantı Active olduğunda exam API tarafından
/// bağlantıyla AYNI transaction'da outbox'a yazılır. Tüketici: BadgeService ParentLinkChangedConsumer (#423, bildirimler).
///
/// Güvenlik: id + Keycloak sub + kısa görünen ad; asla e-posta/token/davet kodu (davet kodu ya da hash'i dahil). Ham ad taşınmaz;
/// tüketici sub yoksa kendi verisinden çözer.
/// </summary>
public class ParentLinkedEvent
{
    /// <summary>Idempotency anahtarı — bu bağlantı aktivasyonu için üretilen tek event.</summary>
    public Guid EventId { get; set; }

    /// <summary>ParentStudentLinks.Id.</summary>
    public int LinkId { get; set; }

    /// <summary>Parents.Id.</summary>
    public int ParentId { get; set; }

    /// <summary>Velinin exam/auth user id'si (Parents.UserId).</summary>
    public int ParentUserId { get; set; }

    /// <summary>Students.Id.</summary>
    public int StudentId { get; set; }

    /// <summary>Öğrencinin exam/auth user id'si (Students.UserId).</summary>
    public int StudentUserId { get; set; }

    /// <summary>Velinin Keycloak sub'ı (bildirim hedefi, #423). auth-api çözümü başarısızsa BOŞ; consumer BadgeService verisinden çözer, çözemezse retry/dead-letter.</summary>
    public string ParentKeycloakId { get; set; } = string.Empty;

    /// <summary>Öğrencinin Keycloak sub'ı (#423); boş olabilir (bkz. <see cref="ParentKeycloakId"/>).</summary>
    public string StudentKeycloakId { get; set; } = string.Empty;

    /// <summary>Velinin kısa görünen adı ("Ad S.", #423; PII azaltma — #105 ile aynı biçim); çözülemediyse boş (consumer varsayılan metne düşer).</summary>
    public string ParentDisplayName { get; set; } = string.Empty;

    /// <summary>Öğrencinin kısa görünen adı ("Ad S."); çözülemediyse boş.</summary>
    public string StudentDisplayName { get; set; } = string.Empty;

    /// <summary>Bağlantının Active olduğu an (UTC).</summary>
    public DateTime LinkedAtUtc { get; set; }
}
