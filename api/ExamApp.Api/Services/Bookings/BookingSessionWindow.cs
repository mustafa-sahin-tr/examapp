using System;
using ExamApp.Api.Services.Video;

namespace ExamApp.Api.Services.Bookings;

/// <summary>Bir andaki katılım penceresi durumu.</summary>
public enum BookingWindowState
{
    /// <summary>Pencere henüz açılmadı (başlangıç − <see cref="VideoOptions.JoinWindowBeforeMinutes"/> öncesi).</summary>
    NotOpen = 0,

    /// <summary>Pencere açık (sınırlar dahil).</summary>
    Open = 1,

    /// <summary>Pencere kapandı (bitiş + <see cref="VideoOptions.JoinWindowAfterMinutes"/> sonrası).</summary>
    Closed = 2
}

/// <summary>
/// Canlı ders oturumunun (görüşme odası #97, ortak çizim tahtası #98) katılım penceresi. Tek kaynak:
/// <see cref="VideoOptions.JoinWindowBeforeMinutes"/> / <see cref="VideoOptions.JoinWindowAfterMinutes"/> (varsayılan
/// başlangıç −15 dk, bitiş +30 dk). Slot tarih-saatleri UTC kabul edilir; gün aşan slotta (bitiş &lt; başlangıç) bitiş
/// ertesi gündür (<see cref="SlotTimeRange.From"/>, issue #300) — ör. 23:30–00:30 penceresi 23:15'te açılır, ertesi gün
/// 01:00'de kapanır.
/// <para>
/// Savunma derinliği (security review O1/L3): pencere hesaplanırken ders süresi
/// <see cref="BookingService.MaxSlotDuration"/> ile kırpılır — veritabanındaki hatalı/uzun bir satır katılımı başlangıç
/// + 4 saat + <see cref="VideoOptions.JoinWindowAfterMinutes"/>'ten öteye açık tutamaz. Sıfır süreli (geçersiz) satırın
/// penceresi hiç açılmaz (<see cref="StateAt"/> daima <see cref="BookingWindowState.Closed"/>).
/// </para>
/// </summary>
public readonly record struct BookingSessionWindow(
    DateTime StartUtc, DateTime EndUtc, DateTime OpensAtUtc, DateTime ClosesAtUtc)
{
    public static BookingSessionWindow For(DateOnly date, TimeOnly startTime, TimeOnly endTime, VideoOptions options)
    {
        var range = SlotTimeRange.From(date, startTime, endTime);
        var maxEndUtc = range.StartUtc + BookingService.MaxSlotDuration;
        var endUtc = range.EndUtc > maxEndUtc ? maxEndUtc : range.EndUtc;
        return new BookingSessionWindow(
            range.StartUtc,
            endUtc,
            range.StartUtc.AddMinutes(-options.JoinWindowBeforeMinutes),
            endUtc.AddMinutes(options.JoinWindowAfterMinutes));
    }

    /// <summary>Pozitif süreli ders mi? Sıfır süreli (hatalı) satırın penceresi yoktur.</summary>
    public bool IsValid => EndUtc > StartUtc;

    /// <summary>
    /// Sınırlar dahil: tam açılış ve tam kapanış anı <see cref="BookingWindowState.Open"/>'dır (#97 davranışı).
    /// Geçersiz (sıfır süreli) pencere daima <see cref="BookingWindowState.Closed"/>.
    /// </summary>
    public BookingWindowState StateAt(DateTime nowUtc)
        => !IsValid ? BookingWindowState.Closed
            : nowUtc < OpensAtUtc ? BookingWindowState.NotOpen
            : nowUtc > ClosesAtUtc ? BookingWindowState.Closed
            : BookingWindowState.Open;
}

/// <summary><see cref="IBookingService.GetLiveSessionAccessAsync"/> red nedeni.</summary>
public enum BookingLiveSessionDenial
{
    None = 0,
    NotFound = 1,
    NotParticipant = 2,
    NotApproved = 3,
    WindowNotOpen = 4,
    WindowClosed = 5,

    /// <summary>
    /// issue #298: randevunun öğretmeninin hesabı onaylı değil ya da askıda (<c>IApprovedTeacherGuard</c> ile aynı karar).
    /// Öğretmene de öğrenciye de verilir; askı kalkınca (pencere içindeyse) erişim kendiliğinden geri açılır.
    /// </summary>
    TeacherUnavailable = 6
}

/// <summary>
/// Canlı ders oturumu (görüşme / çizim tahtası) katılım kararı. <see cref="Denial"/> <see cref="BookingLiveSessionDenial.None"/>
/// ise çağıran randevunun öğretmeni ya da öğrencisidir, randevu Approved'dır, randevunun öğretmeni onaylı ve askıda
/// değildir (issue #298, <see cref="BookingLiveSessionDenial.TeacherUnavailable"/>) ve pencere açıktır. ÇAĞIRANIN kendi
/// öğretmen hesabı (Teacher rolü, #287/#289) ayrıca çağıranda (policy veya hub erişim servisi) kontrol edilir.
/// </summary>
public sealed record BookingLiveSessionAccess(
    BookingLiveSessionDenial Denial,
    int BookingId,
    bool IsTeacher,
    int TeacherUserId,
    int StudentUserId,
    BookingSessionWindow Window)
{
    public bool Allowed => Denial == BookingLiveSessionDenial.None;

    public static BookingLiveSessionAccess Denied(BookingLiveSessionDenial denial, int bookingId)
        => new(denial, bookingId, false, 0, 0, default);
}
