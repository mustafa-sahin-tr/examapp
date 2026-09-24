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
/// metadata'sından, filtre/hedef id istekten); audit hatası 429'u bozmaz; (3) Redis erişilemezse fail-open.
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
    public async Task Redis_store_fails_open_with_a_warning_when_redis_is_unreachable()
    {
        var provider = Substitute.For<IRedisConnectionProvider>();
        provider.GetConnectionAsync().Returns(Task.FromException<IConnectionMultiplexer>(
            new RedisConnectionException(ConnectionFailureType.UnableToConnect, "No connection is available")));
        var logger = new ListLogger<RedisFixedWindowCounterStore>();
        var store = new RedisFixedWindowCounterStore(provider, TimeSpan.FromMilliseconds(200), logger);

        for (var i = 0; i < 5; i++)
            (await store.TryAcquireAsync("k", 1, 1, TimeSpan.FromMinutes(1))).Allowed.ShouldBeTrue();

        // Uyarı loglanır ama kesinti boyunca her istekte değil (throttle).
        logger.Entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains("FAIL-OPEN")).ShouldBe(1);
    }

    [Fact]
    public async Task Redis_store_fails_open_when_redis_is_slow()
    {
        var provider = Substitute.For<IRedisConnectionProvider>();
        provider.GetConnectionAsync().Returns(new TaskCompletionSource<IConnectionMultiplexer>().Task); // hiç tamamlanmaz
        var store = new RedisFixedWindowCounterStore(provider, TimeSpan.FromMilliseconds(50),
            NullLogger<RedisFixedWindowCounterStore>.Instance);

        (await store.TryAcquireAsync("k", 1, 1, TimeSpan.FromMinutes(1))).Allowed.ShouldBeTrue();
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
