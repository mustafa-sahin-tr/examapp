using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using AuthApi.Tests.Support;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AuthApi.Tests.Controllers;

/// <summary>
/// Issue #219: <c>POST /api/auth/users/lookup</c> her kimlikli kullanıcıya tüm kullanıcıların
/// ad/e-posta/KeycloakId'sini veriyordu. Uç artık yalnızca "Service" policy'sini geçen çağıranlara
/// (exam API'nin client_credentials servis hesabı) açık. Gerçek <see cref="AuthController"/> action'ı,
/// auth-api Program.cs ile aynı "Service" policy tanımı ve bir test kimlik doğrulama şeması ile
/// in-process TestServer üzerinden çalıştırılır (AuthRateLimitingTests ile aynı yaklaşım).
/// </summary>
public sealed class UsersLookupAuthorizationTests : IAsyncDisposable
{
    private const string SubHeader = "X-Test-Sub";
    private const string RolesHeader = "X-Test-Roles";
    private const string AzpHeader = "X-Test-Azp";
    private const string UsernameHeader = "X-Test-Username";

    private readonly TestDb _db = TestDb.Create();
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

            var username = Request.Headers.TryGetValue(UsernameHeader, out var u) ? u.ToString() : "user-" + sub;
            var claims = new List<Claim> { new("sub", sub.ToString()), new("preferred_username", username) };
            if (Request.Headers.TryGetValue(AzpHeader, out var azp))
                claims.Add(new Claim("azp", azp.ToString()));
            if (Request.Headers.TryGetValue(RolesHeader, out var roles))
                claims.AddRange(roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(r => new Claim(ClaimTypes.Role, r)));

            var identity = new ClaimsIdentity(claims, SchemeName);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private async Task<HttpClient> StartAsync(string[]? serviceClients = null)
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
                    services.AddSingleton(Substitute.For<IKeycloakService>());

                    services.AddAuthentication(HeaderAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, HeaderAuthHandler>(HeaderAuthHandler.SchemeName, _ => { });
                    // auth-api/Program.cs ile birebir aynı policy tanımı.
                    services.AddAuthorization(options =>
                        options.AddPolicy("Service", policy =>
                            policy.RequireAssertion(context => ServicePrincipal.IsService(context.User, serviceClients))));

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

    private static HttpRequestMessage LookupRequest(int[] ids, string? sub, string? roles = null, string? azp = null, string? username = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/users/lookup")
        {
            Content = JsonContent.Create(new { UserIds = ids })
        };
        if (sub is not null) request.Headers.Add(SubHeader, sub);
        if (roles is not null) request.Headers.Add(RolesHeader, roles);
        if (azp is not null) request.Headers.Add(AzpHeader, azp);
        if (username is not null) request.Headers.Add(UsernameHeader, username);
        return request;
    }

    private async Task<int> SeedUserAsync(string keycloakId, string fullName, string email)
    {
        await using var ctx = _db.NewContext();
        var user = new User { KeycloakId = keycloakId, FullName = fullName, Email = email, Role = "Student" };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task Anonymous_caller_gets_401()
    {
        var client = await StartAsync();

        var response = await client.SendAsync(LookupRequest([1], sub: null));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("Student")]
    [InlineData("Teacher")]
    [InlineData("Admin")]
    public async Task End_user_token_gets_403_and_no_user_data(string role)
    {
        var otherId = await SeedUserAsync("kc-other", "Gizli Kullanıcı", "gizli@test.local");
        var client = await StartAsync();

        var response = await client.SendAsync(LookupRequest([otherId], sub: "kc-caller", roles: role, azp: "exam-client"));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("gizli@test.local");
    }

    [Fact]
    public async Task Service_role_gets_200_with_users()
    {
        var id = await SeedUserAsync("kc-1", "Ayşe Yılmaz", "ayse@test.local");
        var client = await StartAsync();

        var response = await client.SendAsync(LookupRequest([id, 9999], sub: "svc", roles: ServicePrincipal.ServiceRole, azp: "exam-admin"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("ayse@test.local");
        body.ShouldContain("kc-1");
    }

    [Fact]
    public async Task Configured_service_client_azp_without_role_gets_200()
    {
        var id = await SeedUserAsync("kc-2", "Ali Veli", "ali@test.local");
        var client = await StartAsync(serviceClients: ["exam-admin"]);

        var response = await client.SendAsync(LookupRequest([id], sub: "svc", azp: "exam-admin"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("ali@test.local");
    }

    [Fact]
    public async Task Service_caller_with_empty_ids_gets_400_not_403()
    {
        var client = await StartAsync();

        var response = await client.SendAsync(LookupRequest([], sub: "svc", roles: ServicePrincipal.ServiceRole));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Service_caller_with_more_than_500_ids_gets_400()
    {
        var client = await StartAsync();

        var response = await client.SendAsync(LookupRequest(Enumerable.Range(1, 501).ToArray(), sub: "svc", roles: ServicePrincipal.ServiceRole));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Service_caller_with_exactly_500_ids_is_accepted()
    {
        var client = await StartAsync();

        var response = await client.SendAsync(LookupRequest(Enumerable.Range(1, 500).ToArray(), sub: "svc", roles: ServicePrincipal.ServiceRole));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
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
