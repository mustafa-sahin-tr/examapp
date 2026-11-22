using System;
using System.Collections.Generic;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Issue #1281 (#1071 S6) — okul duyurusu gönderildiğinde exam API'nin <c>SchoolAnnouncementService</c>'i duyuru satırıyla AYNI
/// transaction'da yazar. Alıcı listesi producer'da çözülür (hedeflenen öğrenciler, okul geneli için okulun öğretmenleri, hedeflenen
/// öğrencilerin Active velileri; gönderen hariç, kullanıcı başına tek alıcı) ve en çok <see cref="MaxRecipientsPerEvent"/>
/// alıcılık parçalara bölünür: binlerce alıcıda tek dev mesaj yok. BadgeService her parçayı toplu yazar.
///
/// Idempotency: her alıcı producer'ın ürettiği kendi <see cref="AnnouncementRecipient.NotificationId"/>'sini taşır; consumer
/// bunu <c>Notification.SourceEventId</c> olarak yazar (<c>(Type, SourceEventId)</c> tekil) → yeniden teslim aynı satırları atlar.
///
/// Güvenlik: id, Keycloak sub, kısa görünen adlar, duyuru metni; e-posta / öğrenci numarası yok.
/// </summary>
public class AnnouncementPublishedEvent
{
    public const string StudentAudience = "Student";
    public const string TeacherAudience = "Teacher";
    public const string ParentAudience = "Parent";

    /// <summary>Bir event'teki en çok alıcı (parça boyutu).</summary>
    public const int MaxRecipientsPerEvent = 500;

    public Guid EventId { get; set; }

    public int AnnouncementId { get; set; }

    public int SchoolId { get; set; }

    public string SchoolName { get; set; } = string.Empty;

    /// <summary>Gönderenin kısa görünen adı ("Ad S."); çözülemediyse boş.</summary>
    public string SenderDisplayName { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    /// <summary>Parça sırası (0'dan) ve toplam parça; log / izleme için.</summary>
    public int ChunkIndex { get; set; }

    public int ChunkCount { get; set; }

    public DateTime PublishedAtUtc { get; set; }

    public List<AnnouncementRecipient> Recipients { get; set; } = new();
}

/// <summary>Duyurunun tek alıcısı.</summary>
public class AnnouncementRecipient
{
    /// <summary>Bu alıcının bildirimi için idempotency anahtarı (<c>Notification.SourceEventId</c>).</summary>
    public Guid NotificationId { get; set; }

    /// <summary><see cref="AnnouncementPublishedEvent.StudentAudience"/>, <c>TeacherAudience</c> ya da <c>ParentAudience</c>.</summary>
    public string Audience { get; set; } = string.Empty;

    public int UserId { get; set; }

    /// <summary>Alıcının Keycloak sub'ı; boş olabilir (consumer BadgeService verisinden çözer).</summary>
    public string KeycloakId { get; set; } = string.Empty;

    /// <summary>Veli alıcısında çocuğun Students.Id'si (birden çok hedeflenmiş çocukta ilki); diğerlerinde null.</summary>
    public int? StudentId { get; set; }

    /// <summary>Veli alıcısında tek hedeflenmiş çocuğun kısa adı; birden çok çocuk ya da çözülemediyse boş.</summary>
    public string StudentDisplayName { get; set; } = string.Empty;
}
