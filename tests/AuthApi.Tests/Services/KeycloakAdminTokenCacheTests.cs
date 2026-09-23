using ExamApp.Api.Services;

namespace AuthApi.Tests.Services;

/// <summary>
/// Issue #152 review: admin token önbelleği singleton — süre dolunca yenilenir, anahtar değişince yenilenir,
/// eşzamanlı ilk istekler tek token isteğinde birleşir, başarısız istek önbelleğe girmez.
/// </summary>
public class KeycloakAdminTokenCacheTests
{
    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task Reuses_until_expiry_minus_10_seconds_then_refreshes()
    {
        var time = new ManualTime();
        var cache = new KeycloakAdminTokenCache(time);
        var calls = 0;
        Task<(string, int)> Factory(CancellationToken _) => Task.FromResult(($"t{++calls}", 60));

        (await cache.GetOrCreateAsync("k", Factory)).ShouldBe("t1");
        time.Now = time.Now.AddSeconds(49);
        (await cache.GetOrCreateAsync("k", Factory)).ShouldBe("t1");
        time.Now = time.Now.AddSeconds(1); // 50 sn = 60 - 10
        (await cache.GetOrCreateAsync("k", Factory)).ShouldBe("t2");
    }

    [Fact]
    public async Task Different_key_fetches_a_new_token()
    {
        var cache = new KeycloakAdminTokenCache();
        var calls = 0;
        Task<(string, int)> Factory(CancellationToken _) => Task.FromResult(($"t{++calls}", 300));

        (await cache.GetOrCreateAsync("a", Factory)).ShouldBe("t1");
        (await cache.GetOrCreateAsync("b", Factory)).ShouldBe("t2");
    }

    [Fact]
    public async Task Concurrent_first_callers_share_a_single_token_request()
    {
        var cache = new KeycloakAdminTokenCache();
        var calls = 0;
        async Task<(string, int)> Factory(CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(50, ct);
            return ("t", 300);
        }

        var tokens = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => cache.GetOrCreateAsync("k", Factory)));

        tokens.ShouldAllBe(t => t == "t");
        calls.ShouldBe(1);
    }

    [Fact]
    public async Task Failed_fetch_is_not_cached()
    {
        var cache = new KeycloakAdminTokenCache();
        var fail = true;
        Task<(string, int)> Factory(CancellationToken _) => fail
            ? Task.FromException<(string, int)>(new HttpRequestException("down"))
            : Task.FromResult(("ok", 300));

        await Should.ThrowAsync<HttpRequestException>(() => cache.GetOrCreateAsync("k", Factory));
        fail = false;
        (await cache.GetOrCreateAsync("k", Factory)).ShouldBe("ok");
    }
}
