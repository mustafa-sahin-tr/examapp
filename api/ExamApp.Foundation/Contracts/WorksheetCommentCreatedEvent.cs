using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Issue #105 (dilim 2) — bir ÖĞRENCİ worksheet/soru thread'ine kök yorum veya reply yazdığında exam API
/// tarafından yorumla AYNI transaction'da outbox'a yazılır. Alıcı bir ÖĞRETMENdir (kökte sabitlenmiş ilgili
/// öğretmen ya da öğretmen kökünün yazarı). BadgeService tüketip öğretmene in-app bildirim üretir ve SignalR
/// ile push eder.
///
/// TEK ALICI / event: alıcı başına ayrı outbox satırı (ayrı <see cref="EventId"/>) yazılır. Bildirim
/// idempotency'si BadgeService'te mevcut filtreli unique index <c>(Type, SourceEventId)</c> ile kurulduğu
/// için event başına tek alıcı gerekir (alıcı listesi bu index'i ihlal ederdi).
///
/// Güvenlik: yorum GÖVDESİ, e-posta ve token taşınmaz — yalnızca id'ler, başlık (PII değil), rol, kısa görünen
/// ad ("Ad S.") ve alıcının hedefleme kimlikleri. Consumer gerekirse detayı exam API'den okur.
/// </summary>
public class WorksheetCommentCreatedEvent
{
    /// <summary>Idempotency anahtarı — bu (yorum, alıcı) çifti için üretilen tek event'i tekilleştirir.</summary>
    public Guid EventId { get; set; }

    public int CommentId { get; set; }

    /// <summary>Thread'in kök yorumu; kök yorumun kendisi için <see cref="CommentId"/> ile aynıdır.</summary>
    public int RootCommentId { get; set; }

    public int WorksheetId { get; set; }

    /// <summary>Soru bazlı thread ise soru id'si; worksheet seviyesinde null.</summary>
    public int? QuestionId { get; set; }

    /// <summary>Worksheet adı (bildirim metni için); PII değil.</summary>
    public string WorksheetTitle { get; set; } = string.Empty;

    /// <summary>Yazarın rolü: "Student" (bu event'te her zaman öğrenci).</summary>
    public string AuthorRole { get; set; } = string.Empty;

    /// <summary>Yazarın kısa görünen adı ("Ad S."); boş olabilir (consumer varsayılan metne düşer).</summary>
    public string AuthorDisplayName { get; set; } = string.Empty;

    /// <summary>Alıcı öğretmenin exam/auth user id'si (Notification.UserId).</summary>
    public int RecipientUserId { get; set; }

    /// <summary>
    /// Alıcının Keycloak subject'i (SignalR <c>Clients.User(...)</c> hedefi). Exam DB'de sub yoksa (ör. öğretmen)
    /// BOŞ gelir; consumer BadgeService verisinden çözer, çözemezse fırlatır (retry/dead-letter). Boş sub'la Notification yazılmaz.
    /// </summary>
    public string RecipientKeycloakId { get; set; } = string.Empty;

    /// <summary>Yorumun oluşturulduğu an (UTC).</summary>
    public DateTime CreatedAtUtc { get; set; }
}
