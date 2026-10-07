using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Issue #423 (epic #407 V5) — bir öğrencinin ödevi (WorksheetAssignment) süresi geçtiği halde tamamlanmadığında exam API'nin
/// süpürücü job'ı (ParentHomeworkOverdueSweepJob) tarafından, (test, öğrenci) işaretçisiyle AYNI transaction'da outbox'a yazılır.
/// BadgeService tüketip velinin in-app bildirimini üretir ve SignalR ile push eder.
///
/// TEK ALICI / event: bildirim idempotency'si <c>(Type, SourceEventId)</c> unique index'i ile kurulduğundan her Active veli için
/// ayrı event (ayrı EventId) yazılır. Yalnızca o anda Active bağlantısı olan veliler için üretilir.
///
/// Güvenlik: id + Keycloak sub + kısa görünen ad; asla e-posta/token/davet kodu.
/// </summary>
public class ParentHomeworkOverdueEvent
{
    /// <summary>Idempotency anahtarı — bu veli için üretilen tek event.</summary>
    public Guid EventId { get; set; }

    /// <summary>Gecikmiş sayılan (en son biten) WorksheetAssignments.Id.</summary>
    public int AssignmentId { get; set; }

    /// <summary>true: çocuğun oturumu süre dolarak kapandı ("süresi doldu" metni); false: hiç/zamanında bitirilmedi.</summary>
    public bool InstanceExpired { get; set; }

    public int WorksheetId { get; set; }

    /// <summary>Sınavın adı (bildirim metni; consumer exam API'ye geri sormaz).</summary>
    public string WorksheetName { get; set; } = string.Empty;

    /// <summary>Students.Id (derin link: <c>/parent?child=</c>).</summary>
    public int StudentId { get; set; }

    /// <summary>Öğrencinin kısa görünen adı ("Ad S."); çözülemediyse boş.</summary>
    public string StudentDisplayName { get; set; } = string.Empty;

    public int ParentId { get; set; }

    /// <summary>Alıcının (velinin) exam/auth user id'si — Notification.UserId.</summary>
    public int ParentUserId { get; set; }

    /// <summary>Velinin Keycloak sub'ı (SignalR hedefi); boş olabilir — consumer BadgeService verisinden çözer, çözemezse retry/dead-letter.</summary>
    public string ParentKeycloakId { get; set; } = string.Empty;

    /// <summary>Atamanın bitiş anı (UTC).</summary>
    public DateTime DueAtUtc { get; set; }
}
