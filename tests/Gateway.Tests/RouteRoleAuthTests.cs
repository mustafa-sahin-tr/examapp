using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gateway.Tests;

/// <summary>Issue #366: question-detector route'u Bearer ister; okuma uçları Teacher/Admin, send-to-fix Admin/QuestionTrainer, gerisi yalnız Admin.</summary>
public class RouteRoleAuthTests
{
    private static async Task<HttpClient> CreateClientAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, RoleBearerHandler>("Bearer", _ => { });
        var app = builder.Build();
        app.UseRouteRoleAuth();
        app.Run(ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });
        await app.StartAsync();
        return app.GetTestClient();
    }

    private static HttpRequestMessage Post(string path, string? roles)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path);
        if (roles is not null) req.Headers.Add("X-Test-Roles", roles);
        return req;
    }

    [Theory]
    [InlineData("/question-detector-dev/send-to-fix")]
    [InlineData("/question-detector-dev/send-to-fix-for-answers")]
    [InlineData("/question-detector-dev/predict")]
    public async Task Anonymous_Returns401(string path)
    {
        using var client = await CreateClientAsync();
        var res = await client.SendAsync(Post(path, roles: null));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Theory]
    [InlineData("Student")]
    [InlineData("Parent")]
    public async Task NonTeacherRoles_Return403(string roles)
    {
        using var client = await CreateClientAsync();
        var res = await client.SendAsync(Post("/question-detector-dev/send-to-fix", roles));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Theory]
    [InlineData("/question-detector-dev/send-to-fix", "Teacher")]
    [InlineData("/question-detector-dev/send-to-fix-for-answers", "Student,Teacher")]
    [InlineData("/question-detector-dev//send-to-fix", "Teacher")]
    [InlineData("/question-detector-dev/docs", "Teacher")]
    [InlineData("/question-detector-dev/openapi.json", "Teacher")]
    [InlineData("/question-detector-dev/unknown", "Teacher")]
    [InlineData("/question-detector-dev/predict/extra", "Teacher")]
    [InlineData("/question-detector-dev/predictx", "Teacher")]
    [InlineData("/question-detector-dev/labeled-pages", "Teacher")]
    public async Task SendToFix_Teacher_Returns403(string path, string roles)
    {
        using var client = await CreateClientAsync();
        var res = await client.SendAsync(Post(path, roles));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Theory]
    [InlineData("/question-detector-dev/send-to-fix", "Admin")]
    [InlineData("/question-detector-dev/send-to-fix-for-answers", "Admin")]
    [InlineData("/question-detector-dev/predict", "Teacher")]
    [InlineData("/question-detector-dev/read-qr", "Admin")]
    [InlineData("/question-detector-dev/predict", "Student,Teacher")]
    [InlineData("/question-detector-dev/headerlist", "Teacher")]
    [InlineData("/question-detector-dev//predict", "Teacher")]
    public async Task AllowedRoles_PassThrough(string path, string roles)
    {
        using var client = await CreateClientAsync();
        var res = await client.SendAsync(Post(path, roles));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Theory]
    [InlineData("/question-detector-dev/send-to-fix", "QuestionTrainer")]
    [InlineData("/question-detector-dev/send-to-fix", "Teacher,QuestionTrainer")]
    [InlineData("/question-detector-dev/SEND-TO-FIX", "Teacher,QuestionTrainer")]
    [InlineData("/question-detector-dev//send-to-fix", "Teacher,QuestionTrainer")]
    [InlineData("/question-detector-dev/send-to-fix/", "QuestionTrainer")]
    [InlineData("/question-detector-dev/labeled-pages", "QuestionTrainer")]
    [InlineData("/question-detector-dev/labeled-pages", "Admin")]
    public async Task SendToFix_QuestionTrainer_PassesThrough(string path, string roles)
    {
        using var client = await CreateClientAsync();
        var res = await client.SendAsync(Post(path, roles));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Theory]
    [InlineData("/question-detector-dev/docs", "QuestionTrainer")]
    [InlineData("/question-detector-dev/openapi.json", "Teacher,QuestionTrainer")]
    [InlineData("/question-detector-dev/unknown", "QuestionTrainer")]
    [InlineData("/question-detector-dev/send-to-fix-for-answers", "Teacher,QuestionTrainer")]
    [InlineData("/question-detector-dev/send-to-fix/extra", "QuestionTrainer")]
    [InlineData("/question-detector-dev/send-to-fixx", "QuestionTrainer")]
    // QuestionTrainer okuma uçlarına tek başına yetki vermez (yetkili öğretmen Teacher'ı da taşır).
    [InlineData("/question-detector-dev/predict", "QuestionTrainer")]
    [InlineData("/question-detector-dev/headerlist", "QuestionTrainer")]
    public async Task QuestionTrainer_OutsideSendToFix_Returns403(string path, string roles)
    {
        using var client = await CreateClientAsync();
        var res = await client.SendAsync(Post(path, roles));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task SuspiciousEncodedSuffix_AuthenticatedQuestionTrainer_IsDenied400()
    {
        using var client = await CreateClientAsync();
        var res = await client.SendAsync(Post("/question-detector-dev/send-to-fix%3Fx", "QuestionTrainer"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Options_IsNotGated()
    {
        using var client = await CreateClientAsync();
        var res = await client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/question-detector-dev/send-to-fix"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Theory]
    [InlineData("/question-detector-dev/send-to-fix%3F")]
    [InlineData("/question-detector-dev/predict%3Ffoo")]
    [InlineData("/question-detector-dev/predict%23x")]
    [InlineData("/question-detector-dev/predict%2Fx")]
    [InlineData("/question-detector-dev/predict%20")]
    [InlineData("/question-detector-dev/predict%3Bx")]
    public async Task SuspiciousEncodedSuffix_AuthenticatedTeacher_IsDenied400(string path)
    {
        using var client = await CreateClientAsync();
        var res = await client.SendAsync(Post(path, "Teacher"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task SuspiciousEncodedSuffix_Anonymous_Is401()
    {
        using var client = await CreateClientAsync();
        var res = await client.SendAsync(Post("/question-detector-dev/send-to-fix%3F", null));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task OtherRoutes_AreNotAffected()
    {
        using var client = await CreateClientAsync();
        var res = await client.SendAsync(Post("/api/exam/health", roles: null));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Theory]
    [InlineData("ocelot.json")]
    [InlineData("ocelot.Development.json")]
    [InlineData("ocelot.Production.json")]
    public void OcelotConfig_QuestionDetectorRoute_RequiresBearer(string file)
    {
        var dir = AppContext.BaseDirectory;
        string? path = null;
        for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "Gateway", file);
            var alt = Path.Combine(d.FullName, "Services", "Gateway", file);
            if (File.Exists(candidate)) { path = candidate; break; }
            if (File.Exists(alt)) { path = alt; break; }
        }
        Assert.NotNull(path);
        var routes = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path!))!["Routes"]!.AsArray();
        var route = routes.Single(r => r!["UpstreamPathTemplate"]!.GetValue<string>().StartsWith("/question-detector-dev/"));
        Assert.Equal("Bearer", route!["AuthenticationOptions"]!["AuthenticationProviderKey"]!.GetValue<string>());
    }

    private sealed class RoleBearerHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Roles", out var roles))
                return Task.FromResult(AuthenticateResult.NoResult());

            // Keycloak şekli: realm_access iç içe JSON claim.
            var list = string.Join(",", roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(r => $"\"{r}\""));
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "u"), new Claim("realm_access", $"{{\"roles\":[{list}]}}")], "Bearer");
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), "Bearer")));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            Response.Headers.WWWAuthenticate = "Bearer";
            return Task.CompletedTask;
        }
    }

    // ---------- issue #580: soru çözümü onayı ----------

    [Theory]
    [InlineData("/api/exam/questions/12/solution/approve", "Teacher")]
    [InlineData("/api/exam/questions/12/solution/approve", "Student")]
    [InlineData("/api/exam/QUESTIONS/12/Solution/Approve", "Teacher")]
    [InlineData("/api/exam//questions/12/solution/approve/", "Teacher")]
    public async Task SolutionApprove_OtherRoles_Return403(string path, string roles)
    {
        using var client = await CreateClientAsync();
        var res = await client.SendAsync(Post(path, roles));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task SolutionApprove_Anonymous_Returns401()
    {
        using var client = await CreateClientAsync();
        var res = await client.SendAsync(Post("/api/exam/questions/12/solution/approve", roles: null));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Theory]
    [InlineData("/api/exam/questions/12/solution/approve", "Teacher,QuestionTrainer")]
    [InlineData("/api/exam/questions/12/solution/approve", "Admin")]
    // Kural yalnız onay ucunu kapsar: soru bankası ve diğer soru uçları gateway'de rol kuralına girmez (backend denetler).
    [InlineData("/api/exam/questions/12", "Teacher")]
    [InlineData("/api/exam/questions/12/solution", "Teacher")]
    [InlineData("/api/exam/questions/bank", "Teacher")]
    [InlineData("/api/exam/questions/12/solution/approve/extra", "Teacher")]
    public async Task SolutionApprove_AllowedRoles_And_OtherPaths_PassThrough(string path, string roles)
    {
        using var client = await CreateClientAsync();
        var res = await client.SendAsync(Post(path, roles));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public void Pattern_RequiresExactSegmentCount_AndNonEmptyWildcard()
    {
        var pattern = RouteRoleAuthExtensions.DefaultPatterns.Single();
        Assert.True(pattern.Matches("/api/exam/questions/7/solution/approve"));
        Assert.False(pattern.Matches("/api/exam/questions/solution/approve"));
        Assert.False(pattern.Matches("/api/exam/questions/7/8/solution/approve"));
    }
}
