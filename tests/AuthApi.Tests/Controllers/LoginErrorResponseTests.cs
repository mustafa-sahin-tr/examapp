using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using AuthApi.Tests.Support;
using ExamApp.Api.Controllers;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AuthApi.Tests.Controllers;

/// <summary>
/// Issue #231: <c>POST /api/auth/login</c> yanlış parolada 500 + tam stack trace + istek header'larını
/// döndürüyordu (Development'ta örtük DeveloperExceptionPage). Gerçek <see cref="AuthController"/>,
/// auth-api Program.cs ile aynı hata/yerelleştirme kayıtları (<see cref="AuthErrorHandling"/>,
/// <see cref="AuthLocalization"/>) ve Development ortamında WebApplication'ın örtük eklediği
/// DeveloperExceptionPage en dışta olacak şekilde in-process TestServer üzerinden çalıştırılır.
/// </summary>
public sealed class LoginErrorResponseTests : IAsyncDisposable
{
    private const string ExamplePassword = "wrong-pw-example"; // example credential, test-only

    private readonly TestDb _db = TestDb.Create();
    private readonly IKeycloakService _keycloak = Substitute.For<IKeycloakService>();
    private IHost? _host;

    /// <summary>
    /// Issue #240: <c>GET /api/auth/roles</c> artık <c>[Authorize(Roles = "Admin")]</c>; hata gövdesi testleri
    /// Admin kimliğiyle çağırır (<see cref="AdminHeader"/> varsa Admin, yoksa anonim).
    /// </summary>
    private const string AdminHeader = "X-Test-Admin";

    private sealed class AdminHeaderAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "Test";

        public AdminHeaderAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey(AdminHeader))
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity([new Claim("sub", "kc-admin"), new Claim(ClaimTypes.Role, "Admin")], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    private async Task<HttpClient> StartAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.UseEnvironment(Environments.Development);
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddScoped(_ => _db.NewContext());
                    services.AddSingleton(Options.Create(new KeycloakSettings()));
                    services.AddSingleton(Substitute.For<IHttpClientFactory>());
                    services.AddSingleton(_keycloak);

                    // Program.cs ile aynı kayıtlar; sözlük test çıktı klasöründen okunur.
                    services.AddAuthLocalization(o => o.FileProvider = new PhysicalFileProvider(AppContext.BaseDirectory));
                    services.AddAuthErrorHandling();
                    services.AddAuthentication(AdminHeaderAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, AdminHeaderAuthHandler>(AdminHeaderAuthHandler.SchemeName, _ => { });
                    services.AddAuthorization();

                    services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);
                });
                web.Configure(app =>
                {
                    // WebApplication Development'ta bunu pipeline'ın en başına örtük ekler — burada da
                    // en dışta; UseAuthErrorHandling daha içte olduğu için bu sayfa hiç tetiklenmemeli.
                    app.UseDeveloperExceptionPage();
                    app.UseAuthErrorHandling();
                    app.UseRequestLocalization();
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                });
            })
            .StartAsync();

        return _host.GetTestClient();
    }

    private static HttpRequestMessage LoginRequest(string email, string? acceptLanguage = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { Email = email, Password = ExamplePassword })
        };
        request.Headers.Add("Accept", "application/json, text/plain, */*"); // Angular HttpClient varsayılanı
        if (acceptLanguage is not null) request.Headers.Add("Accept-Language", acceptLanguage);
        return request;
    }

    private static void ShouldNotLeakInternals(string body)
    {
        body.ShouldNotContain("KeycloakException");
        body.ShouldNotContain("StackTrace", Case.Insensitive);
        body.ShouldNotContain("   at ");
        body.ShouldNotContain("ExamApp.Api");
        body.ShouldNotContain("invalid_grant");
        body.ShouldNotContain("Invalid user credentials");
        body.ShouldNotContain(ExamplePassword);
    }

    private static string MessageOf(string body)
        => JsonDocument.Parse(body).RootElement.GetProperty("message").GetString()!;

    [Fact]
    public async Task Wrong_password_returns_401_with_localized_message_and_no_stack_trace()
    {
        _keycloak.LoginAsync("bad@test.local", ExamplePassword, Arg.Any<CancellationToken>())
            .Returns<TokenResponseDto>(_ => throw new KeycloakException(
                "Keycloak login failed: invalid_grant - Invalid user credentials",
                StatusCodes.Status401Unauthorized, KeycloakFailureKind.InvalidGrant));
        var client = await StartAsync();

        var response = await client.SendAsync(LoginRequest("bad@test.local"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        ShouldNotLeakInternals(body);
        MessageOf(body).ShouldBe("E-posta veya şifre hatalı.");
    }

    [Fact]
    public async Task Wrong_password_message_follows_accept_language()
    {
        _keycloak.LoginAsync("bad@test.local", ExamplePassword, Arg.Any<CancellationToken>())
            .Returns<TokenResponseDto>(_ => throw new KeycloakException(
                "Keycloak login failed: invalid_grant - Invalid user credentials",
                StatusCodes.Status401Unauthorized, KeycloakFailureKind.InvalidGrant));
        var client = await StartAsync();

        var response = await client.SendAsync(LoginRequest("bad@test.local", acceptLanguage: "en-GB,en;q=0.9"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        MessageOf(await response.Content.ReadAsStringAsync()).ShouldBe("Invalid email or password.");
    }

    [Fact]
    public async Task Wrong_password_still_writes_a_failed_login_outbox_event()
    {
        _keycloak.LoginAsync("bad@test.local", ExamplePassword, Arg.Any<CancellationToken>())
            .Returns<TokenResponseDto>(_ => throw new KeycloakException(
                "Keycloak login failed: invalid_grant - Invalid user credentials",
                StatusCodes.Status401Unauthorized, KeycloakFailureKind.InvalidGrant));
        var client = await StartAsync();

        await client.SendAsync(LoginRequest("bad@test.local"));

        await using var check = _db.NewContext();
        var row = await check.OutboxMessages.SingleAsync();
        row.Type.ShouldBe(OutboxEventRegistry.NameFor<LoginAttemptedEvent>());
        var evt = JsonSerializer.Deserialize<LoginAttemptedEvent>(row.Content)!;
        evt.Success.ShouldBeFalse();
        evt.KeycloakUserId.ShouldBe("bad@test.local");
        row.Content.ShouldNotContain(ExamplePassword);
    }

    [Fact]
    public async Task Keycloak_unreachable_returns_503_with_generic_message()
    {
        _keycloak.LoginAsync("student@test.local", ExamplePassword, Arg.Any<CancellationToken>())
            .Returns<TokenResponseDto>(_ => throw new KeycloakException(
                "Keycloak login failed: token endpoint unreachable (Connection refused)",
                new HttpRequestException("Connection refused (keycloak:8080)"),
                StatusCodes.Status503ServiceUnavailable, KeycloakFailureKind.ProviderUnavailable));
        var client = await StartAsync();

        var response = await client.SendAsync(LoginRequest("student@test.local"));

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadAsStringAsync();
        ShouldNotLeakInternals(body);
        body.ShouldNotContain("Connection refused");
        body.ShouldNotContain("keycloak:8080");
        MessageOf(body).ShouldBe("Giriş servisine şu anda ulaşılamıyor, lütfen daha sonra tekrar deneyin.");
    }

    [Fact]
    public async Task Unexpected_exception_in_development_returns_500_problem_details_without_stack_trace()
    {
        _keycloak.LoginAsync("student@test.local", ExamplePassword, Arg.Any<CancellationToken>())
            .Returns<TokenResponseDto>(_ => throw new InvalidOperationException("secret internal detail example"));
        var client = await StartAsync();

        var response = await client.SendAsync(LoginRequest("student@test.local"));

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var body = await response.Content.ReadAsStringAsync();
        ShouldNotLeakInternals(body);
        body.ShouldNotContain("InvalidOperationException");
        body.ShouldNotContain("secret internal detail example");
        body.ShouldNotContain("Accept-Language"); // DeveloperExceptionPage header dökümü yok

        var problem = JsonDocument.Parse(body).RootElement;
        problem.GetProperty("status").GetInt32().ShouldBe(500);
        problem.TryGetProperty("detail", out _).ShouldBeFalse();
        problem.TryGetProperty("exception", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Unclassified_keycloak_failure_returns_500_problem_details_without_keycloak_details()
    {
        _keycloak.LoginAsync("student@test.local", ExamplePassword, Arg.Any<CancellationToken>())
            .Returns<TokenResponseDto>(_ => throw new KeycloakException(
                "Keycloak login failed: HTTP 401 invalid_client - Invalid client credentials"));
        var client = await StartAsync();

        var response = await client.SendAsync(LoginRequest("student@test.local"));

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync();
        ShouldNotLeakInternals(body);
        body.ShouldNotContain("invalid_client");
    }

    [Fact]
    public async Task Unexpected_exception_in_development_with_html_accept_has_no_stack_trace_or_headers()
    {
        // Tarayıcı benzeri istek: DeveloperExceptionPage normalde HTML hata sayfası (stack + header dökümü) üretirdi.
        _keycloak.LoginAsync("student@test.local", ExamplePassword, Arg.Any<CancellationToken>())
            .Returns<TokenResponseDto>(_ => throw new InvalidOperationException("secret internal detail example"));
        var client = await StartAsync();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { Email = "student@test.local", Password = ExamplePassword })
        };
        request.Headers.Add("Accept", "text/html,application/xhtml+xml");
        request.Headers.Add("X-Example-Header", "header-value-example");
        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync();
        ShouldNotLeakInternals(body);
        body.ShouldNotContain("InvalidOperationException");
        body.ShouldNotContain("secret internal detail example");
        body.ShouldNotContain("X-Example-Header");
        body.ShouldNotContain("header-value-example");
        body.ShouldNotContain("<html", Case.Insensitive);
    }

    [Fact]
    public async Task Exchange_keycloak_unreachable_returns_503_with_generic_message()
    {
        _keycloak.ExchangeTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<TokenResponseDto>(_ => throw new KeycloakException(
                "Keycloak code exchange failed: token endpoint unreachable (Connection refused)",
                new HttpRequestException("Connection refused (keycloak:8080)"),
                StatusCodes.Status503ServiceUnavailable, KeycloakFailureKind.ProviderUnavailable));
        var client = await StartAsync();

        var response = await client.PostAsJsonAsync("/api/auth/exchange", new { Code = "code-example" });

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadAsStringAsync();
        ShouldNotLeakInternals(body);
        body.ShouldNotContain("keycloak:8080");
        MessageOf(body).ShouldBe("Giriş servisine şu anda ulaşılamıyor, lütfen daha sonra tekrar deneyin.");
    }

    private static HttpRequestMessage RefreshRequest(string? refreshToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh-token");
        if (refreshToken is not null) request.Headers.Add("Cookie", $"refresh_token={refreshToken}");
        return request;
    }

    [Fact]
    public async Task Refresh_token_invalid_grant_returns_401_with_localized_message()
    {
        _keycloak.RefreshTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<TokenResponseDto>(_ => throw new KeycloakException(
                "Keycloak refresh token failed: invalid_grant - Token is not active",
                StatusCodes.Status401Unauthorized, KeycloakFailureKind.InvalidGrant));
        var client = await StartAsync();

        var response = await client.SendAsync(RefreshRequest("example-refresh-token"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        ShouldNotLeakInternals(body);
        body.ShouldNotContain("Token is not active");
        MessageOf(body).ShouldBe("Oturumunuzun süresi doldu, lütfen tekrar giriş yapın.");
    }

    [Fact]
    public async Task Refresh_token_keycloak_unreachable_returns_503_with_generic_message()
    {
        _keycloak.RefreshTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<TokenResponseDto>(_ => throw new KeycloakException(
                "Keycloak refresh token failed: token endpoint timed out", new TaskCanceledException(),
                StatusCodes.Status503ServiceUnavailable, KeycloakFailureKind.ProviderUnavailable));
        var client = await StartAsync();

        var response = await client.SendAsync(RefreshRequest("example-refresh-token"));

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadAsStringAsync();
        ShouldNotLeakInternals(body);
        MessageOf(body).ShouldBe("Giriş servisine şu anda ulaşılamıyor, lütfen daha sonra tekrar deneyin.");
    }

    [Fact]
    public async Task Refresh_token_without_cookie_returns_401_with_message()
    {
        var client = await StartAsync();

        var response = await client.SendAsync(RefreshRequest(null));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        MessageOf(await response.Content.ReadAsStringAsync()).ShouldBe("Oturumunuzun süresi doldu, lütfen tekrar giriş yapın.");
    }

    [Theory]
    [InlineData(KeycloakFailureKind.ProviderUnavailable, HttpStatusCode.ServiceUnavailable)]
    [InlineData(KeycloakFailureKind.Unexpected, HttpStatusCode.InternalServerError)]
    public async Task Roles_failure_body_has_no_keycloak_host_or_message(KeycloakFailureKind kind, HttpStatusCode expected)
    {
        _keycloak.GetRealmRolesAsync()
            .Returns<List<KeycloakRoleDto>>(_ => throw new KeycloakException(
                "Error fetching roles: Failed to fetch realm roles from http://keycloak.internal:8080/admin/realms/exam-realm/roles",
                new HttpRequestException("No such host is known. (keycloak.internal:8080)"),
                kind == KeycloakFailureKind.ProviderUnavailable ? 503 : 500, kind));
        var client = await StartAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/roles");
        request.Headers.Add(AdminHeader, "1");

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(expected);
        var body = await response.Content.ReadAsStringAsync();
        ShouldNotLeakInternals(body);
        body.ShouldNotContain("keycloak.internal");
        body.ShouldNotContain("Error fetching roles");
        body.ShouldNotContain("No such host");
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
