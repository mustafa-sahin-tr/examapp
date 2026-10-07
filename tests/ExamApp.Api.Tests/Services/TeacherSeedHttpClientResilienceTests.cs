using System.Diagnostics;
using System.Net;
using ExamApp.Api.Services.Teachers.Seed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Kök neden (2026-09-22 Kars+Erzincan koşusu): ServiceDefaults <c>ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler())</c>
/// tüm named client'lara 10 sn attempt timeout + 3 retry ekliyordu; <c>client.Timeout = 30 dk</c> bunu ezmiyordu. 500'lük
/// partial-import 10 sn'yi aşınca istek iptal edilip aynı parti yeniden gönderildi → Keycloak 409, identity yazılmadı,
/// 4.480 yetim Keycloak kullanıcısı. Bu testler <see cref="AuthApiSeedClient.HttpClientName"/> client'ının o varsayılandan
/// muaf olduğunu (pipeline'da ResilienceHandler yok; attempt timeout'u aşan tek istek iptal edilmez ve tekrarlanmaz) ve kontrol
/// olarak başka bir client'ın hâlâ handler'a sahip olduğunu (aynı istek iptal edilip yenilenir) doğrular. Testin kendi
/// standart handler'ı kısa sürelerle (attempt 1 sn) kurulur — kanıt üretimdeki 10 sn ile aynı, süre kısa.
/// </summary>
public class TeacherSeedHttpClientResilienceTests
{
    private sealed class CountingDelayHandler : HttpMessageHandler
    {
        private readonly TimeSpan _delay;
        public int Calls;
        public bool ObservedCancellation;

        public CountingDelayHandler(TimeSpan delay) => _delay = delay;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            try
            {
                await Task.Delay(_delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ObservedCancellation = true;
                throw;
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }

    private static IHostEnvironment DevEnv()
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Development");
        return env;
    }

    /// <summary>Üretimdeki kayıt sırasıyla aynı: önce ServiceDefaults benzeri varsayılan, sonra AddTeacherSeed.</summary>
    private static ServiceProvider BuildProvider(
        HttpMessageHandler primaryForSeedClient,
        HttpMessageHandler? primaryForControl = null,
        TimeProvider? timeProvider = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (timeProvider is not null)
        {
            // Polly/Microsoft.Extensions.Resilience zamanlayıcıları (attempt/total timeout, retry gecikmesi) DI'daki TimeProvider'ı kullanır.
            services.AddSingleton(timeProvider);
        }
        services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler(o =>
        {
            o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(1);
            o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(3);
            o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(2);
            // Varsayılan 2 sn backoff yerine 0: kontrol testinde yeniden deneme, attempt timeout'u tetikleyen Advance'tan
            // sonra ek zaman ilerletmeden gelir (yeniden deneyen tek test elle ilerletilen saatle koşar).
            o.Retry.Delay = TimeSpan.Zero;
        }));
        services.AddTeacherSeed(DevEnv());
        services.AddHttpClient(AuthApiSeedClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => primaryForSeedClient);
        services.AddHttpClient("control").ConfigurePrimaryHttpMessageHandler(() => primaryForControl ?? new CountingDelayHandler(TimeSpan.Zero));
        return services.BuildServiceProvider();
    }

    private static List<Type> HandlerChain(HttpMessageHandler handler)
    {
        var chain = new List<Type>();
        var current = handler;
        while (current is not null)
        {
            chain.Add(current.GetType());
            current = (current as DelegatingHandler)?.InnerHandler;
        }
        return chain;
    }

    [Fact]
    public void Seed_client_pipeline_has_no_resilience_handler_but_other_clients_still_do()
    {
        using var provider = BuildProvider(new CountingDelayHandler(TimeSpan.Zero));
        var factory = provider.GetRequiredService<IHttpMessageHandlerFactory>();

        var seedChain = HandlerChain(factory.CreateHandler(AuthApiSeedClient.HttpClientName));
        var controlChain = HandlerChain(factory.CreateHandler("control"));

        seedChain.ShouldNotContain(t => t == typeof(ResilienceHandler) || t.IsSubclassOf(typeof(ResilienceHandler)));
        controlChain.ShouldContain(t => t == typeof(ResilienceHandler) || t.IsSubclassOf(typeof(ResilienceHandler)));

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(AuthApiSeedClient.HttpClientName);
        client.Timeout.ShouldBe(AuthApiSeedClient.Timeout);
        client.Timeout.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task Seed_client_request_slower_than_attempt_timeout_completes_once_without_cancellation_or_retry()
    {
        // Attempt timeout 1 sn (üretimde 10 sn): 1,5 sn süren istek standart handler'da iptal edilip yenilenirdi.
        var handler = new CountingDelayHandler(TimeSpan.FromSeconds(1.5));
        using var provider = BuildProvider(handler);
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(AuthApiSeedClient.HttpClientName);

        var sw = Stopwatch.StartNew();
        using var response = await client.PostAsync("http://auth-api.test/api/auth/dev/seed-users", new StringContent("{}"));
        sw.Stop();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        handler.Calls.ShouldBe(1);
        handler.ObservedCancellation.ShouldBeFalse();
        sw.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1.4));
    }

    [Fact]
    public async Task Control_client_with_standard_handler_cancels_the_same_slow_request_and_retries()
    {
        // Kontrol: yanıtı hiç gelmeyen istek, resilience'lı client → attempt timeout (1 sn) dolunca o deneme iptal edilir
        // (handler iptali görür) ve aynı istek yeniden gönderilir. Issue #418: duvar saati yerine elle ilerletilen
        // TimeProvider + kapısı açılmayan handler → yük altında zamanlama yarışı yok; iptalin sebebi yalnız attempt timeout.
        var time = new ManualTimeProvider();
        var control = new GatedHandler();
        using var provider = BuildProvider(new CountingDelayHandler(TimeSpan.Zero), control, time);
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("control");
        using var callerCts = new CancellationTokenSource();

        var send = client.PostAsync("http://auth-api.test/x", new StringContent("{}"), callerCts.Token);

        (await control.WaitForAttemptAsync()).ShouldBeTrue("ilk deneme handler'a ulaşmadı");
        var firstAttempt = control.Tokens[0];

        // Attempt timeout'un hemen altı: deneme iptal edilmez, yeniden gönderilmez.
        time.Advance(TimeSpan.FromMilliseconds(999));
        firstAttempt.IsCancellationRequested.ShouldBeFalse();
        control.Calls.ShouldBe(1);

        // Attempt timeout doldu: ilk deneme iptal edilir ve (retry gecikmesi 0) aynı istek ikinci kez gönderilir.
        time.Advance(TimeSpan.FromMilliseconds(1));
        firstAttempt.IsCancellationRequested.ShouldBeTrue();
        (await control.WaitForAttemptAsync()).ShouldBeTrue("attempt timeout sonrası yeniden deneme gelmedi");

        control.CancelledAttempts.ShouldBe(1);
        control.Calls.ShouldBe(2);
        send.IsCompleted.ShouldBeFalse();

        // Temizlik: çağıranın iptali isteği sonlandırır (retry çağıran iptalini yeniden denemez).
        callerCts.Cancel();
        var ex = await Should.ThrowAsync<Exception>(() => send);
        ex.ShouldBeAssignableTo<OperationCanceledException>();
        control.Calls.ShouldBe(2);
    }

    /// <summary>Hiç tamamlanmayan primary handler: her deneme yalnız iptal ile biter; deneme başlangıçları sinyallenir.</summary>
    private sealed class GatedHandler : HttpMessageHandler
    {
        private readonly SemaphoreSlim _attemptStarted = new(0);
        private int _calls;
        private int _cancelled;
        public readonly List<CancellationToken> Tokens = new();

        public int Calls => Volatile.Read(ref _calls);
        public int CancelledAttempts => Volatile.Read(ref _cancelled);

        /// <summary>Gerçek-zaman sınırı yalnız testin asılı kalmaması için emniyet; kanıt bu süreye bağlı değil.</summary>
        public Task<bool> WaitForAttemptAsync() => _attemptStarted.WaitAsync(TimeSpan.FromSeconds(30));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Tokens)
            {
                Tokens.Add(cancellationToken);
            }
            Interlocked.Increment(ref _calls);
            _attemptStarted.Release();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _cancelled);
                throw;
            }
            throw new InvalidOperationException("unreachable");
        }
    }

    /// <summary>
    /// Elle ilerletilen TimeProvider: zamanlayıcılar yalnız <see cref="Advance"/> içinde, vade sırasıyla ve senkron tetiklenir.
    /// </summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly object _gate = new();
        private readonly List<ManualTimer> _timers = new();
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate)
            {
                return _now;
            }
        }

        public override long GetTimestamp() => GetUtcNow().UtcTicks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(TimeSpan by)
        {
            DateTimeOffset target;
            lock (_gate)
            {
                target = _now + by;
            }

            while (true)
            {
                ManualTimer? next;
                lock (_gate)
                {
                    next = _timers
                        .Where(t => t.DueAt <= target)
                        .OrderBy(t => t.DueAt)
                        .FirstOrDefault();
                    if (next is null)
                    {
                        _now = target;
                        return;
                    }

                    _now = next.DueAt!.Value;
                    if (next.Period > TimeSpan.Zero)
                    {
                        next.DueAt = _now + next.Period;
                    }
                    else
                    {
                        _timers.Remove(next);
                        next.DueAt = null;
                    }
                }

                next.Fire();
            }
        }

        private sealed class ManualTimer : ITimer
        {
            private readonly ManualTimeProvider _owner;
            private readonly TimerCallback _callback;
            private readonly object? _state;

            public ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state)
            {
                _owner = owner;
                _callback = callback;
                _state = state;
            }

            public DateTimeOffset? DueAt { get; set; }
            public TimeSpan Period { get; private set; }

            public void Fire() => _callback(_state);

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (_owner._gate)
                {
                    _owner._timers.Remove(this);
                    Period = period == Timeout.InfiniteTimeSpan ? TimeSpan.Zero : period;
                    if (dueTime == Timeout.InfiniteTimeSpan)
                    {
                        DueAt = null;
                        return true;
                    }

                    DueAt = _owner._now + dueTime;
                    _owner._timers.Add(this);
                    return true;
                }
            }

            public void Dispose()
            {
                lock (_owner._gate)
                {
                    _owner._timers.Remove(this);
                    DueAt = null;
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
