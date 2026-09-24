using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using BadgeService.Controllers;
using BadgeService.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BadgeService.Tests;

/// <summary>
/// Security review follow-up (#148, L4): an end-to-end check that the ASP.NET Core authorization
/// pipeline itself — not just the <c>[Authorize]</c> attribute being present, see
/// <see cref="BadgeDefinitionsAdminControllerAuthorizationTests"/> — actually rejects a non-admin caller
/// and accepts an admin one. Runs against a real <see cref="TestServer"/> hosting only this controller;
/// authentication is a minimal test scheme (no Keycloak/Postgres available in this suite) that mimics the
/// shape of the real JWT pipeline (a `sub`-bearing identity, an optional realm-role-derived
/// <see cref="ClaimTypes.Role"/> claim) closely enough to exercise the same
/// <c>[Authorize(Roles = "Admin")]</c> decision.
/// </summary>
public class BadgeDefinitionsAdminEndpointAuthorizationTests : IAsyncDisposable
{
    private const string SchemeName = "Test";
    private readonly SqliteConnection _connection;
    private readonly IHost _host;
    private readonly TestServer _server;

    public BadgeDefinitionsAdminEndpointAuthorizationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddControllers().AddApplicationPart(typeof(BadgeDefinitionsAdminController).Assembly);
                    services.AddLogging();
                    services.AddDbContext<BadgeDbContext>(o => o.UseSqlite(_connection));
                    services.AddScoped<BadgeDefinitionAdminService>();

                    services.AddAuthentication(SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(SchemeName, _ => { });
                    services.AddAuthorization();
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                });
            });

        _host = hostBuilder.Start();
        _server = _host.GetTestServer();

        using var scope = _host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<BadgeDbContext>().Database.EnsureCreated();
    }

    [Fact]
    public async Task Unauthenticated_request_is_rejected()
    {
        var client = _server.CreateClient();

        var response = await client.GetAsync("/api/admin/badge-definitions");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Authenticated_caller_without_the_Admin_role_gets_403()
    {
        var client = _server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Sub", "user-1");

        var response = await client.GetAsync("/api/admin/badge-definitions");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Authenticated_admin_gets_200()
    {
        var client = _server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Sub", "user-1");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");

        var response = await client.GetAsync("/api/admin/badge-definitions");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
        await _connection.DisposeAsync();
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Sub", out var sub) || string.IsNullOrEmpty(sub))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, sub.ToString()) };
            if (Request.Headers.TryGetValue("X-Test-Role", out var role) && !string.IsNullOrEmpty(role))
            {
                claims.Add(new Claim(ClaimTypes.Role, role.ToString()));
            }

            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
