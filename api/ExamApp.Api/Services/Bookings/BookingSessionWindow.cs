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
/// başlangıç −15 dk, bitiş +30 dk). Slot tarih-saatleri UTC kabul edilir (<see cref="BookingService.ToUtc"/>).
/// </summary>
public readonly record struct BookingSessionWindow(
    DateTime StartUtc, DateTime EndUtc, DateTime OpensAtUtc, DateTime ClosesAtUtc)
{
    public static BookingSessionWindow For(DateOnly date, TimeOnly startTime, TimeOnly endTime, VideoOptions options)
    {
        var startUtc = BookingService.ToUtc(date, startTime);
        var endUtc = BookingService.ToUtc(date, endTime);
        return new BookingSessionWindow(
            startUtc,
            endUtc,
            startUtc.AddMinutes(-options.JoinWindowBeforeMinutes),
            endUtc.AddMinutes(options.JoinWindowAfterMinutes));
    }

    /// <summary>Sınırlar dahil: tam açılış ve tam kapanış anı <see cref="BookingWindowState.Open"/>'dır (#97 davranışı).</summary>
    public BookingWindowState StateAt(DateTime nowUtc)
        => nowUtc < OpensAtUtc ? BookingWindowState.NotOpen
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
    WindowClosed = 5
}

/// <summary>
/// Canlı ders oturumu (görüşme / çizim tahtası) katılım kararı. <see cref="Denial"/> <see cref="BookingLiveSessionDenial.None"/>
/// ise çağıran randevunun öğretmeni ya da öğrencisidir, randevu Approved'dır ve pencere açıktır. Öğretmen hesabının
/// onay/askı durumu (#287/#289) burada DEĞİL, çağıranda (policy veya hub erişim servisi) kontrol edilir.
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
