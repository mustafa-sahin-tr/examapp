using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using BadgeService.Controllers;
using BadgeService.Models;
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

    /// <summary>
    /// Code review follow-up (#148, BLOCKER): pins down the actual bug — <c>CreatedAtAction(nameof(GetAsync), ...)</c>
    /// silently failed to resolve a route (MVC's SuppressAsyncSuffixInActionNames convention registers the
    /// action as "Get", not "GetAsync") and threw inside <c>CreatedAtAction</c>, turning every successful
    /// create into a 500 even though the row was already saved. A unit test instantiating the controller
    /// directly can't catch this — link generation only runs inside a real routing pipeline, hence a
    /// TestServer-backed HTTP test.
    /// </summary>
    [Fact]
    public async Task Create_returns_201_with_a_resolvable_Location_header()
    {
        var client = AdminClient();

        var response = await client.PostAsJsonAsync("/api/admin/badge-definitions", new CreateBadgeDefinitionRequest
        {
            Code = "http-created",
            Name = "HTTP ile Oluşturulan",
            Category = "Test",
            RuleType = "AnswerCount",
            RuleConfigJson = "{\"target\":1}",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location.ShouldNotBeNull();

        var created = await response.Content.ReadFromJsonAsync<BadgeDefinitionAdminDto>();
        created.ShouldNotBeNull();
        created!.Code.ShouldBe("http-created");

        // The whole point of the bug: the Location header must actually resolve, not just be present.
        var getResponse = await client.GetAsync(response.Headers.Location);
        getResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var fetched = await getResponse.Content.ReadFromJsonAsync<BadgeDefinitionAdminDto>();
        fetched!.Id.ShouldBe(created.Id);
    }

    [Fact]
    public async Task Update_returns_200_with_the_edited_fields()
    {
        var client = AdminClient();
        var created = await CreateBadgeAsync(client, "http-updated");

        var response = await client.PutAsJsonAsync($"/api/admin/badge-definitions/{created.Id}", new UpdateBadgeDefinitionRequest
        {
            Name = "Güncellendi",
            Category = "Test",
            RuleType = "AnswerCount",
            RuleConfigJson = "{\"target\":2}",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<BadgeDefinitionAdminDto>();
        updated!.Name.ShouldBe("Güncellendi");
        updated.RuleConfigJson.ShouldBe("{\"target\":2}");
    }

    [Fact]
    public async Task Deactivate_then_activate_round_trip_returns_200()
    {
        var client = AdminClient();
        var created = await CreateBadgeAsync(client, "http-toggle");

        var deactivateResponse = await client.PostAsync($"/api/admin/badge-definitions/{created.Id}/deactivate", content: null);
        deactivateResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var deactivated = await deactivateResponse.Content.ReadFromJsonAsync<BadgeDefinitionAdminDto>();
        deactivated!.IsActive.ShouldBeFalse();

        var activateResponse = await client.PostAsync($"/api/admin/badge-definitions/{created.Id}/activate", content: null);
        activateResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var activated = await activateResponse.Content.ReadFromJsonAsync<BadgeDefinitionAdminDto>();
        activated!.IsActive.ShouldBeTrue();
    }

    private HttpClient AdminClient()
    {
        var client = _server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Sub", "user-1");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");
        return client;
    }

    private static async Task<BadgeDefinitionAdminDto> CreateBadgeAsync(HttpClient client, string code)
    {
        var response = await client.PostAsJsonAsync("/api/admin/badge-definitions", new CreateBadgeDefinitionRequest
        {
            Code = code,
            Name = "Seed",
            Category = "Test",
            RuleType = "AnswerCount",
            RuleConfigJson = "{\"target\":1}",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<BadgeDefinitionAdminDto>())!;
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
