using ExamApp.Foundation.Contracts;

namespace ExamApp.Foundation.Tests.Contracts;

public class EventVersionTests
{
    [Fact]
    public void Unspecified_is_treated_as_utc_and_sub_microsecond_ticks_are_dropped()
    {
        var raw = new DateTime(2026, 9, 23, 10, 0, 0, DateTimeKind.Unspecified).AddTicks(1234567);
        var v = EventVersion.Normalize(raw);
        v.Kind.ShouldBe(DateTimeKind.Utc);
        v.Ticks.ShouldBe(raw.Ticks - 7);
    }

    [Fact]
    public void Local_is_converted_to_utc()
    {
        var utc = new DateTime(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc);
        EventVersion.Normalize(utc.ToLocalTime()).ShouldBe(utc);
        EventVersion.Normalize(utc.ToLocalTime()).Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public void Is_idempotent()
    {
        var once = EventVersion.Normalize(DateTime.UtcNow);
        EventVersion.Normalize(once).ShouldBe(once);
    }
}
