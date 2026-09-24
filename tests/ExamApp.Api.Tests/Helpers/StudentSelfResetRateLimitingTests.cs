using System.Net;
using System.Reflection;
using System.Security.Claims;
using ExamApp.Api.Controllers;
using ExamApp.Api.Helpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ExamApp.Api.Tests.Helpers;

/// <summary>
/// issue #243: öğrenci self-reset rate limit — varsayılan (config yok) sub başına saatte 1; 2. istek 429 +
/// Retry-After + yerelleştirilmiş metin. Minimal pipeline (test kimliği → limiter), AdminUserListRateLimitingTests deseni.
/// </summary>
public class StudentSelfResetRateLimitingTests
{
    private static async Task<IHost> StartHostAsync()
    {
        return await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    // Bilerek boş config: appsettings'teki değerlerle aynı olması gereken kod varsayılanları test edilir.
                    services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
                    services.AddRouting();
                    services.AddLogging();
                    services.AddAdminUserListRateLimiting();
                    services.AddStudentSelfResetRateLimiting();
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
                        e.MapPost("/reset", () => Results.Accepted()).RequireRateLimiting(StudentSelfResetRateLimiting.Policy));
                });
            })
            .StartAsync();
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string sub)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/reset");
        request.Headers.Add("X-Sub", sub);
        return client.SendAsync(request);
    }

    [Fact]
    public void Defaults_are_one_per_hour()
    {
        var options = new StudentSelfResetRateLimitOptions();
        options.PermitLimit.ShouldBe(1);
        options.WindowSeconds.ShouldBe(3600);
        StudentSelfResetRateLimitOptions.SectionName.ShouldBe("RateLimiting:StudentSelfReset");
    }

    [Fact]
    public async Task Second_request_within_the_hour_gets_429_with_retry_after_and_localized_message()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        (await PostAsync(client, "student-a")).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var rejected = await PostAsync(client, "student-a");

        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter!.Delta!.Value.TotalSeconds.ShouldBeInRange(1, 3600);
        (await rejected.Content.ReadAsStringAsync())
            .ShouldBe("Verilerinizi kısa süre önce sıfırladınız. Lütfen daha sonra tekrar deneyin.");
    }

    [Fact]
    public async Task Partition_is_the_user_sub()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        (await PostAsync(client, "student-a")).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await PostAsync(client, "student-a")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await PostAsync(client, "student-b")).StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Request_without_sub_is_rejected_with_401_and_does_not_share_a_bucket()
    {
        // review: eskiden sub'sız istekler ortak "sub:unknown" kovasını paylaşıyordu (global DoS).
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        var first = await client.PostAsync("/reset", content: null);
        var second = await client.PostAsync("/reset", content: null);

        first.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        second.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        first.Headers.RetryAfter.ShouldBeNull();
        // Kimlikli öğrencinin kotası etkilenmez.
        (await PostAsync(client, "student-a")).StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public void Reset_endpoint_uses_the_student_self_reset_policy()
    {
        var method = typeof(StudentController).GetMethod(nameof(StudentController.ResetMyStudentData))!;

        method.GetCustomAttribute<HttpPostAttribute>()!.Template.ShouldBe("me/reset");
        method.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName.ShouldBe(StudentSelfResetRateLimiting.Policy);
    }
}
