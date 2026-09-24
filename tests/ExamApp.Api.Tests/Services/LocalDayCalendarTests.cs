using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #265: dashboard pencereleri yerel (varsayılan Europe/Istanbul) takvim günüdür — TR 00:00-03:00 arası artık
/// bir önceki UTC gününe sayılmaz.
/// </summary>
public class LocalDayCalendarTests
{
    private static LocalDayCalendar Istanbul(DateTimeOffset now)
        => new(LocalDayCalendar.DefaultTimeZoneId, new FixedTimeProvider(now));

    private static DateTime Utc(int y, int mo, int d, int h, int mi = 0) => new(y, mo, d, h, mi, 0, DateTimeKind.Utc);

    [Fact]
    public void Today_is_the_istanbul_date_even_when_utc_is_still_the_previous_day()
    {
        // 2026-09-24 22:30 UTC = 2026-09-25 01:30 TR.
        var calendar = Istanbul(new DateTimeOffset(Utc(2026, 9, 24, 22, 30)));

        calendar.Today.ShouldBe(new DateOnly(2026, 9, 25));
    }

    [Fact]
    public void Window_starts_at_local_midnight_expressed_in_utc()
    {
        var calendar = Istanbul(new DateTimeOffset(Utc(2026, 9, 24, 22, 30)));

        var window = calendar.LastDays(7);

        window.FirstDay.ShouldBe(new DateOnly(2026, 9, 19));
        window.LastDay.ShouldBe(new DateOnly(2026, 9, 25));
        window.Days.ShouldBe(7);
        window.StartUtc.ShouldBe(Utc(2026, 9, 18, 21));      // 19 Eylül 00:00 TR
        window.StartUtc.Kind.ShouldBe(DateTimeKind.Utc);      // Npgsql timestamptz karşılaştırması için şart
        window.EndUtc.ShouldBe(Utc(2026, 9, 25, 21));        // 26 Eylül 00:00 TR (hariç)
    }

    [Fact]
    public void Istanbul_window_is_a_single_constant_offset_segment()
    {
        var calendar = Istanbul(new DateTimeOffset(Utc(2026, 9, 24, 12)));

        var segment = calendar.LastDays(90).Segments.ShouldHaveSingleItem();

        segment.Offset.ShouldBe(TimeSpan.FromHours(3));
        segment.SingleDay.ShouldBeNull();
    }

    [Fact]
    public void Configurable_zone_splits_the_dst_transition_day_into_its_own_segment()
    {
        // Europe/Berlin: 2026-10-25 03:00 CEST → 02:00 CET (25 saatlik gün).
        var calendar = new LocalDayCalendar("Europe/Berlin", new FixedTimeProvider(new DateTimeOffset(Utc(2026, 10, 27, 12))));

        var window = calendar.LastDays(5); // 23..27 Ekim

        window.StartUtc.ShouldBe(Utc(2026, 10, 22, 22));
        window.EndUtc.ShouldBe(Utc(2026, 10, 27, 23));
        window.Segments.Count.ShouldBe(3);
        window.Segments[0].ShouldBe(new LocalDaySegment(Utc(2026, 10, 22, 22), Utc(2026, 10, 24, 22), TimeSpan.FromHours(2), null));
        window.Segments[1].SingleDay.ShouldBe(new DateOnly(2026, 10, 25));
        window.Segments[1].EndUtc.ShouldBe(Utc(2026, 10, 25, 23));
        window.Segments[2].ShouldBe(new LocalDaySegment(Utc(2026, 10, 25, 23), Utc(2026, 10, 27, 23), TimeSpan.FromHours(1), null));
    }

    [Theory]
    [InlineData("Europe/Istanbul", true)]
    [InlineData("Not/AZone", false)]
    [InlineData("", false)]
    public void Time_zone_setting_is_validated(string id, bool expected)
        => LocalDayCalendar.IsKnownTimeZone(id).ShouldBe(expected);
}
