using System;
using System.Collections.Generic;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// issue #439: veli onayı bekleyen (<c>AwaitingParent</c>) bir randevu talebinin veli tarafındaki sonucu. Exam API karar
/// satırıyla AYNI transaction'da outbox'a yazar; BadgeService öğrenciye (<see cref="StudentKeycloakId"/>) sonucu, velilere
/// (<see cref="Parents"/>) bilgi bildirimini üretir.
/// <list type="bullet">
/// <item><see cref="BookingParentDecisions.Approved"/>: bir veli onayladı → talep öğretmene gitti (Pending). Öğretmenin sonraki
/// kararı ayrıca <see cref="BookingDecisionEvent"/> ile gelir.</item>
/// <item><see cref="BookingParentDecisions.Rejected"/>: bir veli reddetti (<see cref="Note"/> opsiyonel).</item>
/// <item><see cref="BookingParentDecisions.Expired"/>: 7 gün içinde (ya da ders saatine kadar) karar verilmedi; sistem kapattı,
/// <see cref="DecidedByParentUserId"/> null.</item>
/// </list>
/// İlk karar kazanır: koşullu UPDATE 0 satır dönerse event yazılmaz → talep başına en fazla bir event.
/// <see cref="Parents"/> kararı veren veliyi de içerir (consumer isterse onu <see cref="DecidedByParentUserId"/> ile atlar).
/// </summary>
public class BookingParentDecisionEvent
{
    /// <summary>Idempotency anahtarı — talep başına tek karar event'i.</summary>
    public Guid EventId { get; set; }

    public int BookingId { get; set; }

    /// <summary><see cref="BookingParentDecisions"/>: "Approved" | "Rejected" | "Expired".</summary>
    public string Decision { get; set; } = string.Empty;

    public int StudentId { get; set; }

    /// <summary>Öğrencinin exam/auth user id'si — öğrenci bildiriminin Notification.UserId'si.</summary>
    public int StudentUserId { get; set; }

    /// <summary>Öğrencinin Keycloak sub'ı (SignalR hedefi); boş olabilir.</summary>
    public string StudentKeycloakId { get; set; } = string.Empty;

    /// <summary>Öğrencinin görünen adı (veli bildirim metni); boş olabilir.</summary>
    public string StudentName { get; set; } = string.Empty;

    /// <summary>Karar anında Active bağlantılı veliler.</summary>
    public List<BookingParentRecipient> Parents { get; set; } = new();

    /// <summary>Kararı veren velinin user id'si; Expired'da null.</summary>
    public int? DecidedByParentUserId { get; set; }

    /// <summary>Kararı veren velinin görünen adı; Expired'da ya da çözülemezse null.</summary>
    public string? DecidedByParentName { get; set; }

    public int TeacherId { get; set; }

    /// <summary>Öğretmenin görünen adı; boş olabilir.</summary>
    public string TeacherName { get; set; } = string.Empty;

    public DateOnly Date { get; set; }

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }

    /// <summary>Velinin ret notu (yalnız Rejected, en fazla 500 karakter).</summary>
    public string? Note { get; set; }

    /// <summary>Karar / süre aşımı anı (UTC).</summary>
    public DateTime DecidedAt { get; set; }
}

/// <summary><see cref="BookingParentDecisionEvent.Decision"/> değerleri.</summary>
public static class BookingParentDecisions
{
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Expired = "Expired";
}
