using System;
using System.Collections.Generic;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// issue #439: veli gerektiren bir öğrenci randevu talebi oluşturduğunda (talep <c>AwaitingParent</c> durumunda, öğretmene
/// henüz ulaşmadı) exam API tarafından talep satırıyla AYNI transaction'da outbox'a yazılır. BadgeService öğrencinin o an Active
/// bağlantılı her velisine (<see cref="Parents"/>) "onay bekleyen ders talebi" bildirimi üretir (V5 veli bildirimi altyapısı).
/// <para>
/// Tek event / talep: alıcı listesi event'te taşınır. Bildirim idempotency'si consumer'da <c>(EventId, ParentUserId)</c>
/// başına kurulmalıdır (ör. alıcı başına deterministik id) — <c>(Type, SourceEventId)</c> unique index'i tek başına çok
/// alıcılı event için yetmez.
/// </para>
/// Güvenlik: id + Keycloak sub + görünen ad + slot zamanı; e-posta/token/ücret/not taşınmaz.
/// </summary>
public class BookingParentApprovalRequestedEvent
{
    /// <summary>Idempotency anahtarı — talep başına üretilen tek event.</summary>
    public Guid EventId { get; set; }

    /// <summary>Booking.Id (UI derin linki: veli paneli "Onay bekleyen ders talepleri").</summary>
    public int BookingId { get; set; }

    /// <summary>Students.Id.</summary>
    public int StudentId { get; set; }

    /// <summary>Öğrencinin exam/auth user id'si.</summary>
    public int StudentUserId { get; set; }

    /// <summary>Öğrencinin görünen adı (bildirim metni); auth-api'ye erişilemediyse boş.</summary>
    public string StudentName { get; set; } = string.Empty;

    /// <summary>Teachers.Id.</summary>
    public int TeacherId { get; set; }

    /// <summary>Öğretmenin görünen adı (bildirim metni); auth-api'ye erişilemediyse boş.</summary>
    public string TeacherName { get; set; } = string.Empty;

    /// <summary>Ders günü (slot duvar saati, UTC kabul edilir).</summary>
    public DateOnly Date { get; set; }

    public TimeOnly StartTime { get; set; }

    /// <summary>Bitiş; <see cref="StartTime"/>'dan küçükse ertesi gün (gün aşan slot, issue #300).</summary>
    public TimeOnly EndTime { get; set; }

    /// <summary>Talep anında Active bağlantılı veliler (bildirim alıcıları). Aktif velisi olmayan öğrenci talep oluşturamaz.</summary>
    public List<BookingParentRecipient> Parents { get; set; } = new();

    /// <summary>Talebin oluşturulduğu an (UTC).</summary>
    public DateTime RequestedAt { get; set; }

    /// <summary>Bu andan sonra talep otomatik Expired olur (UTC): oluşturma + 7 gün ya da ders başlangıcı, hangisi önceyse.</summary>
    public DateTime ExpiresAt { get; set; }
}
