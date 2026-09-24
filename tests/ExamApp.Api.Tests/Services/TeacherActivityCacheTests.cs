using ExamApp.Api.Services.Teachers;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #265: iki aktivite ucunun ortak toplaması — UI iki ucu PARALEL çağırır; süren hesap paylaşılır (single-flight),
/// başarılı sonuç kısa süre tutulur, hata/iptal tutulmaz.
/// </summary>
public class TeacherActivityCacheTests
{
    private sealed class Box(int value)
    {
        public int Value { get; } = value;
    }

    private static int Count(Box _) => 1;

    [Fact]
    public async Task Concurrent_callers_share_one_in_flight_computation()
    {
        using var cache = new TeacherActivityCache(TimeSpan.FromSeconds(60));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        async Task<Box> Factory(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            await gate.Task;
            return new Box(42);
        }

        var first = cache.GetOrCreateAsync("k", Factory, Count);
        var second = cache.GetOrCreateAsync("k", Factory, Count);
        gate.SetResult();

        (await first).Value.ShouldBe(42);
        (await second).ShouldBeSameAs(await first);
        calls.ShouldBe(1);

        // Tamamlanmış sonuç TTL boyunca döner.
        (await cache.GetOrCreateAsync("k", Factory, Count)).Value.ShouldBe(42);
        calls.ShouldBe(1);
    }

    [Fact]
    public async Task Different_keys_compute_separately()
    {
        using var cache = new TeacherActivityCache(TimeSpan.FromSeconds(60));
        var calls = 0;
        Task<Box> Factory(CancellationToken _) => Task.FromResult(new Box(Interlocked.Increment(ref calls)));

        (await cache.GetOrCreateAsync("a", Factory, Count)).Value.ShouldBe(1);
        (await cache.GetOrCreateAsync("b", Factory, Count)).Value.ShouldBe(2);
    }

    [Fact]
    public async Task Failures_are_not_cached()
    {
        using var cache = new TeacherActivityCache(TimeSpan.FromSeconds(60));
        var calls = 0;

        Task<Box> Factory(CancellationToken _)
            => Interlocked.Increment(ref calls) == 1
                ? Task.FromException<Box>(new InvalidOperationException("db down"))
                : Task.FromResult(new Box(7));

        await Should.ThrowAsync<InvalidOperationException>(() => cache.GetOrCreateAsync("k", Factory, Count));
        (await cache.GetOrCreateAsync("k", Factory, Count)).Value.ShouldBe(7);
        calls.ShouldBe(2);
    }

    [Fact]
    public async Task Waiter_takes_over_when_the_owner_request_is_cancelled()
    {
        using var cache = new TeacherActivityCache(TimeSpan.FromSeconds(60));
        using var ownerCts = new CancellationTokenSource();
        var ownerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        async Task<Box> Factory(CancellationToken ct)
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                ownerStarted.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }
            return new Box(5);
        }

        var owner = cache.GetOrCreateAsync("k", Factory, Count, ownerCts.Token);
        await ownerStarted.Task;
        var waiter = cache.GetOrCreateAsync("k", Factory, Count);
        ownerCts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => owner);
        (await waiter).Value.ShouldBe(5);
        calls.ShouldBe(2);
    }

    [Fact]
    public async Task Zero_ttl_does_not_keep_completed_results()
    {
        using var cache = new TeacherActivityCache(TimeSpan.Zero);
        var calls = 0;
        Task<Box> Factory(CancellationToken _) => Task.FromResult(new Box(Interlocked.Increment(ref calls)));

        (await cache.GetOrCreateAsync("k", Factory, Count)).Value.ShouldBe(1);
        (await cache.GetOrCreateAsync("k", Factory, Count)).Value.ShouldBe(2);
    }

    [Fact]
    public async Task Waiter_cancelling_its_own_request_stops_waiting_without_affecting_the_owner()
    {
        // review NIT: devralma döngüsü yalnızca SAHİBİN iptalinde döner; bekleyenin kendi iptali döngüden çıkarır.
        using var cache = new TeacherActivityCache(TimeSpan.FromSeconds(60));
        using var waiterCts = new CancellationTokenSource();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        async Task<Box> Factory(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            await gate.Task;
            return new Box(3);
        }

        var owner = cache.GetOrCreateAsync("k", Factory, Count);
        var waiter = cache.GetOrCreateAsync("k", Factory, Count, waiterCts.Token);
        waiterCts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => waiter);
        gate.SetResult();
        (await owner).Value.ShouldBe(3);
        calls.ShouldBe(1);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(99, 1)]
    [InlineData(100, 2)]
    [InlineData(1_000, 11)]
    public void Entry_size_is_proportional_to_result_rows(int rows, long expected)
        => TeacherActivityCache.EntrySize(rows).ShouldBe(expected);
}
