using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
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
/// Issue #246: 429 metni policy'ye özgü — admin liste policy'si kendi metnini, kendi OnRejected'ı olmayan başka bir
/// policy jenerik <c>common.tooManyRequests</c> metnini yazar. Partition = sub. Minimal pipeline (auth → limiter).
/// </summary>
public class AdminUserListRateLimitingTests
{
    private const string OtherPolicy = "other-policy";

    private static async Task<IHost> StartHostAsync()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:AdminUserList:PermitLimit"] = "1",
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
                    services.AddAdminUserListRateLimiting();
                    services.Configure<RateLimiterOptions>(o => o.AddFixedWindowLimiter(OtherPolicy, l =>
                    {
                        l.PermitLimit = 1;
                        l.Window = TimeSpan.FromMinutes(10);
                        l.QueueLimit = 0;
                    }));
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
                        e.MapGet("/list", () => Results.Ok("ok")).RequireRateLimiting(AdminUserListRateLimiting.Policy);
                        e.MapGet("/other", () => Results.Ok("ok")).RequireRateLimiting(OtherPolicy);
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
    public async Task Admin_list_policy_writes_its_own_message_with_retry_after_within_window()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        (await GetAsync(client, "/list", "a")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var rejected = await GetAsync(client, "/list", "a");

        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter!.Delta!.Value.TotalSeconds.ShouldBeInRange(1, 600);
        (await rejected.Content.ReadAsStringAsync())
            .ShouldBe("Kısa sürede çok fazla liste isteği yapıldı. Lütfen biraz bekleyip tekrar deneyin.");
    }

    [Fact]
    public async Task Other_policies_get_the_generic_message()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        (await GetAsync(client, "/other", "a")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var rejected = await GetAsync(client, "/other", "a");

        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await rejected.Content.ReadAsStringAsync())
            .ShouldBe("Çok fazla istek yapıldı. Lütfen biraz bekleyip tekrar deneyin.");
    }

    [Fact]
    public async Task Partition_is_the_user_sub()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        (await GetAsync(client, "/list", "a")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(client, "/list", "a")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await GetAsync(client, "/list", "b")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Request_without_sub_is_rejected_with_401_and_does_not_share_a_bucket()
    {
        // issue #279 item 7: eskiden sub'sız istekler ortak "sub:unknown" kovasını paylaşıyordu (global
        // DoS — biri kovayı tüketince diğer kimliksiz istekler de 429 alırdı). StudentSelfReset'teki aynı
        // düzeltme (issue #243 review) burada da uygulandı: koşulsuz 401, kova paylaşılmaz.
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        var first = await client.GetAsync("/list"); // X-Sub header'ı yok
        var second = await client.GetAsync("/list");

        first.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        second.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
