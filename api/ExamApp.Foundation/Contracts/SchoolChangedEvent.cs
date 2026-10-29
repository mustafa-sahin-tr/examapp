using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Issue #542 (#194 K5) — admin bir öğrencinin ya da öğretmenin okulunu GERÇEKTEN değiştirdiğinde (<c>changed=true</c> ve okul farklı)
/// exam API'nin <c>AdminStudentSchoolService</c> / <c>AdminTeacherSchoolService</c>'i okul yazımıyla AYNI transaction'da yazar.
/// TEK ALICI / event: kişinin kendisi için bir tane, öğrencinin o an Active bağlantılı her velisi için ayrı bir tane
/// (<see cref="ParentChildTestCompletedEvent"/> deseni). BadgeService uygulama içi bildirim üretir.
///
/// Güvenlik: id, Keycloak sub, okul adı, kısa görünen ad; e-posta / admin kimliği yok.
/// </summary>
public class SchoolChangedEvent
{
    public const string StudentAudience = "Student";
    public const string TeacherAudience = "Teacher";
    public const string ParentAudience = "Parent";

    /// <summary>Idempotency anahtarı — bu alıcı için üretilen tek event.</summary>
    public Guid EventId { get; set; }

    /// <summary>Alıcı: <see cref="StudentAudience"/> (öğrencinin kendisi), <see cref="TeacherAudience"/> ya da <see cref="ParentAudience"/>.</summary>
    public string Audience { get; set; } = string.Empty;

    public int RecipientUserId { get; set; }

    /// <summary>Alıcının Keycloak sub'ı; boş olabilir (consumer BadgeService verisinden çözer).</summary>
    public string RecipientKeycloakId { get; set; } = string.Empty;

    /// <summary>Okulu değişen kişinin exam kullanıcı id'si (öğrenci ya da öğretmen).</summary>
    public int SubjectUserId { get; set; }

    /// <summary>Okulu değişen öğrencinin Students.Id'si; öğretmende null.</summary>
    public int? StudentId { get; set; }

    /// <summary>Veli alıcısı için çocuğun kısa görünen adı ("Ad S."); çözülemediyse ya da başka alıcıda boş.</summary>
    public string StudentDisplayName { get; set; } = string.Empty;

    /// <summary>Önceki okul; okulsuzdu ise null.</summary>
    public int? FromSchoolId { get; set; }

    public string FromSchoolName { get; set; } = string.Empty;

    /// <summary>Yeni okul; okulsuz yapıldıysa (#541) null.</summary>
    public int? ToSchoolId { get; set; }

    public string ToSchoolName { get; set; } = string.Empty;

    /// <summary>Öğretmen transferinde özel yapılan "Okulum" sınavı sayısı (#539); diğerlerinde 0.</summary>
    public int PrivatizedWorksheetCount { get; set; }

    public DateTime ChangedAtUtc { get; set; }
}
