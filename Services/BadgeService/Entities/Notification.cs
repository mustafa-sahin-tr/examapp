using System;

namespace BadgeService.Entities;

/// <summary>
/// Kullanıcıya gösterilecek in-app bildirim. Outbox event'lerini tüketen consumer'lar
/// (örn. <c>WorksheetReminderDueConsumer</c>) buraya satır yazar; frontend zil bileşeni okur.
/// </summary>
public class Notification
{
    public int Id { get; set; }

    /// <summary>exam/auth user id (BadgeService genelinde kullanılan UserId ile aynı).</summary>
    public int UserId { get; set; }

    /// <summary>
    /// Öğrencinin Keycloak subject'i. Sahiplik kontrolü (IDOR koruması) ve SignalR
    /// hedeflemesi bunun üzerinden yapılır; API sorguları çağıranın sub'ı ile filtrelenir.
    /// </summary>
    public string? UserKeycloakId { get; set; }

    public string Type { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    /// <summary>Serbest biçimli JSON — client'ın derin link kurması için (worksheetId vb.).</summary>
    public string? Data { get; set; }

    /// <summary>
    /// Idempotency anahtarı: bir reminder'dan üretilen bildirim tekilliğini sağlar.
    /// Reminder kaynaklı olmayan bildirimlerde null.
    /// </summary>
    public int? SourceReminderId { get; set; }

    /// <summary>
    /// Idempotency anahtarı: bir atama izni talebinden (WorksheetAccessRequest) üretilen bildirim
    /// tekilliğini sağlar. Talep/karar kaynaklı olmayan bildirimlerde null.
    /// </summary>
    public int? SourceAccessRequestId { get; set; }

    /// <summary>
    /// Idempotency anahtarı (yalnızca <c>Type == "TeacherApplicationSubmitted"</c> için, bkz.
    /// BadgeDbContext'teki filtreli unique index): bağımsız öğretmen başvurusundan (Teacher.Id,
    /// issue #94) üretilen Admin bildiriminin tekilliğini sağlar. Karar bildirimlerinde (issue #157,
    /// <see cref="SourceEventId"/> kullanır) yalnızca referans amaçlı doldurulur, tekillik burada
    /// KURULMAZ — aynı öğretmen birden fazla kez başvurup karar alabilir (ör. red sonrası yeni okul
    /// talebi), bu durumda ikinci karar da (farklı Type+TeacherId kombinasyonu paylaşılsa da farklı
    /// EventId ile) bildirilmelidir.
    /// </summary>
    public int? SourceTeacherApplicationId { get; set; }

    /// <summary>
    /// Idempotency anahtarı (issue #157): outbox event'inin kendi Guid kimliği (ör.
    /// <see cref="ExamApp.Foundation.Contracts.TeacherApplicationDecidedEvent.EventId"/>). Aynı öğretmen
    /// için birden fazla karar üretilebildiğinden (Teacher.Id tek başına tekillik için yetersiz) karar
    /// bildirimleri burada tekilleştirilir; TeacherId/Type'a göre değil, kararın kendisine göre dedup
    /// yapılır. Event kimliği taşımayan eski event'lerde (TeacherApplicationSubmitted vb.) null kalır.
    /// </summary>
    public Guid? SourceEventId { get; set; }

    /// <summary>
    /// Idempotency anahtarı: bir randevu talebinden (Booking.Id, issue #96) üretilen bildirimin
    /// tekilliğini sağlar. Hem talep hem karar bildirimleri aynı BookingId'yi taşır; tekillik
    /// <see cref="Type"/> ile birlikte kontrol edilir. Booking kaynaklı olmayan bildirimlerde null.
    /// </summary>
    public int? SourceBookingId { get; set; }

    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
