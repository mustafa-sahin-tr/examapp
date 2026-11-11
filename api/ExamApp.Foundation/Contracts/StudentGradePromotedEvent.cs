using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Issue #1110 — 1 Ağustos sınıf atlaması: exam API'nin <c>StudentGradePromotionJob</c>'ı öğrencinin sınıfını bir üst sınıfa
/// taşıdığında, sınıf güncellemesi ve <c>StudentGradePromotions</c> satırıyla AYNI transaction'da yazar. TEK ALICI / event:
/// öğrencinin kendisi için bir tane, o an Active bağlantılı her velisi için ayrı bir tane (<see cref="SchoolChangedEvent"/> deseni).
/// BadgeService uygulama içi bildirim üretir ("Artık 7. sınıftasın; içerikler yeni sınıfına göre").
///
/// Güvenlik: id, Keycloak sub, sınıf seviyesi, kısa görünen ad; e-posta yok.
/// </summary>
public class StudentGradePromotedEvent
{
    public const string StudentAudience = "Student";
    public const string ParentAudience = "Parent";

    /// <summary>Idempotency anahtarı — bu alıcı için üretilen tek event.</summary>
    public Guid EventId { get; set; }

    /// <summary>Alıcı: <see cref="StudentAudience"/> (öğrencinin kendisi) ya da <see cref="ParentAudience"/>.</summary>
    public string Audience { get; set; } = string.Empty;

    public int RecipientUserId { get; set; }

    /// <summary>Alıcının Keycloak sub'ı; boş olabilir (consumer BadgeService verisinden çözer).</summary>
    public string RecipientKeycloakId { get; set; } = string.Empty;

    /// <summary>Students.Id (veli bildiriminin derin linki: <c>/parent?child=</c>).</summary>
    public int StudentId { get; set; }

    /// <summary>Veli alıcısı için çocuğun kısa görünen adı ("Ad S."); çözülemediyse ya da öğrenci alıcısında boş.</summary>
    public string StudentDisplayName { get; set; } = string.Empty;

    /// <summary>Atlama yılı (1 Ağustos'un yılı).</summary>
    public int Year { get; set; }

    /// <summary>Yeni sınıf seviyesi (2–12).</summary>
    public int ToGradeLevel { get; set; }

    public DateTime PromotedAtUtc { get; set; }
}
