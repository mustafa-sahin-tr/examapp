using Shouldly;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using AssistantService;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssistantService.Tests;

/// <summary>
/// Issue #502: no token → 401, switched off or no Anthropic key → 503 AssistantDisabled, role not enabled → 403
/// (decision on #496: students first). Paths outside /api/assistant are not gated.
/// </summary>
public sealed class AssistantGateTests
{
    [Theory]
    [InlineData(null, true, "key", HttpStatusCode.Unauthorized, null)]
    [InlineData("Student", false, "key", HttpStatusCode.ServiceUnavailable, "AssistantDisabled")]
    [InlineData("Student", true, "", HttpStatusCode.ServiceUnavailable, "AssistantDisabled")]
    [InlineData("Parent", true, "key", HttpStatusCode.Forbidden, "AssistantRoleNotEnabled")]
    [InlineData("Student", true, "key", HttpStatusCode.OK, null)]
    public async Task Assistant_calls_pass_the_gate_in_order(string? role, bool enabled, string apiKey, HttpStatusCode expected,
        string? code)
    {
        using var host = await StartAsync(enabled, apiKey);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/assistant/usage/me");
        if (role is not null)
            request.Headers.Add(TestAuth.Header, role);

        var response = await host.GetTestClient().SendAsync(request);

        response.StatusCode.ShouldBe(expected);
        if (code is not null)
            JsonNode.Parse(await response.Content.ReadAsStringAsync())!["code"]!.GetValue<string>().ShouldBe(code);
    }

    // #507: the panel asks /status on every page; a closed assistant answers there (enabled: false), not with 403 / 503.
    [Theory]
    [InlineData(null, true, "key", HttpStatusCode.Unauthorized)]
    [InlineData("Student", false, "key", HttpStatusCode.OK)]
    [InlineData("Parent", true, "key", HttpStatusCode.OK)]
    [InlineData("Student", true, "key", HttpStatusCode.OK)]
    public async Task Status_passes_the_gate_for_any_signed_in_user(string? role, bool enabled, string apiKey, HttpStatusCode expected)
    {
        using var host = await StartAsync(enabled, apiKey);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/assistant/status");
        if (role is not null)
            request.Headers.Add(TestAuth.Header, role);

        (await host.GetTestClient().SendAsync(request)).StatusCode.ShouldBe(expected);
    }

    [Fact]
    public async Task Status_exception_is_only_for_get()
    {
        using var host = await StartAsync(enabled: false, apiKey: "key");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/assistant/status");
        request.Headers.Add(TestAuth.Header, "Student");

        (await host.GetTestClient().SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public void Gate_codes_match_the_status_answer()
    {
        AssistantGate.Code(AssistantGateResult.Disabled).ShouldBe("AssistantDisabled");
        AssistantGate.Code(AssistantGateResult.RoleNotEnabled).ShouldBe("AssistantRoleNotEnabled");
        AssistantGate.Code(AssistantGateResult.Allowed).ShouldBeNull();
    }

    [Fact]
    public async Task Other_paths_are_not_gated()
    {
        using var host = await StartAsync(enabled: false, apiKey: "");
        (await host.GetTestClient().GetAsync("/alive-probe")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public void Disabled_wins_over_a_missing_role_and_any_enabled_role_is_enough()
    {
        var parent = TestAuth.Principal("Parent");
        var options = new AssistantOptions { Enabled = false, Roles = ["Student"] };
        AssistantGate.Evaluate(parent, options, apiKeyConfigured: true).ShouldBe(AssistantGateResult.Disabled);

        options.Enabled = true;
        options.Roles = ["Student", "Parent"];
        AssistantGate.Evaluate(parent, options, apiKeyConfigured: true).ShouldBe(AssistantGateResult.Allowed);
    }

    [Fact]
    public void Production_defaults_keep_the_assistant_off()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(ServiceDir(), "appsettings.json"))
            .Build();
        var options = config.GetSection(AssistantOptions.SectionName).Get<AssistantOptions>()!;
        options.Enabled.ShouldBeFalse();
        options.Roles.ShouldBeEmpty();
        // #496 decision 4 (demo / pilot): tighter limits.
        options.DailyBudgetUsdPerUser.ShouldBe(0.10m);
        options.DailyBudgetUsdTotal.ShouldBe(5m);
        options.MessagesPerHour.ShouldBe(30);
        options.ExplanationsPerDay.ShouldBe(20);
        options.RetentionDays.ShouldBe(30);
    }

    private static async Task<IHost> StartAsync(bool enabled, string apiKey)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Assistant:Enabled"] = enabled.ToString(),
                    ["Assistant:Roles:0"] = "Student",
                    ["Anthropic:ApiKey"] = apiKey,
                }))
                .ConfigureServices(s =>
                {
                    s.AddRouting();
                    s.AddOptions<AssistantOptions>().BindConfiguration(AssistantOptions.SectionName);
                    s.AddAuthentication(TestAuth.Scheme).AddScheme<AuthenticationSchemeOptions, TestAuth>(TestAuth.Scheme, _ => { });
                    s.AddAuthorization();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAssistantGate();
                    app.UseAuthorization();
                    app.UseEndpoints(e =>
                    {
                        e.MapGet("/api/assistant/status", () => "ok").RequireAuthorization();
                        e.MapGet("/api/assistant/usage/me", () => "ok").RequireAuthorization();
                        e.MapPost("/api/assistant/status", () => "ok").RequireAuthorization();
                        e.MapGet("/alive-probe", () => "ok");
                    });
                }))
            .Build();
        await host.StartAsync();
        return host;
    }

    private static string ServiceDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ExamApp.slnx")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "Services", "AssistantService");
    }

    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Scheme = "Test";
        public const string Header = "X-Test-Role";

        public static ClaimsPrincipal Principal(string role) =>
            new(new ClaimsIdentity([new Claim("sub", "u1"), new Claim(ClaimTypes.Role, role)], Scheme));

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(Request.Headers.TryGetValue(Header, out var role)
                ? AuthenticateResult.Success(new AuthenticationTicket(Principal(role!), Scheme))
                : AuthenticateResult.NoResult());
    }
}
