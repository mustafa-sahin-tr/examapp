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

    private static async Task<IHost> StartHostAsync(int permitLimit)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:AuthAttempts:PermitLimit"] = permitLimit.ToString(),
                ["RateLimiting:AuthAttempts:WindowSeconds"] = "60",
            })
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
    public void Only_login_and_exchange_actions_carry_the_auth_attempts_policy()
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

        limited.ShouldBe(new[] { nameof(AuthController.EchangeCode), nameof(AuthController.Login) });
    }
}
