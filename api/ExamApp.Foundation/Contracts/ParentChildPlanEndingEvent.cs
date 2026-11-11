using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Issue #1091 — öğrencinin planı ya da denemesi birkaç gün içinde bitecek; velisi yeniler. Exam API'nin hatırlatma job'ı
/// (ParentPlanEndingReminderJob) plan atamasının "hatırlatıldı" işaretiyle AYNI transaction'da, her Active veli için ayrı yazar.
/// BadgeService tüketip velinin in-app bildirimini üretir ve SignalR ile push eder.
///
/// TEK ALICI / event (bildirim idempotency'si <c>(Type, SourceEventId)</c>). Güvenlik: id + Keycloak sub + kısa görünen ad.
/// </summary>
public class ParentChildPlanEndingEvent
{
    /// <summary>Idempotency anahtarı — bu veli için üretilen tek event.</summary>
    public Guid EventId { get; set; }

    /// <summary>UserSubscriptionPlans.Id (biten atama).</summary>
    public int AssignmentId { get; set; }

    /// <summary>Biten plan deneme mi ("deneme bitiyor" metni).</summary>
    public bool IsTrial { get; set; }

    /// <summary>
    /// Issue #1110: doluysa bu bir "sınıf bandı değişti" uyarısıdır (bitişten 30 gün önce): çocuk artık bu sınıfta ve yenileme yeni
    /// bandın fiyatıyla olur. Boşsa olağan "plan bitiyor" hatırlatması (#1091).
    /// </summary>
    public int? NewGradeLevel { get; set; }

    /// <summary>Atamanın bitişi (UTC).</summary>
    public DateTime EndsAtUtc { get; set; }

    /// <summary>Students.Id (derin link: <c>/parent?child=&amp;tab=plan</c>).</summary>
    public int StudentId { get; set; }

    /// <summary>Öğrencinin kısa görünen adı ("Ad S."); çözülemediyse boş.</summary>
    public string StudentDisplayName { get; set; } = string.Empty;

    public int ParentId { get; set; }

    /// <summary>Alıcının (velinin) exam/auth user id'si — Notification.UserId.</summary>
    public int ParentUserId { get; set; }

    /// <summary>Velinin Keycloak sub'ı; boş olabilir — consumer BadgeService verisinden çözer.</summary>
    public string ParentKeycloakId { get; set; } = string.Empty;
}
