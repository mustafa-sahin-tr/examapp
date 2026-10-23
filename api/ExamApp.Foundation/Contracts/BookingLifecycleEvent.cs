using System;
using System.Collections.Generic;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// issue #315 — randevu iptali, erteleme önerisi/kararı ve no-show bildirimi. exam API outbox'a yazar; BadgeService
/// <c>BookingLifecycleConsumer</c> karşı tarafa (ve öğrencinin Active velilerine) in-app bildirim üretir.
/// <para>
/// Tek event tipi, <see cref="Kind"/> ile ayrılır (<see cref="BookingLifecycleKinds"/>) — beş ayrı exchange/izin yerine tek
/// RabbitMQ exchange'i. Payload yalnız id'ler, görünen adlar, Keycloak sub'ları, zaman bilgisi ve (iptalde) kullanıcı
/// gerekçesi taşır.
/// </para>
/// </summary>
public class BookingLifecycleEvent
{
    /// <summary>Idempotency anahtarı (alıcı başına deterministik bildirim anahtarı bundan türetilir).</summary>
    public Guid EventId { get; set; }

    public int BookingId { get; set; }

    /// <summary><see cref="BookingLifecycleKinds"/> değeri.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>İşlemi yapan taraf: "Teacher" | "Student".</summary>
    public string ActorRole { get; set; } = string.Empty;

    public int TeacherId { get; set; }
    public int TeacherUserId { get; set; }
    public string TeacherName { get; set; } = string.Empty;
    public string TeacherKeycloakId { get; set; } = string.Empty;

    public int StudentId { get; set; }
    public int StudentUserId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string StudentKeycloakId { get; set; } = string.Empty;

    /// <summary>Öğretmene bildirim gider mi (ör. öğretmene hiç ulaşmamış AwaitingParent talebin iptalinde false).</summary>
    public bool NotifyTeacher { get; set; }

    /// <summary>Öğrenciye bildirim gider mi.</summary>
    public bool NotifyStudent { get; set; }

    /// <summary>İptal gerekçesi (opsiyonel, kullanıcı metni, en fazla 500).</summary>
    public string? Reason { get; set; }

    /// <summary>Randevunun (işlem öncesi) mevcut slotu.</summary>
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    /// <summary>Erteleme olaylarında önerilen / yeni slot; diğerlerinde null.</summary>
    public DateOnly? NewDate { get; set; }
    public TimeOnly? NewStartTime { get; set; }
    public TimeOnly? NewEndTime { get; set; }

    /// <summary>NoShowReported'da katılmadığı bildirilen taraf ("Teacher" | "Student").</summary>
    public string? NoShowParty { get; set; }

    /// <summary>Öğrencinin Active velileri (işlemle aynı transaction'da FOR SHARE ile doğrulanmış).</summary>
    public List<BookingParentRecipient> Parents { get; set; } = new();

    public DateTime OccurredAt { get; set; }
}

/// <summary><see cref="BookingLifecycleEvent.Kind"/> değerleri.</summary>
public static class BookingLifecycleKinds
{
    public const string Cancelled = "Cancelled";
    public const string RescheduleProposed = "RescheduleProposed";
    public const string RescheduleAccepted = "RescheduleAccepted";
    public const string RescheduleDeclined = "RescheduleDeclined";
    public const string RescheduleWithdrawn = "RescheduleWithdrawn";
    public const string NoShowReported = "NoShowReported";
}
