using ExamApp.Api.Helpers;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Helpers;

/// <summary>issue #419: PeekAsync sayacı değiştirmez ve pencereyi başlatmaz (pencere ilk gerçek artışta başlar).</summary>
public class FixedWindowCounterStorePeekTests
{
    [Fact]
    public async Task Peek_on_missing_key_is_zero_and_does_not_start_a_window()
    {
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero));
        var store = new InMemoryFixedWindowCounterStore(time);
        var window = TimeSpan.FromSeconds(60);

        (await store.PeekAsync("k")).ShouldBe(new FixedWindowPeek(0, null));
        time.Now = time.Now.AddSeconds(50);
        await store.TryAcquireAsync("k", 1, 10, window); // pencere ŞİMDİ başlar

        time.Now = time.Now.AddSeconds(30); // peek'ten 80 sn sonra, ilk artıştan 30 sn sonra
        var peek = await store.PeekAsync("k");
        peek.Count.ShouldBe(1);
        peek.RemainingWindow.ShouldBe(TimeSpan.FromSeconds(30));
        (await store.PeekAsync("k")).Count.ShouldBe(1); // okuma sayacı artırmaz

        time.Now = time.Now.AddSeconds(31);
        (await store.PeekAsync("k")).ShouldBe(new FixedWindowPeek(0, null));
    }
}
