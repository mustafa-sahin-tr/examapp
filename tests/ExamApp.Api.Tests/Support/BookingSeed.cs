using ExamApp.Api.Data;

namespace ExamApp.Api.Tests.Support;

/// <summary>
/// issue #192 testleri için Booking seed yardımcısı: her booking'e kendi (Teacher, tarih, saat) slotunu açar ki
/// "slot başına tek aktif booking" filtreli unique index'ine takılmasın. Kaydetme çağıranın işi (SaveChangesAsync).
/// </summary>
public static class BookingSeed
{
    private static readonly DateOnly DefaultDate = new(2026, 10, 1);

    /// <summary>
    /// <paramref name="teacherId"/> için <paramref name="hour"/>:00-<paramref name="hour"/>+1:00 slotu açar ve
    /// öğrenciye <paramref name="status"/> durumlu booking ekler. Aynı öğretmen için farklı saat ver.
    /// </summary>
    public static Booking Add(AppDbContext ctx, int teacherId, int studentId, BookingStatus status, int hour, DateOnly? date = null)
    {
        var slot = new TeacherAvailabilitySlot
        {
            TeacherId = teacherId,
            Date = date ?? DefaultDate,
            StartTime = new TimeOnly(hour, 0),
            EndTime = new TimeOnly(hour + 1, 0),
            CreatedAt = DateTime.UtcNow,
        };
        ctx.TeacherAvailabilitySlots.Add(slot);

        var booking = new Booking
        {
            TeacherId = teacherId,
            StudentId = studentId,
            AvailabilitySlot = slot,
            Status = status,
            CreatedAt = DateTime.UtcNow,
        };
        ctx.Bookings.Add(booking);
        return booking;
    }
}
