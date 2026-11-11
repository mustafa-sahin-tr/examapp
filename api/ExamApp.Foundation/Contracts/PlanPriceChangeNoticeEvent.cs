using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Issue #1116 — süren satın almanın yenileme fiyatı değişecek (admin artışı en erken 30 gün sonra geçerli). Exam API'nin
/// PlanPriceChangeJob'ı atamanın "bildirildi" işaretiyle AYNI transaction'da ödeyen başına bir event yazar: öğrenci planında her
/// Active veli (Active velisi yoksa öğrencinin kendisi), öğretmen planında öğretmen. BadgeService in-app bildirim + e-posta üretir.
///
/// TEK ALICI / event (bildirim idempotency'si <c>(Type, SourceEventId)</c>, e-posta <c>NotificationEventLog</c>). Ödenmiş dönemin
/// fiyatı değişmez; yeni fiyat <see cref="EffectiveFromUtc"/>'dan sonraki yenilemede geçerlidir.
/// </summary>
public class PlanPriceChangeNoticeEvent
{
    /// <summary>Idempotency anahtarı — bu alıcı için üretilen tek event.</summary>
    public Guid EventId { get; set; }

    /// <summary>UserSubscriptionPlans.Id (süren satın alma).</summary>
    public int AssignmentId { get; set; }

    /// <summary>Planın adı (admin'in verdiği, ör. "Öğrenci Pro").</summary>
    public string PlanName { get; set; } = string.Empty;

    /// <summary>Satın alınan süre: <c>Monthly</c>, <c>SixMonths</c>, <c>TwelveMonths</c>.</summary>
    public string Duration { get; set; } = string.Empty;

    /// <summary>Bugünkü liste fiyatı (indirim öncesi).</summary>
    public decimal CurrentPrice { get; set; }

    /// <summary>Yürürlük tarihinden sonraki liste fiyatı (indirim öncesi).</summary>
    public decimal NewPrice { get; set; }

    public string Currency { get; set; } = string.Empty;

    /// <summary>Yeni fiyatın geçerli olduğu an (UTC).</summary>
    public DateTime EffectiveFromUtc { get; set; }

    /// <summary>Süren satın almanın bitişi (UTC) — yenileme tarihi.</summary>
    public DateTime RenewsAtUtc { get; set; }

    /// <summary>Alıcı veli ise çocuğun Students.Id'si (derin link <c>/parent?child=&amp;tab=plan</c>); kendi planında null.</summary>
    public int? StudentId { get; set; }

    /// <summary>Veli alıcıda öğrencinin kısa görünen adı ("Ad S."); çözülemediyse boş.</summary>
    public string StudentDisplayName { get; set; } = string.Empty;

    /// <summary>Alıcının exam/auth user id'si — Notification.UserId.</summary>
    public int RecipientUserId { get; set; }

    /// <summary>Alıcının Keycloak sub'ı; boş olabilir — consumer BadgeService verisinden çözer.</summary>
    public string RecipientKeycloakId { get; set; } = string.Empty;

    /// <summary>Alıcının e-postası; boşsa e-posta atlanır (in-app bildirim yine yazılır).</summary>
    public string RecipientEmail { get; set; } = string.Empty;
}
