using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Issue #106 (dilim b) — öğrenci ↔ öğretmen doğrudan mesajı kalıcı olarak yazıldığında exam API tarafından mesajla
/// AYNI transaction'da outbox'a yazılır. Alıcı mesajın KARŞI tarafıdır: öğrenci yazdıysa öğretmen, öğretmen yazdıysa öğrenci.
/// BadgeService tüketip alıcıya in-app bildirim üretir ve SignalR ile push eder (aynı konuşmadaki okunmamış bildirimler birleşir).
///
/// TEK ALICI / event: bildirim idempotency'si <c>(Type, SourceEventId)</c> unique index'i ile kurulduğundan event başına
/// tek alıcı vardır (mesaj başına tek event zaten tek alıcıdır).
///
/// Güvenlik: mesaj GÖVDESİ, e-posta ve token taşınmaz. Yalnızca id'ler, rol, kısa görünen ad ("Ad S.") ve alıcının
/// hedefleme kimlikleri taşınır. Engel/ilişki/kota yüzünden reddedilen gönderim yazılmadığı için event de üretmez.
/// </summary>
public class DirectMessageSentEvent
{
    /// <summary>Idempotency anahtarı — bu mesaj için üretilen tek event'i tekilleştirir.</summary>
    public Guid EventId { get; set; }

    public int ConversationId { get; set; }

    public int MessageId { get; set; }

    /// <summary>Gönderenin rolü: "Student" ya da "Teacher" (alıcı rolü ve derin link buradan türetilir).</summary>
    public string SenderRole { get; set; } = string.Empty;

    /// <summary>Gönderenin kısa görünen adı ("Ad S."); çözülemediyse boş (consumer yerelleştirilmiş varsayılana düşer).</summary>
    public string SenderDisplayName { get; set; } = string.Empty;

    /// <summary>Alıcının exam user id'si (Notification.UserId).</summary>
    public int RecipientUserId { get; set; }

    /// <summary>
    /// Alıcının Keycloak subject'i (SignalR <c>Clients.User(...)</c> hedefi). auth-api çözümü başarısızsa BOŞ gelir; consumer
    /// BadgeService verisinden çözer, çözemezse fırlatır (retry/dead-letter). Boş sub'la Notification yazılmaz.
    /// </summary>
    public string RecipientKeycloakId { get; set; } = string.Empty;

    /// <summary>Mesajın yazıldığı an (UTC).</summary>
    public DateTime CreatedAtUtc { get; set; }
}
