using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using ExamApp.Api.Controllers;
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
/// issue #419: veli kod denemesi (redeem) veli (sub) başına dakikada 5 — PO kararı; öğrenci kod üretimi ayrı kova.
/// Controller rol + policy eşlemesi.
/// </summary>
public class ParentLinkRateLimitingTests
{
    private static async Task<IHost> StartHostAsync(Dictionary<string, string?>? overrides = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(overrides ?? new Dictionary<string, string?>())
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
                    services.AddParentLinkRateLimiting();
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
                        e.MapPost("/redeem", () => Results.Ok("ok")).RequireRateLimiting(ParentLinkRateLimiting.RedeemPolicy);
                        e.MapPost("/invite", () => Results.Ok("ok")).RequireRateLimiting(ParentLinkRateLimiting.InvitePolicy);
                        e.MapPost("/summary", () => Results.Ok("ok")).RequireRateLimiting(ParentLinkRateLimiting.ChildSummaryPolicy);
                        e.MapPost("/activity", () => Results.Ok("ok")).RequireRateLimiting(ParentLinkRateLimiting.ChildActivityPolicy);
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
    public async Task Redeem_defaults_to_five_per_minute_per_parent_and_sixth_gets_429_json()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        for (var i = 0; i < 5; i++)
            (await PostAsync(client, "/redeem", "p1")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var rejected = await PostAsync(client, "/redeem", "p1");
        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter!.Delta!.Value.TotalSeconds.ShouldBeInRange(1, 60);
        using var body = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("errorCode").GetString().ShouldBe("RateLimited");
        body.RootElement.GetProperty("message").GetString()
            .ShouldBe("Çok fazla kod denemesi yapıldı. Lütfen daha sonra tekrar deneyin.");

        (await PostAsync(client, "/redeem", "p2")).StatusCode.ShouldBe(HttpStatusCode.OK); // başka veli etkilenmez
    }

    [Fact]
    public async Task Invite_bucket_is_separate_from_redeem()
    {
        using var host = await StartHostAsync(new Dictionary<string, string?>
        {
            ["RateLimiting:ParentLinkInvite:PermitLimit"] = "1",
            ["RateLimiting:ParentLinkInvite:WindowSeconds"] = "3600",
        });
        using var client = host.GetTestClient();

        (await PostAsync(client, "/invite", "s1")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PostAsync(client, "/invite", "s1")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await PostAsync(client, "/redeem", "s1")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Child_summary_defaults_to_thirty_per_minute_per_parent_with_retry_after()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        for (var i = 0; i < 30; i++)
            (await PostAsync(client, "/summary", "p1")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var rejected = await PostAsync(client, "/summary", "p1");
        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter!.Delta!.Value.TotalSeconds.ShouldBeInRange(1, 60);
        using var body = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("errorCode").GetString().ShouldBe("RateLimited");
        body.RootElement.GetProperty("message").GetString()
            .ShouldBe("Çok fazla istek yapıldı. Lütfen biraz sonra tekrar deneyin.");

        (await PostAsync(client, "/summary", "p2")).StatusCode.ShouldBe(HttpStatusCode.OK); // başka veli etkilenmez
        (await PostAsync(client, "/redeem", "p1")).StatusCode.ShouldBe(HttpStatusCode.OK);  // ayrı kova
    }

    [Fact]
    public void Child_summary_action_carries_its_rate_limit_policy_and_parent_role()
    {
        var method = typeof(ParentDashboardController).GetMethod(nameof(ParentDashboardController.GetChildSummary))!;
        method.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName.ShouldBe(ParentLinkRateLimiting.ChildSummaryPolicy);
        typeof(ParentDashboardController).GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Roles).ShouldContain("Parent");
    }

    [Fact]
    public async Task Child_activity_defaults_to_sixty_per_minute_per_parent_separate_from_summary()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        for (var i = 0; i < 60; i++)
            (await PostAsync(client, "/activity", "p1")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var rejected = await PostAsync(client, "/activity", "p1");
        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter!.Delta!.Value.TotalSeconds.ShouldBeInRange(1, 60);
        using var body = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("errorCode").GetString().ShouldBe("RateLimited");

        body.RootElement.GetProperty("message").GetString()
            .ShouldBe("Çocuğunuzun bilgileri için kısa sürede çok fazla istek yapıldı. Lütfen biraz sonra tekrar deneyin."); // kendi 429 metni
        (await PostAsync(client, "/activity", "p2")).StatusCode.ShouldBe(HttpStatusCode.OK); // başka veli etkilenmez
        (await PostAsync(client, "/summary", "p1")).StatusCode.ShouldBe(HttpStatusCode.OK);  // özet kovası ayrı
        (await PostAsync(client, "/activity", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(nameof(ParentDashboardController.GetChildAssignments))]
    [InlineData(nameof(ParentDashboardController.GetChildTestResult))]
    public void Child_activity_actions_carry_their_rate_limit_policy_and_are_get_only(string action)
    {
        var method = typeof(ParentDashboardController).GetMethod(action)!;
        method.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName.ShouldBe(ParentLinkRateLimiting.ChildActivityPolicy);
        method.GetCustomAttribute<Microsoft.AspNetCore.Mvc.HttpGetAttribute>().ShouldNotBeNull();
    }

    [Fact]
    public void Parent_dashboard_controller_has_no_write_actions()
    {
        var actions = typeof(ParentDashboardController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        actions.ShouldNotBeEmpty();
        actions.ShouldAllBe(m => m.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>()
            .All(a => a.HttpMethods.All(h => h == "GET")));
    }

    [Fact]
    public async Task Request_without_sub_is_rejected_with_401()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();
        (await PostAsync(client, "/redeem", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(nameof(ParentLinksController.Redeem), ParentLinkRateLimiting.RedeemPolicy)]
    [InlineData(nameof(ParentLinksController.CreateSecondParentCode), ParentLinkRateLimiting.InvitePolicy)] // #436
    [InlineData(nameof(ParentLinksController.Approve), ParentLinkRateLimiting.ManagePolicy)] // #436 security MINOR-5
    [InlineData(nameof(ParentLinksController.Reject), ParentLinkRateLimiting.ManagePolicy)]
    [InlineData(nameof(ParentLinksController.Revoke), ParentLinkRateLimiting.ManagePolicy)]
    public void Write_actions_carry_their_rate_limit_policy(string action, string policy)
        => typeof(ParentLinksController).GetMethod(action)!
            .GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName.ShouldBe(policy);

    [Theory]
    [InlineData(nameof(ParentLinksController.CreateSecondParentCode), "Parent")] // #436: yalnız (birincil) veli kod üretir
    [InlineData(nameof(ParentLinksController.GetMyParents), "Student")]
    [InlineData(nameof(ParentLinksController.Redeem), "Parent")]
    [InlineData(nameof(ParentLinksController.GetMyChildren), "Parent")]
    [InlineData(nameof(ParentLinksController.Revoke), "Parent,Admin")] // #436: öğrenci koparamaz
    [InlineData(nameof(ParentLinksController.Approve), "Student,Parent")] // öğrenci: yalnız geçiş dönemi (servis)
    [InlineData(nameof(ParentLinksController.Reject), "Student,Parent")]
    public void Actions_are_role_gated(string action, string roles)
    {
        var attributes = typeof(ParentLinksController).GetMethod(action)!.GetCustomAttributes<AuthorizeAttribute>().ToList();
        attributes.Select(a => a.Roles).ShouldContain(roles);
        // Sınıf seviyesinde rol kısıtı yok (ASP.NET sınıf + metot rollerini AND'lerdi).
        typeof(ParentLinksController).GetCustomAttributes<AuthorizeAttribute>().ShouldAllBe(a => string.IsNullOrEmpty(a.Roles));
    }
}
