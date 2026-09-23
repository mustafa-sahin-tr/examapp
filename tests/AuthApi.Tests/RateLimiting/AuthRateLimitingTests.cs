using System.Net;
using System.Reflection;
using ExamApp.Api.Controllers;
using ExamApp.Api.Helpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AuthApi.Tests.RateLimiting;

/// <summary>
/// Issue #84 side finding: unauthenticated <c>/login</c> and <c>/exchange</c> must be rate limited
/// per client IP so a brute-force attempt can't fan out through the outbox → RabbitMQ →
/// BadgeService chain unbounded. Exercises <see cref="AuthRateLimiting"/> through a minimal
/// in-process pipeline that mirrors auth-api's Program.cs ordering
/// (UseForwardedHeaders → UseRouting → UseRateLimiter → endpoints).
/// </summary>
public class AuthRateLimitingTests
{
    private const string TestClientIpHeader = "X-Test-Client-Ip";

    private static async Task<IHost> StartHostAsync(int permitLimit, IDictionary<string, string?>? extraConfig = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["RateLimiting:AuthAttempts:PermitLimit"] = permitLimit.ToString(),
            ["RateLimiting:AuthAttempts:WindowSeconds"] = "60",
        };
        foreach (var (key, value) in extraConfig ?? new Dictionary<string, string?>())
            settings[key] = value;

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        return await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthForwardedHeaders(config);
                    services.AddAuthRateLimiting(config);
                });
                web.Configure(app =>
                {
                    // TestServer has no real socket, so RemoteIpAddress is null. Let a test
                    // header stand in for the transport-level peer address (what a gateway or
                    // a direct client would present) before forwarded-headers processing runs.
                    app.Use((ctx, next) =>
                    {
                        if (ctx.Request.Headers.TryGetValue(TestClientIpHeader, out var ip))
                            ctx.Connection.RemoteIpAddress = IPAddress.Parse(ip.ToString());
                        return next(ctx);
                    });
                    app.UseForwardedHeaders();
                    app.UseRouting();
                    app.UseRateLimiter();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapPost("/login", () => Results.Ok("ok"))
                            .RequireRateLimiting(AuthRateLimiting.AuthAttemptsPolicy);
                        endpoints.MapPost("/open", () => Results.Ok("ok"));
                    });
                });
            })
            .StartAsync();
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string? clientIp = null, string? forwardedFor = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (clientIp is not null) request.Headers.Add(TestClientIpHeader, clientIp);
        if (forwardedFor is not null) request.Headers.Add("X-Forwarded-For", forwardedFor);
        return client.SendAsync(request);
    }

    [Fact]
    public async Task Requests_over_the_limit_get_429_with_retry_after_and_plain_text_body()
    {
        using var host = await StartHostAsync(permitLimit: 3);
        using var client = host.GetTestClient();

        for (var i = 0; i < 3; i++)
        {
            var ok = await PostAsync(client, "/login", clientIp: "198.51.100.10");
            ok.StatusCode.ShouldBe(HttpStatusCode.OK, $"attempt {i + 1} should still be allowed");
        }

        var rejected = await PostAsync(client, "/login", clientIp: "198.51.100.10");

        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter.ShouldNotBeNull();
        rejected.Headers.RetryAfter!.Delta.ShouldNotBeNull();
        rejected.Headers.RetryAfter.Delta!.Value.TotalSeconds.ShouldBeGreaterThan(0);
        (await rejected.Content.ReadAsStringAsync()).ShouldBe("Too many authentication attempts. Please try again later.");
    }

    [Fact]
    public async Task Limit_is_tracked_per_client_ip()
    {
        using var host = await StartHostAsync(permitLimit: 2);
        using var client = host.GetTestClient();

        await PostAsync(client, "/login", clientIp: "198.51.100.10");
        await PostAsync(client, "/login", clientIp: "198.51.100.10");
        var exhausted = await PostAsync(client, "/login", clientIp: "198.51.100.10");
        exhausted.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        // A different peer address has its own, untouched bucket.
        var otherClient = await PostAsync(client, "/login", clientIp: "198.51.100.11");
        otherClient.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Client_ip_is_taken_from_x_forwarded_for_behind_the_gateway()
    {
        using var host = await StartHostAsync(permitLimit: 2);
        using var client = host.GetTestClient();

        // Every request arrives from the same transport peer (the gateway); only the
        // gateway-written X-Forwarded-For distinguishes real clients.
        const string gatewayIp = "10.0.0.2";

        await PostAsync(client, "/login", clientIp: gatewayIp, forwardedFor: "203.0.113.5");
        await PostAsync(client, "/login", clientIp: gatewayIp, forwardedFor: "203.0.113.5");
        var exhausted = await PostAsync(client, "/login", clientIp: gatewayIp, forwardedFor: "203.0.113.5");
        exhausted.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        var otherForwardedClient = await PostAsync(client, "/login", clientIp: gatewayIp, forwardedFor: "203.0.113.6");
        otherForwardedClient.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- Issue #100: ForwardedHeaders:KnownNetworks CIDR pinning ----

    private static Dictionary<string, string?> PinnedNetwork(string cidr) => new()
    {
        ["ForwardedHeaders:KnownNetworks:0"] = cidr,
    };

    [Fact]
    public async Task With_known_networks_x_forwarded_for_from_a_trusted_gateway_is_honoured()
    {
        using var host = await StartHostAsync(permitLimit: 1, PinnedNetwork("10.0.0.0/24"));
        using var client = host.GetTestClient();
        const string gatewayIp = "10.0.0.2"; // inside the pinned CIDR

        (await PostAsync(client, "/login", clientIp: gatewayIp, forwardedFor: "203.0.113.5"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PostAsync(client, "/login", clientIp: gatewayIp, forwardedFor: "203.0.113.5"))
            .StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        // Different real client behind the same trusted gateway → separate bucket.
        (await PostAsync(client, "/login", clientIp: gatewayIp, forwardedFor: "203.0.113.6"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task With_known_networks_a_direct_caller_cannot_escape_the_limit_by_spoofing_x_forwarded_for()
    {
        using var host = await StartHostAsync(permitLimit: 1, PinnedNetwork("10.0.0.0/24"));
        using var client = host.GetTestClient();
        const string attackerIp = "198.51.100.66"; // outside the pinned CIDR, bypassing the gateway

        (await PostAsync(client, "/login", clientIp: attackerIp, forwardedFor: "203.0.113.1"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Rotating the spoofed header must not yield a fresh bucket: the limit keys on the real peer.
        (await PostAsync(client, "/login", clientIp: attackerIp, forwardedFor: "203.0.113.2"))
            .StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public void Invalid_known_network_cidr_fails_fast_at_startup()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(PinnedNetwork("not-a-cidr"))
            .Build();

        Should.Throw<InvalidOperationException>(() => new ServiceCollection().AddAuthForwardedHeaders(config))
            .Message.ShouldContain("KnownNetworks");
    }

    private static IConfiguration ConfigOf(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static IHostEnvironment Env(string name)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(name);
        return env;
    }

    [Theory]
    [InlineData("0.0.0.0/0")]
    [InlineData("::/0")]
    public void A_slash_zero_known_network_is_rejected_at_startup(string cidr)
    {
        Should.Throw<InvalidOperationException>(
                () => new ServiceCollection().AddAuthForwardedHeaders(ConfigOf(PinnedNetwork(cidr))))
            .Message.ShouldContain("/0");
    }

    [Fact]
    public void Invalid_known_proxy_fails_eagerly_at_registration_not_on_first_request()
    {
        var config = ConfigOf(new() { ["ForwardedHeaders:KnownProxies:0"] = "not-an-ip" });

        // Throws from AddAuthForwardedHeaders itself — no options resolution / request needed.
        Should.Throw<InvalidOperationException>(() => new ServiceCollection().AddAuthForwardedHeaders(config))
            .Message.ShouldContain("KnownProxies");
    }

    [Fact]
    public void Production_with_no_known_networks_or_proxies_fails_fast()
    {
        Should.Throw<InvalidOperationException>(
                () => new ServiceCollection().AddAuthForwardedHeaders(ConfigOf(new()), Env(Environments.Production)))
            .Message.ShouldContain("Production");
    }

    [Theory]
    [InlineData("ForwardedHeaders:KnownNetworks:0", "10.0.0.0/24")]
    [InlineData("ForwardedHeaders:KnownProxies:0", "10.0.0.2")]
    public void Production_with_either_list_pinned_starts(string key, string value)
    {
        Should.NotThrow(() => new ServiceCollection()
            .AddAuthForwardedHeaders(ConfigOf(new() { [key] = value }), Env(Environments.Production)));
    }

    [Fact]
    public void Development_with_no_pinning_starts_and_is_reported_as_open_trust()
    {
        var config = ConfigOf(new());

        Should.NotThrow(() => new ServiceCollection().AddAuthForwardedHeaders(config, Env(Environments.Development)));
        AuthRateLimiting.IsForwardedHeadersTrustOpen(config).ShouldBeTrue();
        AuthRateLimiting.IsForwardedHeadersTrustOpen(ConfigOf(PinnedNetwork("10.0.0.0/24"))).ShouldBeFalse();
    }

    [Fact]
    public async Task Endpoints_without_the_policy_are_not_limited()
    {
        using var host = await StartHostAsync(permitLimit: 1);
        using var client = host.GetTestClient();

        for (var i = 0; i < 5; i++)
        {
            var response = await PostAsync(client, "/open", clientIp: "198.51.100.10");
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public void Only_login_exchange_and_register_actions_carry_the_auth_attempts_policy()
    {
        var actions = typeof(AuthController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .ToList();

        var limited = actions
            .Where(m => m.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName == AuthRateLimiting.AuthAttemptsPolicy)
            .Select(m => m.Name)
            .OrderBy(n => n)
            .ToList();

        limited.ShouldBe(new[] { nameof(AuthController.EchangeCode), nameof(AuthController.Login), nameof(AuthController.Register) });
    }
}
