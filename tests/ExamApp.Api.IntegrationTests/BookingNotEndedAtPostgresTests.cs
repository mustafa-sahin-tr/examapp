using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Services.Bookings;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// issue #300 (code review O1): <see cref="SlotTimeRange.BookingNotEndedAt"/> gerçek PostgreSQL'de (date + time kolonları,
/// Npgsql çevirisi) birim testlerdeki SQLite ile aynı sonucu veriyor mu? Askıya alma yan etkisi
/// (<c>AdminTeacherSuspensionService.UpcomingApprovedBookings</c>) bu ifadeyle "henüz bitmemiş" randevuları seçer.
/// "Şimdi" = 2026-06-15 00:10 UTC.
/// </summary>
public class BookingNotEndedAtPostgresTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private static readonly DateTime Now = new(2026, 6, 15, 0, 10, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 6, 15);

    [Fact]
    public async Task Crossing_midnight_rows_are_classified_like_SlotTimeRange_on_postgres()
    {
        var ids = await WithDbAsync(async db =>
        {
            var teacher = new Teacher { UserId = 93001, AccountApprovedAt = DateTime.UtcNow };
            var student = new Student { UserId = 93002, StudentNumber = "S-300" };
            db.AddRange(teacher, student);
            await db.SaveChangesAsync();

            Booking Add(DateOnly date, TimeOnly start, TimeOnly end)
            {
                var booking = new Booking
                {
                    TeacherId = teacher.Id,
                    StudentId = student.Id,
                    AvailabilitySlot = new TeacherAvailabilitySlot
                    {
                        TeacherId = teacher.Id, Date = date, StartTime = start, EndTime = end, CreatedAt = DateTime.UtcNow
                    },
                    Status = BookingStatus.Approved,
                    CreatedAt = DateTime.UtcNow
                };
                db.Bookings.Add(booking);
                return booking;
            }

            var ongoingFromYesterday = Add(Today.AddDays(-1), new(23, 30), new(0, 30)); // dün 23:30 → bugün 00:30, sürüyor
            var tonight = Add(Today, new(23, 30), new(0, 30));                           // bugün 23:30 → yarın 00:30
            var laterToday = Add(Today, new(10, 0), new(11, 0));                          // bugün, henüz başlamadı
            var tomorrow = Add(Today.AddDays(1), new(9, 0), new(10, 0));
            var endedCrossing = Add(Today.AddDays(-1), new(23, 0), new(0, 5));          // dün 23:00 → bugün 00:05, bitti
            var endedYesterday = Add(Today.AddDays(-1), new(22, 0), new(23, 0));        // bitti
            // issue #323: sıfır süreli satır artık DB CHECK constraint'iyle reddediliyor (CK_TeacherAvailabilitySlots_Duration);
            // "sıfır süre → bitmiş" dalı SQLite birim testlerinde (BookingNotEndedAt) kapsanıyor.
            await db.SaveChangesAsync();

            return new
            {
                Expected = new[] { ongoingFromYesterday.Id, tonight.Id, laterToday.Id, tomorrow.Id },
                NotExpected = new[] { endedCrossing.Id, endedYesterday.Id }
            };
        });

        var (sql, matched) = await WithDbAsync(async db =>
        {
            var query = db.Bookings.AsNoTracking()
                .Where(SlotTimeRange.BookingNotEndedAt(Now))
                .Select(b => b.Id);
            return (query.ToQueryString(), await query.ToListAsync());
        });

        TestContext.Current.TestOutputHelper?.WriteLine(sql);

        matched.ShouldBe(ids.Expected, ignoreOrder: true);
        matched.ShouldNotContain(id => ids.NotExpected.Contains(id));

        // Bellekteki kural (SlotTimeRange.From) ile birebir aynı sınıflandırma.
        var rows = await WithDbAsync(db => db.Bookings.AsNoTracking()
            .Select(b => new { b.Id, b.AvailabilitySlot.Date, b.AvailabilitySlot.StartTime, b.AvailabilitySlot.EndTime })
            .ToListAsync());
        var inMemory = rows
            .Where(r =>
            {
                var range = SlotTimeRange.From(r.Date, r.StartTime, r.EndTime);
                return range.IsValid && range.EndUtc > Now;
            })
            .Select(r => r.Id);
        matched.ShouldBe(inMemory, ignoreOrder: true);
    }
}
