using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Issue #105 (dilim 2) — bir thread'e yazılan reply'ın, kök yorumun ÖĞRENCİ yazarına bildirimi. Öğretmen
/// cevabında ve (kök yazarı başkasıysa) başka bir öğrencinin reply'ında exam API tarafından yorumla AYNI
/// transaction'da outbox'a yazılır. BadgeService tüketip öğrenciye in-app bildirim üretir ve SignalR ile push eder.
///
/// TEK ALICI / event (bkz. <see cref="WorksheetCommentCreatedEvent"/> — (Type, SourceEventId) unique index).
/// Öğretmen kökü için bildirim üretilmez (MVP). Yorum gövdesi, e-posta ve token taşınmaz.
/// </summary>
public class WorksheetCommentRepliedEvent
{
    /// <summary>Idempotency anahtarı — bu (yorum, alıcı) çifti için üretilen tek event'i tekilleştirir.</summary>
    public Guid EventId { get; set; }

    public int CommentId { get; set; }

    /// <summary>Thread'in kök yorumu (alıcının yazdığı yorum).</summary>
    public int RootCommentId { get; set; }

    public int WorksheetId { get; set; }

    /// <summary>Soru bazlı thread ise soru id'si; worksheet seviyesinde null.</summary>
    public int? QuestionId { get; set; }

    /// <summary>Worksheet adı (bildirim metni için); PII değil.</summary>
    public string WorksheetTitle { get; set; } = string.Empty;

    /// <summary>Reply yazarının rolü: "Teacher" (öğretmen cevabı) veya "Student" (başka öğrencinin reply'ı).</summary>
    public string AuthorRole { get; set; } = string.Empty;

    /// <summary>Öğrenci yazar için kısa görünen ad ("Ad S."); öğretmen cevabında boş (metin "Öğretmenin" der).</summary>
    public string AuthorDisplayName { get; set; } = string.Empty;

    /// <summary>Alıcı öğrencinin (kök yazarı) exam/auth user id'si (Notification.UserId).</summary>
    public int RecipientUserId { get; set; }

    /// <summary>
    /// Alıcının Keycloak subject'i (SignalR <c>Clients.User(...)</c> hedefi). Exam DB'de yoksa BOŞ gelir;
    /// consumer BadgeService verisinden çözer, çözemezse fırlatır (retry/dead-letter). Boş sub'la Notification yazılmaz.
    /// </summary>
    public string RecipientKeycloakId { get; set; } = string.Empty;

    /// <summary>Reply'ın oluşturulduğu an (UTC).</summary>
    public DateTime CreatedAtUtc { get; set; }
}
