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

        var first = cache.GetOrCreateAsync("k", Factory);
        var second = cache.GetOrCreateAsync("k", Factory);
        gate.SetResult();

        (await first).Value.ShouldBe(42);
        (await second).ShouldBeSameAs(await first);
        calls.ShouldBe(1);

        // Tamamlanmış sonuç TTL boyunca döner.
        (await cache.GetOrCreateAsync("k", Factory)).Value.ShouldBe(42);
        calls.ShouldBe(1);
    }

    [Fact]
    public async Task Different_keys_compute_separately()
    {
        using var cache = new TeacherActivityCache(TimeSpan.FromSeconds(60));
        var calls = 0;
        Task<Box> Factory(CancellationToken _) => Task.FromResult(new Box(Interlocked.Increment(ref calls)));

        (await cache.GetOrCreateAsync("a", Factory)).Value.ShouldBe(1);
        (await cache.GetOrCreateAsync("b", Factory)).Value.ShouldBe(2);
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

        await Should.ThrowAsync<InvalidOperationException>(() => cache.GetOrCreateAsync("k", Factory));
        (await cache.GetOrCreateAsync("k", Factory)).Value.ShouldBe(7);
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

        var owner = cache.GetOrCreateAsync("k", Factory, ownerCts.Token);
        await ownerStarted.Task;
        var waiter = cache.GetOrCreateAsync("k", Factory);
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

        (await cache.GetOrCreateAsync("k", Factory)).Value.ShouldBe(1);
        (await cache.GetOrCreateAsync("k", Factory)).Value.ShouldBe(2);
    }
}
