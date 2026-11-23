using System.Collections.Concurrent;
using StackExchange.Redis;

namespace AssistantService.Chat;

/// <summary>
/// Issue #504: fixed-window counters (Redis <c>INCRBY</c> + expiry; the exam API's <c>LabelingAiQuota</c> pattern).
/// Redis unreachable or not configured → in-process counters (fail open, warned), so the assistant keeps working.
/// </summary>
public interface ICounterStore
{
    /// <summary>Adds <paramref name="amount"/> and returns the new value and the window's remaining time.</summary>
    Task<(long Value, TimeSpan Remaining)> AddAsync(string key, long amount, TimeSpan window);

    Task<long> GetAsync(string key);
}

public sealed class InMemoryCounterStore(TimeProvider clock) : ICounterStore
{
    private readonly ConcurrentDictionary<string, (long Value, DateTimeOffset ExpiresAt)> _counters = new();

    public Task<(long Value, TimeSpan Remaining)> AddAsync(string key, long amount, TimeSpan window)
    {
        var now = clock.GetUtcNow();
        var entry = _counters.AddOrUpdate(key,
            _ => (amount, now + window),
            (_, old) => old.ExpiresAt <= now ? (amount, now + window) : (old.Value + amount, old.ExpiresAt));
        return Task.FromResult((entry.Value, entry.ExpiresAt - now));
    }

    public Task<long> GetAsync(string key) =>
        Task.FromResult(_counters.TryGetValue(key, out var e) && e.ExpiresAt > clock.GetUtcNow() ? e.Value : 0);
}

public sealed class RedisCounterStore(string configuration, InMemoryCounterStore fallback, ILogger<RedisCounterStore> logger)
    : ICounterStore, IDisposable
{
    private const string Script = """
        local current = redis.call('INCRBY', KEYS[1], ARGV[1])
        local ttl = redis.call('PTTL', KEYS[1])
        if ttl < 0 then
          redis.call('PEXPIRE', KEYS[1], ARGV[2])
          ttl = tonumber(ARGV[2])
        end
        return {current, ttl}
        """;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);
    private readonly Lazy<Task<ConnectionMultiplexer>> _connection = new(() => ConnectionMultiplexer.ConnectAsync(configuration));

    public async Task<(long Value, TimeSpan Remaining)> AddAsync(string key, long amount, TimeSpan window)
    {
        try
        {
            var db = (await _connection.Value.WaitAsync(Timeout)).GetDatabase();
            var result = (RedisResult[])(await db.ScriptEvaluateAsync(Script, [key], [amount, (long)window.TotalMilliseconds]).WaitAsync(Timeout))!;
            return ((long)result[0], TimeSpan.FromMilliseconds(Math.Max(1, (long)result[1])));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Assistant] Redis counter unavailable; counting in memory");
            return await fallback.AddAsync(key, amount, window);
        }
    }

    public async Task<long> GetAsync(string key)
    {
        try
        {
            var db = (await _connection.Value.WaitAsync(Timeout)).GetDatabase();
            var value = await db.StringGetAsync(key).WaitAsync(Timeout);
            return value.HasValue ? (long)value : 0;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Assistant] Redis counter unavailable; reading in memory");
            return await fallback.GetAsync(key);
        }
    }

    public void Dispose()
    {
        if (_connection.IsValueCreated && _connection.Value.IsCompletedSuccessfully)
            _connection.Value.Result.Dispose();
    }
}

public enum LimitExceeded
{
    None,
    MessagesPerHour,
    UserBudget,
    TotalBudget,
}

public sealed record UsageSnapshot(long MessagesThisHour, int MessagesPerHour, decimal SpentTodayUsd, decimal DailyBudgetUsd, bool TotalBudgetReached);

/// <summary>
/// Issue #504 (#496 limits): messages per user per hour, estimated USD per user per day and in total per day. Spend is
/// counted in micro-USD after each model call; a turn starts only while both budgets have room.
/// </summary>
public sealed class UsageLimiter(ICounterStore store, Microsoft.Extensions.Options.IOptionsMonitor<AssistantOptions> options, TimeProvider clock)
{
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    private static readonly TimeSpan Day = TimeSpan.FromHours(25);

    private string Today => clock.GetUtcNow().ToString("yyyyMMdd");
    private string ThisHour => clock.GetUtcNow().ToString("yyyyMMddHH");

    private string MessagesKey(string sub) => $"assistant:msg:{sub}:{ThisHour}";
    private string UserUsdKey(string sub) => $"assistant:usd:{sub}:{Today}";
    private string TotalUsdKey => $"assistant:usd:all:{Today}";

    /// <summary>Checks the budgets, then takes one message from the hourly quota (not taken when refused).</summary>
    public async Task<(LimitExceeded Exceeded, TimeSpan? RetryAfter)> TryStartTurnAsync(string sub)
    {
        var o = options.CurrentValue;
        if (await store.GetAsync(TotalUsdKey) >= ToMicro(o.DailyBudgetUsdTotal))
            return (LimitExceeded.TotalBudget, UntilTomorrow());
        if (await store.GetAsync(UserUsdKey(sub)) >= ToMicro(o.DailyBudgetUsdPerUser))
            return (LimitExceeded.UserBudget, UntilTomorrow());
        var (count, remaining) = await store.AddAsync(MessagesKey(sub), 1, Hour);
        if (count > o.MessagesPerHour)
        {
            await store.AddAsync(MessagesKey(sub), -1, Hour);
            return (LimitExceeded.MessagesPerHour, remaining);
        }
        return (LimitExceeded.None, null);
    }

    public async Task RecordSpendAsync(string sub, decimal usd)
    {
        var micro = ToMicro(usd);
        if (micro <= 0)
            return;
        await store.AddAsync(UserUsdKey(sub), micro, Day);
        await store.AddAsync(TotalUsdKey, micro, Day);
    }

    /// <summary>True while the user's and the total daily budget still have room (checked between model calls).</summary>
    public async Task<bool> HasBudgetAsync(string sub)
    {
        var o = options.CurrentValue;
        return await store.GetAsync(UserUsdKey(sub)) < ToMicro(o.DailyBudgetUsdPerUser)
            && await store.GetAsync(TotalUsdKey) < ToMicro(o.DailyBudgetUsdTotal);
    }

    public async Task<UsageSnapshot> SnapshotAsync(string sub)
    {
        var o = options.CurrentValue;
        return new UsageSnapshot(
            await store.GetAsync(MessagesKey(sub)), o.MessagesPerHour,
            await store.GetAsync(UserUsdKey(sub)) / 1_000_000m, o.DailyBudgetUsdPerUser,
            await store.GetAsync(TotalUsdKey) >= ToMicro(o.DailyBudgetUsdTotal));
    }

    private TimeSpan UntilTomorrow()
    {
        var now = clock.GetUtcNow();
        return now.Date.AddDays(1) - now.UtcDateTime;
    }

    private static long ToMicro(decimal usd) => (long)Math.Ceiling(usd * 1_000_000m);
}
