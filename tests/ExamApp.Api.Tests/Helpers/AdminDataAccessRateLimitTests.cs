using System.Net;
using System.Security.Claims;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.AdminUsers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace ExamApp.Api.Tests.Helpers;

/// <summary>
/// Issue #262: admin veri uçlarının rate limit'i — (1) sayaç dağıtık depoda: aynı depoyu paylaşan iki "replica" toplamda
/// limit kadar geçirir (N×limit değil); (2) 429 reddi audit tablosuna <c>RateLimited</c> olarak yazılır (kaynak endpoint
/// metadata'sından, filtre/hedef id istekten), pencere başına yalnızca İLK red; audit hatası 429'u bozmaz; (3) Redis
/// erişilemezse fail-open ama limitsiz değil: süreç içi sayaca düşer; (4) bağlantı sağlayıcısı hatadan sonra yeniden dener.
/// </summary>
public class AdminDataAccessRateLimitTests
{
    private static async Task<IHost> StartHostAsync(IFixedWindowCounterStore store, IAdminDataAccessAuditService audit, int permitLimit = 1)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:AdminUserList:PermitLimit"] = permitLimit.ToString(),
                ["RateLimiting:AdminUserList:WindowSeconds"] = "600",
            })
            .Build();

        return await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddSingleton<IConfiguration>(config);
                    services.AddRouting();
                    services.AddLogging();
                    services.AddSingleton(store); // TryAdd: AddAdminUserListRateLimiting bunu ezmez
                    services.AddScoped(_ => audit);
                    services.AddAdminUserListRateLimiting();
                });
                web.Configure(app =>
                {
                    app.Use((ctx, next) =>
                    {
                        if (ctx.Request.Headers.TryGetValue("X-Sub", out var sub))
                            ctx.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, sub.ToString())], "Test"));
                        return next(ctx);
                    });
                    app.UseRouting();
                    app.UseRateLimiter();
                    app.UseEndpoints(e =>
                    {
                        e.MapGet("/students", () => Results.Ok("ok"))
                            .RequireRateLimiting(AdminUserListRateLimiting.Policy)
                            .WithMetadata(new AdminDataAccessAttribute(AdminDataAccessResource.StudentList));
                        e.MapGet("/applications", () => Results.Ok("ok"))
                            .RequireRateLimiting(AdminUserListRateLimiting.Policy)
                            .WithMetadata(new AdminDataAccessAttribute(AdminDataAccessResource.TeacherApplicationList));
                        e.MapGet("/applications/{id:int}", (int id) => Results.Ok(id))
                            .RequireRateLimiting(AdminUserListRateLimiting.Policy)
                            .WithMetadata(new AdminDataAccessAttribute(AdminDataAccessResource.TeacherApplicationDetail));
                        e.MapGet("/unmarked", () => Results.Ok("ok"))
                            .RequireRateLimiting(AdminUserListRateLimiting.Policy);
                    });
                });
            })
            .StartAsync();
    }

    private static Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string sub)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Sub", sub);
        return client.SendAsync(request);
    }

    [Fact]
    public async Task Rejected_list_request_is_persisted_as_rate_limited_with_filters()
    {
        var audit = Substitute.For<IAdminDataAccessAuditService>();
        using var host = await StartHostAsync(new InMemoryFixedWindowCounterStore(), audit);
        using var client = host.GetTestClient();

        (await GetAsync(client, "/students", "kc-a")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var rejected = await GetAsync(client, "/students?schoolId=7&unassigned=false", "kc-a");

        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter!.Delta!.Value.TotalSeconds.ShouldBeInRange(1, 600);
        await audit.Received(1).RecordRateLimitedAsync(
            new AdminRateLimitedAccessRecord("kc-a", AdminDataAccessResource.StudentList, 7, false, null),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("/applications?status=all", TeacherApplicationStatusFilter.All)]
    [InlineData("/applications", TeacherApplicationStatusFilter.Pending)]
    [InlineData("/applications?status=bogus", null)]
    public async Task Issue187_rejected_application_list_request_is_persisted_with_the_status_filter(
        string path, TeacherApplicationStatusFilter? expected)
    {
        var audit = Substitute.For<IAdminDataAccessAuditService>();
        using var host = await StartHostAsync(new InMemoryFixedWindowCounterStore(), audit);
        using var client = host.GetTestClient();

        (await GetAsync(client, "/applications", "kc-a")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(client, path, "kc-a")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        await audit.Received(1).RecordRateLimitedAsync(
            new AdminRateLimitedAccessRecord("kc-a", AdminDataAccessResource.TeacherApplicationList, null, false, null, expected),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Status_query_is_not_recorded_for_other_resources()
    {
        var audit = Substitute.For<IAdminDataAccessAuditService>();
        using var host = await StartHostAsync(new InMemoryFixedWindowCounterStore(), audit);
        using var client = host.GetTestClient();

        (await GetAsync(client, "/students", "kc-a")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(client, "/students?status=all", "kc-a")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        await audit.Received(1).RecordRateLimitedAsync(
            new AdminRateLimitedAccessRecord("kc-a", AdminDataAccessResource.StudentList, null, false, null, null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rejected_detail_request_is_persisted_with_the_target_id()
    {
        var audit = Substitute.For<IAdminDataAccessAuditService>();
        using var host = await StartHostAsync(new InMemoryFixedWindowCounterStore(), audit);
        using var client = host.GetTestClient();

        (await GetAsync(client, "/applications/5", "kc-a")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(client, "/applications/42", "kc-a")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        await audit.Received(1).RecordRateLimitedAsync(
            new AdminRateLimitedAccessRecord("kc-a", AdminDataAccessResource.TeacherApplicationDetail, null, false, 42),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Audit_failure_does_not_change_the_429_response()
    {
        var audit = Substitute.For<IAdminDataAccessAuditService>();
        audit.RecordRateLimitedAsync(Arg.Any<AdminRateLimitedAccessRecord>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("db down")));
        using var host = await StartHostAsync(new InMemoryFixedWindowCounterStore(), audit);
        using var client = host.GetTestClient();

        (await GetAsync(client, "/students", "kc-a")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var rejected = await GetAsync(client, "/students", "kc-a");

        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await rejected.Content.ReadAsStringAsync())
            .ShouldBe("Kısa sürede çok fazla liste isteği yapıldı. Lütfen biraz bekleyip tekrar deneyin.");
    }

    [Fact]
    public async Task Endpoint_without_resource_metadata_still_gets_429_but_is_not_audited()
    {
        var audit = Substitute.For<IAdminDataAccessAuditService>();
        using var host = await StartHostAsync(new InMemoryFixedWindowCounterStore(), audit);
        using var client = host.GetTestClient();

        (await GetAsync(client, "/unmarked", "kc-a")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(client, "/unmarked", "kc-a")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        await audit.DidNotReceiveWithAnyArgs().RecordRateLimitedAsync(default!, default);
    }

    [Fact]
    public async Task Two_replicas_sharing_the_store_enforce_one_combined_limit()
    {
        // Paylaşılan depo = Redis'in rolü. Önceden (instance başına bellek) iki replica toplam 2×limit geçirirdi.
        var shared = new InMemoryFixedWindowCounterStore();
        var audit = Substitute.For<IAdminDataAccessAuditService>();
        using var replicaA = await StartHostAsync(shared, audit, permitLimit: 2);
        using var replicaB = await StartHostAsync(shared, audit, permitLimit: 2);
        using var clientA = replicaA.GetTestClient();
        using var clientB = replicaB.GetTestClient();

        (await GetAsync(clientA, "/students", "kc-a")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(clientB, "/students", "kc-a")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(clientA, "/students", "kc-a")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await GetAsync(clientB, "/students", "kc-a")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        // Başka admin kendi kovasıyla
        (await GetAsync(clientB, "/students", "kc-b")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- Depolar ----

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task In_memory_store_counts_per_key_and_resets_after_the_window()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.Zero));
        var store = new InMemoryFixedWindowCounterStore(clock);
        var window = TimeSpan.FromSeconds(60);

        (await store.TryAcquireAsync("k", 1, 2, window)).Allowed.ShouldBeTrue();
        (await store.TryAcquireAsync("k", 1, 2, window)).Allowed.ShouldBeTrue();
        clock.Now = clock.Now.AddSeconds(15);
        var third = await store.TryAcquireAsync("k", 1, 2, window);
        third.Allowed.ShouldBeFalse();
        third.RetryAfter.ShouldBe(TimeSpan.FromSeconds(45));
        (await store.TryAcquireAsync("other", 1, 2, window)).Allowed.ShouldBeTrue();

        clock.Now = clock.Now.AddSeconds(45);
        (await store.TryAcquireAsync("k", 1, 2, window)).Allowed.ShouldBeTrue();
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    [Fact]
    public async Task Redis_outage_falls_back_to_the_in_memory_limit_with_a_throttled_warning()
    {
        // #262 güvenlik review'u: fail-open limiti KALDIRMAZ — süreç içi (replica başına) sayaca düşer.
        var provider = Substitute.For<IRedisConnectionProvider>();
        provider.GetConnectionAsync().Returns(Task.FromException<IConnectionMultiplexer>(
            new RedisConnectionException(ConnectionFailureType.UnableToConnect, "No connection is available")));
        var logger = new ListLogger<RedisFixedWindowCounterStore>();
        var store = new RedisFixedWindowCounterStore(provider, TimeSpan.FromMilliseconds(200), logger);

        var decisions = new List<FixedWindowDecision>();
        for (var i = 0; i < 5; i++)
            decisions.Add(await store.TryAcquireAsync("k", 1, 2, TimeSpan.FromMinutes(1)));

        decisions.Select(d => d.Allowed).ShouldBe([true, true, false, false, false]);
        decisions[2].RetryAfter.ShouldNotBeNull();
        (await store.TryAcquireAsync("other", 1, 2, TimeSpan.FromMinutes(1))).Allowed.ShouldBeTrue(); // anahtar başına
        // Uyarı loglanır ama kesinti boyunca her istekte değil (throttle).
        logger.Entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains("FAIL-OPEN")).ShouldBe(1);
    }

    [Fact]
    public async Task Slow_redis_falls_back_to_the_in_memory_limit()
    {
        var provider = Substitute.For<IRedisConnectionProvider>();
        provider.GetConnectionAsync().Returns(new TaskCompletionSource<IConnectionMultiplexer>().Task); // hiç tamamlanmaz
        var store = new RedisFixedWindowCounterStore(provider, TimeSpan.FromMilliseconds(50),
            NullLogger<RedisFixedWindowCounterStore>.Instance);

        (await store.TryAcquireAsync("k", 1, 1, TimeSpan.FromMinutes(1))).Allowed.ShouldBeTrue();
        (await store.TryAcquireAsync("k", 1, 1, TimeSpan.FromMinutes(1))).Allowed.ShouldBeFalse();
    }

    [Fact]
    public async Task Redis_outage_through_the_pipeline_still_rejects_and_audits_once()
    {
        var provider = Substitute.For<IRedisConnectionProvider>();
        provider.GetConnectionAsync().Returns(Task.FromException<IConnectionMultiplexer>(
            new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down")));
        var store = new RedisFixedWindowCounterStore(provider, TimeSpan.FromMilliseconds(200),
            NullLogger<RedisFixedWindowCounterStore>.Instance);
        var audit = Substitute.For<IAdminDataAccessAuditService>();
        using var host = await StartHostAsync(store, audit);
        using var client = host.GetTestClient();

        (await GetAsync(client, "/students", "kc-a")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(client, "/students", "kc-a")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        await audit.ReceivedWithAnyArgs(1).RecordRateLimitedAsync(default!, default);
    }

    // ---- İlk red audit'i (#262 review) ----

    [Fact]
    public async Task Only_the_first_rejection_per_window_is_audited()
    {
        var audit = Substitute.For<IAdminDataAccessAuditService>();
        using var host = await StartHostAsync(new InMemoryFixedWindowCounterStore(), audit, permitLimit: 2);
        using var client = host.GetTestClient();

        for (var i = 0; i < 2; i++)
            (await GetAsync(client, "/students", "kc-a")).StatusCode.ShouldBe(HttpStatusCode.OK);
        for (var i = 0; i < 5; i++)
            (await GetAsync(client, "/students", "kc-a")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await GetAsync(client, "/students", "kc-b")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(client, "/students", "kc-b")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(client, "/students", "kc-b")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        // Her partition (sub) için pencere başına TEK satır.
        await audit.ReceivedWithAnyArgs(2).RecordRateLimitedAsync(default!, default);
        await audit.Received(1).RecordRateLimitedAsync(Arg.Is<AdminRateLimitedAccessRecord>(r => r.ActorKeycloakId == "kc-a"), Arg.Any<CancellationToken>());
        await audit.Received(1).RecordRateLimitedAsync(Arg.Is<AdminRateLimitedAccessRecord>(r => r.ActorKeycloakId == "kc-b"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task First_rejection_is_audited_again_in_the_next_window()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.Zero));
        var audit = Substitute.For<IAdminDataAccessAuditService>();
        using var host = await StartHostAsync(new InMemoryFixedWindowCounterStore(clock), audit);
        using var client = host.GetTestClient();

        (await GetAsync(client, "/students", "kc-a")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(client, "/students", "kc-a")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await GetAsync(client, "/students", "kc-a")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        clock.Now = clock.Now.AddSeconds(601); // pencere (600 sn) doldu
        (await GetAsync(client, "/students", "kc-a")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(client, "/students", "kc-a")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        await audit.ReceivedWithAnyArgs(2).RecordRateLimitedAsync(default!, default);
    }

    [Theory]
    [InlineData(3, 2, true)]   // limit+1 → ilk red
    [InlineData(4, 2, false)]  // sonraki red
    [InlineData(2, 2, false)]  // izinli (Allowed=true)
    public void IsFirstRejection_is_count_equals_limit_plus_permits(long count, int limit, bool expected)
        => new FixedWindowDecision(count <= limit, null, count).IsFirstRejection(1, limit).ShouldBe(expected);

    [Fact]
    public async Task In_memory_store_reports_the_counter_value()
    {
        var store = new InMemoryFixedWindowCounterStore();
        (await store.TryAcquireAsync("k", 1, 1, TimeSpan.FromMinutes(1))).Count.ShouldBe(1);
        var second = await store.TryAcquireAsync("k", 1, 1, TimeSpan.FromMinutes(1));
        second.Count.ShouldBe(2);
        second.IsFirstRejection(1, 1).ShouldBeTrue();
        (await store.TryAcquireAsync("k", 1, 1, TimeSpan.FromMinutes(1))).IsFirstRejection(1, 1).ShouldBeFalse();
    }

    // ---- RedisConnectionProvider (#262 review NIT) ----

    [Fact]
    public async Task Connection_provider_retries_after_a_failed_connect()
    {
        var calls = 0;
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        using var provider = new RedisConnectionProvider(() =>
        {
            calls++;
            return calls == 1
                ? Task.FromException<IConnectionMultiplexer>(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"))
                : Task.FromResult(multiplexer);
        });

        await Should.ThrowAsync<RedisConnectionException>(() => provider.GetConnectionAsync());
        (await provider.GetConnectionAsync()).ShouldBeSameAs(multiplexer); // hatalı görev önbellekte kalmadı
        (await provider.GetConnectionAsync()).ShouldBeSameAs(multiplexer); // başarılı bağlantı paylaşılır
        calls.ShouldBe(2);
    }

    [Fact]
    public async Task Connection_provider_retries_after_a_synchronous_factory_exception()
    {
        var calls = 0;
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        using var provider = new RedisConnectionProvider(() =>
        {
            if (++calls == 1)
                throw new InvalidOperationException("bad config");
            return Task.FromResult(multiplexer);
        });

        await Should.ThrowAsync<InvalidOperationException>(() => provider.GetConnectionAsync());
        (await provider.GetConnectionAsync()).ShouldBeSameAs(multiplexer);
    }

    [Fact]
    public async Task Connection_provider_shares_a_pending_connect()
    {
        var calls = 0;
        var tcs = new TaskCompletionSource<IConnectionMultiplexer>();
        using var provider = new RedisConnectionProvider(() => { calls++; return tcs.Task; });

        var first = provider.GetConnectionAsync();
        var second = provider.GetConnectionAsync();
        tcs.SetResult(Substitute.For<IConnectionMultiplexer>());

        (await first).ShouldBeSameAs(await second);
        calls.ShouldBe(1);
    }

    [Fact]
    public async Task Redis_store_propagates_caller_cancellation()
    {
        var provider = Substitute.For<IRedisConnectionProvider>();
        provider.GetConnectionAsync().Returns(new TaskCompletionSource<IConnectionMultiplexer>().Task);
        var store = new RedisFixedWindowCounterStore(provider, TimeSpan.FromSeconds(30),
            NullLogger<RedisFixedWindowCounterStore>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await store.TryAcquireAsync("k", 1, 1, TimeSpan.FromMinutes(1), cts.Token));
    }
}
