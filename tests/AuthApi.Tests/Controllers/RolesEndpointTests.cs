using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using AuthApi.Tests.Support;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AuthApi.Tests.Controllers;

/// <summary>
/// <c>GET /api/auth/roles</c>: anonim 401, Admin olmayan 403, Admin 200 (#240). Gerçek <see cref="AuthController"/>
/// action'ları in-process TestServer üzerinden çalışır (UsersLookupAuthorizationTests ile aynı yaklaşım).
/// </summary>
public sealed class RolesEndpointTests : IAsyncDisposable
{
    private const string SubHeader = "X-Test-Sub";
    private const string RolesHeader = "X-Test-Roles";

    private readonly TestDb _db = TestDb.Create();
    private readonly IKeycloakService _keycloak = Substitute.For<IKeycloakService>();
    private IHost? _host;

    private sealed class HeaderAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "Test";

        public HeaderAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(SubHeader, out var sub))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim> { new("sub", sub.ToString()) };
            if (Request.Headers.TryGetValue(RolesHeader, out var roles))
                claims.AddRange(roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(r => new Claim(ClaimTypes.Role, r)));

            var identity = new ClaimsIdentity(claims, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    private async Task<HttpClient> StartAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddScoped(_ => _db.NewContext());
                    services.AddSingleton(Options.Create(new KeycloakSettings()));
                    services.AddSingleton(Substitute.For<IHttpClientFactory>());
                    services.AddSingleton(_keycloak);

                    services.AddAuthentication(HeaderAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, HeaderAuthHandler>(HeaderAuthHandler.SchemeName, _ => { });
                    services.AddAuthorization();

                    services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                });
            })
            .StartAsync();

        return _host.GetTestClient();
    }

    // ---- GET /api/auth/roles ----

    [Fact]
    public async Task Roles_anonymous_gets_401()
    {
        var client = await StartAsync();

        var response = await client.GetAsync("/api/auth/roles");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await _keycloak.DidNotReceive().GetRealmRolesAsync();
    }

    [Theory]
    [InlineData("Student")]
    [InlineData("Teacher")]
    [InlineData("Parent")]
    public async Task Roles_non_admin_gets_403(string role)
    {
        var client = await StartAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/roles");
        request.Headers.Add(SubHeader, "kc-user");
        request.Headers.Add(RolesHeader, role);

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await _keycloak.DidNotReceive().GetRealmRolesAsync();
    }

    [Fact]
    public async Task Roles_admin_gets_200_with_roles()
    {
        _keycloak.GetRealmRolesAsync().Returns(new List<KeycloakRoleDto> { new() { name = "Student" }, new() { name = "Teacher" } });
        var client = await StartAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/roles");
        request.Headers.Add(SubHeader, "kc-admin");
        request.Headers.Add(RolesHeader, "Admin");

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Teacher");
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        _db.Dispose();
    }
}
