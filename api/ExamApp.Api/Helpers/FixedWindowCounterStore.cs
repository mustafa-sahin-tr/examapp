using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ExamApp.Api.Helpers;

/// <summary>Sabit pencere sayacının kararı. <see cref="RetryAfter"/> reddedilince pencerenin kalan süresi.</summary>
public readonly record struct FixedWindowDecision(bool Allowed, TimeSpan? RetryAfter)
{
    public static FixedWindowDecision Allow => new(true, null);
}

/// <summary>
/// issue #262: rate limit sayaç deposu. Üretimde Redis (<see cref="RedisFixedWindowCounterStore"/>) — tüm replica'lar aynı
/// sayacı görür, etkin limit N×limit değil limit'tir. Redis yapılandırılmamışsa (lokal / birim test) süreç içi
/// <see cref="InMemoryFixedWindowCounterStore"/>.
/// </summary>
public interface IFixedWindowCounterStore
{
    /// <summary>
    /// <paramref name="key"/> sayacını <paramref name="permits"/> kadar artırır; pencere ilk artışta başlar.
    /// Yeni değer <paramref name="limit"/>'i aşıyorsa reddedilir.
    /// </summary>
    ValueTask<FixedWindowDecision> TryAcquireAsync(string key, int permits, int limit, TimeSpan window, CancellationToken ct = default);
}

/// <summary>
/// Redis sabit pencere: tek Lua betiğiyle atomik <c>INCRBY</c> + (TTL yoksa) <c>PEXPIRE</c>; tüm replica'lar ortak sayaç.
///
/// FAIL-OPEN (ürün kararı, issue #262): Redis erişilemez / zaman aşımı / beklenmeyen hata → istek GEÇER ve uyarı loglanır
/// (uyarı en fazla <see cref="WarningInterval"/>'da bir; kesinti boyunca log seli olmasın). Gerekçe: rate limit ikincil
/// savunma; asıl kontrol [Authorize(Roles="Admin")] + her erişimin audit'i. Redis kesintisi admin panelini kilitlememeli.
/// </summary>
public sealed class RedisFixedWindowCounterStore : IFixedWindowCounterStore
{
    internal static readonly TimeSpan WarningInterval = TimeSpan.FromSeconds(30);

    // KEYS[1]=sayaç, ARGV[1]=artış, ARGV[2]=pencere (ms). Dönüş: {yeni değer, kalan ms}.
    private const string Script = """
        local current = redis.call('INCRBY', KEYS[1], ARGV[1])
        local ttl = redis.call('PTTL', KEYS[1])
        if ttl < 0 then
          redis.call('PEXPIRE', KEYS[1], ARGV[2])
          ttl = tonumber(ARGV[2])
        end
        return {current, ttl}
        """;

    private readonly IRedisConnectionProvider _connection;
    private readonly TimeSpan _timeout;
    private readonly ILogger<RedisFixedWindowCounterStore> _logger;
    private long _lastWarningTicks = long.MinValue;

    public RedisFixedWindowCounterStore(IRedisConnectionProvider connection, TimeSpan timeout, ILogger<RedisFixedWindowCounterStore> logger)
    {
        _connection = connection;
        _timeout = timeout;
        _logger = logger;
    }

    public async ValueTask<FixedWindowDecision> TryAcquireAsync(string key, int permits, int limit, TimeSpan window, CancellationToken ct = default)
    {
        try
        {
            var multiplexer = await _connection.GetConnectionAsync().WaitAsync(_timeout, ct);
            var windowMs = (long)Math.Max(1, window.TotalMilliseconds);
            var result = await multiplexer.GetDatabase()
                .ScriptEvaluateAsync(Script, [key], [permits, windowMs])
                .WaitAsync(_timeout, ct);

            var values = (RedisResult[])result!;
            var current = (long)values[0];
            var ttlMs = Math.Max(1, (long)values[1]);
            return current <= limit
                ? FixedWindowDecision.Allow
                : new FixedWindowDecision(false, TimeSpan.FromMilliseconds(ttlMs));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            WarnFailOpen(ex, key);
            return FixedWindowDecision.Allow;
        }
    }

    private void WarnFailOpen(Exception ex, string key)
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastWarningTicks);
        if (last != long.MinValue && now - last < (long)WarningInterval.TotalMilliseconds)
            return;
        if (Interlocked.CompareExchange(ref _lastWarningTicks, now, last) != last)
            return;

        _logger.LogWarning(ex,
            "[RateLimit] Redis sayacına erişilemedi; FAIL-OPEN — istek limitsiz geçiriliyor (key={Key}). Uyarı en fazla {Interval}s'de bir.",
            key, WarningInterval.TotalSeconds);
    }
}

/// <summary>
/// Süreç içi sabit pencere (Redis yapılandırılmamışsa: lokal geliştirme, birim/entegrasyon testleri). Çok replica'da
/// replica başınadır — üretimde Redis kullanılmalı.
/// </summary>
public sealed class InMemoryFixedWindowCounterStore : IFixedWindowCounterStore
{
    private const int CleanupThreshold = 1_024;

    private readonly ConcurrentDictionary<string, Window> _windows = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;

    public InMemoryFixedWindowCounterStore(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

    private sealed class Window
    {
        public long Count;
        public DateTimeOffset EndsAt;
    }

    public ValueTask<FixedWindowDecision> TryAcquireAsync(string key, int permits, int limit, TimeSpan window, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();
        if (_windows.Count > CleanupThreshold)
        {
            foreach (var pair in _windows)
            {
                if (pair.Value.EndsAt <= now)
                    _windows.TryRemove(pair);
            }
        }

        var entry = _windows.GetOrAdd(key, _ => new Window { EndsAt = now + window });
        lock (entry)
        {
            if (entry.EndsAt <= now)
            {
                entry.Count = 0;
                entry.EndsAt = now + window;
            }

            entry.Count += permits;
            return ValueTask.FromResult(entry.Count <= limit
                ? FixedWindowDecision.Allow
                : new FixedWindowDecision(false, entry.EndsAt - now));
        }
    }
}
