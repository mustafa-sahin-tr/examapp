using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using Microsoft.AspNetCore.Authorization;
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
/// issue #106: doğrudan mesaj uçlarının kullanıcı (sub) başına rate limit kovaları (gönderme / şikayet / okuma) ve
/// controller'ın rol + policy eşlemesi (admin şikayet listesi yalnız Admin; engel yalnız Teacher).
/// </summary>
public class DirectMessageRateLimitingTests
{
    private static async Task<IHost> StartHostAsync()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:DirectMessageSend:PermitLimit"] = "2",
                ["RateLimiting:DirectMessageSend:WindowSeconds"] = "60",
                ["RateLimiting:DirectMessageReport:PermitLimit"] = "1",
                ["RateLimiting:DirectMessageReport:WindowSeconds"] = "3600",
                ["RateLimiting:DirectMessageBlock:PermitLimit"] = "1",
                ["RateLimiting:DirectMessageBlock:WindowSeconds"] = "3600",
                ["RateLimiting:DirectMessageSearch:PermitLimit"] = "1",
                ["RateLimiting:DirectMessageSearch:WindowSeconds"] = "60",
                ["RateLimiting:DirectMessageRead:PermitLimit"] = "3",
                ["RateLimiting:DirectMessageRead:WindowSeconds"] = "60",
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
                    services.AddDirectMessageRateLimiting();
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
                        e.MapPost("/send", () => Results.Ok("ok")).RequireRateLimiting(DirectMessageRateLimiting.SendPolicy);
                        e.MapPost("/report", () => Results.Ok("ok")).RequireRateLimiting(DirectMessageRateLimiting.ReportPolicy);
                        e.MapPost("/block", () => Results.Ok("ok")).RequireRateLimiting(DirectMessageRateLimiting.BlockPolicy);
                        e.MapGet("/teachers", () => Results.Ok("ok")).RequireRateLimiting(DirectMessageRateLimiting.TeacherListPolicy);
                        e.MapGet("/inbox", () => Results.Ok("ok")).RequireRateLimiting(DirectMessageRateLimiting.ReadPolicy);
                    });
                });
            })
            .StartAsync();
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string? sub)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (sub != null)
            request.Headers.Add("X-Sub", sub);
        return client.SendAsync(request);
    }

    [Fact]
    public async Task Send_bucket_is_per_user_and_excess_gets_429_json_with_retry_after()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        (await PostAsync(client, "/send", "s1")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PostAsync(client, "/send", "s1")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var rejected = await PostAsync(client, "/send", "s1");
        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter!.Delta!.Value.TotalSeconds.ShouldBeInRange(1, 60);
        using var body = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("errorCode").GetString().ShouldBe("RateLimited");
        body.RootElement.GetProperty("message").GetString()
            .ShouldBe("Kısa sürede çok fazla mesaj gönderdiniz. Lütfen biraz bekleyip tekrar deneyin.");

        (await PostAsync(client, "/send", "s2")).StatusCode.ShouldBe(HttpStatusCode.OK); // başka kullanıcı etkilenmez
    }

    [Fact]
    public async Task Report_bucket_is_separate_from_send()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        (await PostAsync(client, "/report", "s1")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var rejected = await PostAsync(client, "/report", "s1");
        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await rejected.Content.ReadAsStringAsync()).ShouldContain("şikayet");
        (await PostAsync(client, "/send", "s1")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Block_bucket_is_separate_from_send()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        (await PostAsync(client, "/block", "t1")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var rejected = await PostAsync(client, "/block", "t1");
        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await rejected.Content.ReadAsStringAsync()).ShouldContain("engelleme");
        (await PostAsync(client, "/send", "t1")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string sub)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Sub", sub);
        return client.SendAsync(request);
    }

    [Fact]
    public async Task Teacher_list_uses_search_bucket_only_when_search_is_given_and_read_bucket_otherwise()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        (await GetAsync(client, "/teachers?search=ata", "s1")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var rejected = await GetAsync(client, "/teachers?search=ata", "s1");
        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await rejected.Content.ReadAsStringAsync()).ShouldContain("arama");

        // Aramasız liste okuma kovasını kullanır — okuma kovası diğer okuma uçlarıyla ortak (3).
        (await GetAsync(client, "/teachers", "s1")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(client, "/teachers?search=%20", "s1")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(client, "/inbox", "s1")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(client, "/inbox", "s1")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public void Admin_report_list_uses_the_audited_admin_list_bucket()
    {
        var method = typeof(DirectMessagesController).GetMethod(nameof(DirectMessagesController.GetOpenReports))!;
        method.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName.ShouldBe(AdminUserListRateLimiting.Policy);
        method.GetCustomAttribute<AdminDataAccessAttribute>()!.Resource.ShouldBe(AdminDataAccessResource.DirectMessageReports);
    }

    [Fact]
    public async Task Request_without_sub_is_rejected_with_401()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();
        (await PostAsync(client, "/send", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await PostAsync(client, "/report", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(nameof(DirectMessagesController.SendToTeacher), DirectMessageRateLimiting.SendPolicy)]
    [InlineData(nameof(DirectMessagesController.SendToConversation), DirectMessageRateLimiting.SendPolicy)]
    [InlineData(nameof(DirectMessagesController.Block), DirectMessageRateLimiting.BlockPolicy)]
    [InlineData(nameof(DirectMessagesController.Unblock), DirectMessageRateLimiting.BlockPolicy)]
    [InlineData(nameof(DirectMessagesController.MarkRead), DirectMessageRateLimiting.ReadPolicy)]
    [InlineData(nameof(DirectMessagesController.Report), DirectMessageRateLimiting.ReportPolicy)]
    [InlineData(nameof(DirectMessagesController.GetMessageableTeachers), DirectMessageRateLimiting.TeacherListPolicy)]
    [InlineData(nameof(DirectMessagesController.GetStudentConversations), DirectMessageRateLimiting.ReadPolicy)]
    [InlineData(nameof(DirectMessagesController.GetInbox), DirectMessageRateLimiting.ReadPolicy)]
    [InlineData(nameof(DirectMessagesController.GetMessages), DirectMessageRateLimiting.ReadPolicy)]
    [InlineData(nameof(DirectMessagesController.GetOpenReports), AdminUserListRateLimiting.Policy)]
    public void Every_action_is_rate_limited(string action, string policy)
        => typeof(DirectMessagesController).GetMethod(action)!
            .GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName.ShouldBe(policy);

    [Theory]
    [InlineData(nameof(DirectMessagesController.GetOpenReports), "Admin")]
    [InlineData(nameof(DirectMessagesController.Block), "Teacher")]
    [InlineData(nameof(DirectMessagesController.Unblock), "Teacher")]
    [InlineData(nameof(DirectMessagesController.GetInbox), "Teacher")]
    [InlineData(nameof(DirectMessagesController.SendToTeacher), "Student")]
    [InlineData(nameof(DirectMessagesController.GetMessageableTeachers), "Student")]
    [InlineData(nameof(DirectMessagesController.GetStudentConversations), "Student")]
    [InlineData(nameof(DirectMessagesController.GetMessages), "Student,Teacher")]
    [InlineData(nameof(DirectMessagesController.SendToConversation), "Student,Teacher")]
    [InlineData(nameof(DirectMessagesController.Report), "Student,Teacher")]
    [InlineData(nameof(DirectMessagesController.MarkRead), "Student,Teacher")]
    public void Actions_are_role_gated(string action, string roles)
        => typeof(DirectMessagesController).GetMethod(action)!
            .GetCustomAttributes<AuthorizeAttribute>().Where(a => a.Roles != null).Select(a => a.Roles)
            .ShouldBe(new[] { roles });
}
