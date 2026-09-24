using System.Net;
using System.Reflection;
using System.Security.Claims;
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

namespace ExamApp.Api.Tests.Helpers;

/// <summary>
/// Issue #265: öğretmen aktivite uçları öğretmen (sub) başına rate limit — #246/#262 dağıtık sayaç altyapısı
/// (<see cref="IFixedWindowCounterStore"/>; Redis ayarı yok → süreç içi). İki uç tek kovayı paylaşır; sub'sız istek 401.
/// </summary>
public class TeacherActivityRateLimitingTests
{
    private static async Task<IHost> StartHostAsync()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:TeacherActivity:PermitLimit"] = "2",
                ["RateLimiting:TeacherActivity:WindowSeconds"] = "600",
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
                    services.AddAdminUserListRateLimiting();
                    services.AddTeacherActivityRateLimiting();
                });
                web.Configure(app =>
                {
                    // Test kimliği: X-Sub başlığı → NameIdentifier claim'i.
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
                        e.MapGet("/own", () => Results.Ok("ok")).RequireRateLimiting(TeacherActivityRateLimiting.Policy);
                        e.MapGet("/students", () => Results.Ok("ok")).RequireRateLimiting(TeacherActivityRateLimiting.Policy);
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
    public async Task Both_endpoints_share_one_bucket_and_excess_gets_429_with_retry_after_and_message()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        (await GetAsync(client, "/own", "t1")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(client, "/students", "t1")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var rejected = await GetAsync(client, "/own", "t1");

        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter!.Delta!.Value.TotalSeconds.ShouldBeInRange(1, 600);
        (await rejected.Content.ReadAsStringAsync())
            .ShouldBe("Aktivite özeti kısa sürede çok fazla istendi. Lütfen biraz bekleyip tekrar deneyin.");
    }

    [Fact]
    public async Task Partition_is_the_teacher_sub()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        (await GetAsync(client, "/own", "t1")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(client, "/own", "t1")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(client, "/own", "t1")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await GetAsync(client, "/own", "t2")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Request_without_sub_is_rejected_with_401()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        (await client.GetAsync("/own")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/students")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(nameof(TeacherController.GetOwnActivitySummary))]
    [InlineData(nameof(TeacherController.GetStudentsActivitySummary))]
    public void Teacher_activity_actions_use_the_policy(string action)
        => typeof(TeacherController).GetMethod(action)!
            .GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName
            .ShouldBe(TeacherActivityRateLimiting.Policy);
}
