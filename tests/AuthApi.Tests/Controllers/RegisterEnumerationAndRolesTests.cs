using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using AuthApi.Tests.Support;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AuthApi.Tests.Controllers;

/// <summary>
/// Issue #240:
/// <list type="bullet">
/// <item><c>POST /api/auth/register</c> e-postanın kayıtlı olup olmadığını ele vermez: yeni e-posta, yerel DB'de
/// kayıtlı e-posta, Keycloak 409, Keycloak 400 ve eşzamanlı unique ihlali aynı status + aynı gövde ile; 200 ve 500
/// yanıtları taban süreden önce dönmez; Keycloak kesintisinde kayıtlı ve yeni e-posta aynı 500'ü alır.</item>
/// <item>E-postadan bağımsız doğrulamalar (rol allowlist, e-posta biçimi) DB/Keycloak'tan önce 400 döner.</item>
/// <item><c>GET /api/auth/roles</c> anonim 401, Admin olmayan 403, Admin 200.</item>
/// </list>
/// Gerçek <see cref="AuthController"/> action'ları in-process TestServer üzerinden çalışır
/// (UsersLookupAuthorizationTests ile aynı yaklaşım).
/// </summary>
public sealed class RegisterEnumerationAndRolesTests : IAsyncDisposable
{
    private const string SubHeader = "X-Test-Sub";
    private const string RolesHeader = "X-Test-Roles";
    private const string ExamplePassword = "pw-example-123"; // example credential, test-only
    private const int Floor = 300;
    private const int FloorWithTimerSlack = Floor - 10; // timer çözünürlüğü payı

    private readonly TestDb _db = TestDb.Create();
    private readonly IKeycloakService _keycloak = Substitute.For<IKeycloakService>();
    private IInterceptor[] _dbInterceptors = [];
    private IHost? _host;

    private static string Text(string key) => FallbackMessageLocalizer.Instance[key].Value;

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

    /// <summary>
    /// Users satırı eklenirken PostgreSQL hatasını taklit eder (SQLite gerçek Postgres SqlState üretmez;
    /// controller'ın <c>IsUniqueViolation</c> predicate'i iç <see cref="Npgsql.PostgresException"/>'a bakar).
    /// </summary>
    private sealed class FailUserInsertInterceptor(string sqlState) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<User>().Any(e => e.State == EntityState.Added))
                throw new DbUpdateException("simulated database error",
                    new Npgsql.PostgresException("simulated", "ERROR", "ERROR", sqlState));
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    /// <summary>İlk SaveChanges'ten önce verilen token'ı iptal eder (istemci yerel yazım sırasında koptu).</summary>
    private sealed class CancelBeforeSaveInterceptor(CancellationTokenSource cts) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            cts.Cancel();
            // Controller CancellationToken.None geçmeli; aksi halde burada iptal görülür.
            cancellationToken.ThrowIfCancellationRequested();
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private async Task<HttpClient> StartAsync(int minimumResponseMilliseconds = 0)
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddScoped(_ => _db.NewContext(_dbInterceptors));
                    services.AddSingleton(Options.Create(new KeycloakSettings()));
                    services.Configure<RegistrationSettings>(o => o.MinimumResponseMilliseconds = minimumResponseMilliseconds);
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

    private static object RegisterBody(string email, string role = "Student") => new
    {
        FirstName = "Ayşe",
        LastName = "Yılmaz",
        Email = email,
        Password = ExamplePassword,
        Role = role
    };

    private async Task SeedUserAsync(string email)
    {
        await using var ctx = _db.NewContext();
        ctx.Users.Add(new User { KeycloakId = "kc-existing", FullName = "Var Olan", Email = email, Role = "Student" });
        await ctx.SaveChangesAsync();
    }

    private void KeycloakCreatesUser(string keycloakId)
        => _keycloak.CreateUserAsync(default!, default!, default!, default!, default!).ReturnsForAnyArgs(keycloakId);

    private void KeycloakCreateFailsFor(string email, Exception ex)
        => _keycloak.CreateUserAsync(email, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns<Task<string>>(_ => throw ex);

    private static KeycloakException KeycloakValidationError() => new(
        "Keycloak user creation failed: {\"errorMessage\":\"invalidPasswordMinLengthMessage\"}", 400, KeycloakFailureKind.Validation);

    private static async Task<(HttpResponseMessage Response, long ElapsedMs)> TimedPostAsync(HttpClient client, object body)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await client.PostAsJsonAsync("/api/auth/register", body);
        stopwatch.Stop();
        return (response, stopwatch.ElapsedMilliseconds);
    }

    private static async Task ShouldBeExactlyAcceptedBodyAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.ValueKind.ShouldBe(JsonValueKind.Object);
        var properties = doc.RootElement.EnumerateObject().ToList();
        properties.Count.ShouldBe(1);
        properties[0].Name.ShouldBe("message");
        properties[0].Value.GetString().ShouldBe(Text("auth.register.accepted"));
    }

    private static async Task<string?> MessageOfAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
    }

    [Fact]
    public void Accepted_message_resource_is_resolved()
    {
        Text("auth.register.accepted").ShouldNotBe("auth.register.accepted");
    }

    // ---- register: enumeration (status + gövde) ----

    [Fact]
    public async Task Register_new_and_already_registered_email_return_identical_status_and_body()
    {
        await SeedUserAsync("taken@test.local");
        KeycloakCreatesUser("kc-new");
        var client = await StartAsync();

        var fresh = await client.PostAsJsonAsync("/api/auth/register", RegisterBody("fresh@test.local"));
        var taken = await client.PostAsJsonAsync("/api/auth/register", RegisterBody("taken@test.local"));

        await ShouldBeExactlyAcceptedBodyAsync(fresh);
        await ShouldBeExactlyAcceptedBodyAsync(taken);
        (await taken.Content.ReadAsStringAsync()).ShouldBe(await fresh.Content.ReadAsStringAsync());
        taken.Content.Headers.ContentType.ShouldBe(fresh.Content.Headers.ContentType);

        // Kayıtlı e-posta Keycloak'ta kullanıcı oluşturmadı ama yine de Keycloak'a gitti (kesinti eşitliği).
        await _keycloak.Received(1).CreateUserAsync("fresh@test.local", Arg.Any<string>(), "fresh@test.local", Arg.Any<string>(), Arg.Any<string>());
        await _keycloak.DidNotReceive().CreateUserAsync("taken@test.local", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
        await _keycloak.Received(1).FindUserIdByUsernameAsync("taken@test.local", Arg.Any<CancellationToken>());
        (await _db.NewContext().Users.CountAsync(u => u.Email == "fresh@test.local")).ShouldBe(1);
    }

    [Fact]
    public async Task Register_keycloak_conflict_returns_the_exact_accepted_body_without_creating_a_local_user()
    {
        KeycloakCreateFailsFor("kc-only@test.local", new KeycloakException(
            "Keycloak user creation failed: {\"errorMessage\":\"User exists with same username\"}", 409, KeycloakFailureKind.Conflict));
        var client = await StartAsync();

        var response = await client.PostAsJsonAsync("/api/auth/register", RegisterBody("kc-only@test.local"));

        await ShouldBeExactlyAcceptedBodyAsync(response);
        (await _db.NewContext().Users.AnyAsync(u => u.Email == "kc-only@test.local")).ShouldBeFalse();
        await _keycloak.DidNotReceiveWithAnyArgs().DeleteUserAsync(default!);
    }

    [Fact]
    public async Task Register_registered_email_and_new_email_rejected_by_keycloak_validation_get_the_same_status_and_body()
    {
        await SeedUserAsync("taken@test.local");
        KeycloakCreateFailsFor("fresh-invalid@test.local", KeycloakValidationError());
        var client = await StartAsync();

        var taken = await client.PostAsJsonAsync("/api/auth/register", RegisterBody("taken@test.local"));
        var rejected = await client.PostAsJsonAsync("/api/auth/register", RegisterBody("fresh-invalid@test.local"));

        rejected.StatusCode.ShouldBe(taken.StatusCode);
        (await rejected.Content.ReadAsStringAsync()).ShouldBe(await taken.Content.ReadAsStringAsync());
        await ShouldBeExactlyAcceptedBodyAsync(rejected);
        (await _db.NewContext().Users.AnyAsync(u => u.Email == "fresh-invalid@test.local")).ShouldBeFalse();
    }

    [Fact]
    public async Task Register_concurrent_unique_violation_returns_accepted_and_rolls_back_the_keycloak_user()
    {
        KeycloakCreatesUser("kc-race");
        _dbInterceptors = [new FailUserInsertInterceptor(Npgsql.PostgresErrorCodes.UniqueViolation)];
        var client = await StartAsync();

        var response = await client.PostAsJsonAsync("/api/auth/register", RegisterBody("race@test.local"));

        await ShouldBeExactlyAcceptedBodyAsync(response);
        await _keycloak.Received(1).DeleteUserAsync("kc-race");
    }

    [Fact]
    public async Task Register_non_unique_db_error_goes_to_the_500_path_and_rolls_back_the_keycloak_user()
    {
        KeycloakCreatesUser("kc-fk");
        _dbInterceptors = [new FailUserInsertInterceptor(Npgsql.PostgresErrorCodes.ForeignKeyViolation)];
        var client = await StartAsync();

        var response = await client.PostAsJsonAsync("/api/auth/register", RegisterBody("fk@test.local"));

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await MessageOfAsync(response)).ShouldBe(Text("auth.register.failed"));
        await _keycloak.Received(1).DeleteUserAsync("kc-fk");
    }

    // ---- register: Keycloak kullanıcısı oluştuktan sonra iptal → yetim kullanıcı kalmaz ----

    private AuthController NewController(AppDbContext context, int floorMs) => new(
        context, Options.Create(new KeycloakSettings()), Substitute.For<IHttpClientFactory>(), _keycloak,
        Substitute.For<ILogger<AuthController>>(),
        registrationOptions: Options.Create(new RegistrationSettings { MinimumResponseMilliseconds = floorMs }));

    [Theory]
    [InlineData(0)]     // iptal yerel yazım sırasında; taban beklemesi yok
    [InlineData(5000)]  // ayrıca taban beklemesi iptal edilmiş token ile başlar
    public async Task Register_cancelled_after_keycloak_user_creation_still_persists_the_local_user(int floorMs)
    {
        using var cts = new CancellationTokenSource();
        KeycloakCreatesUser("kc-cancel");
        await using var ctx = _db.NewContext(new CancelBeforeSaveInterceptor(cts));
        var controller = NewController(ctx, floorMs);

        try
        {
            await controller.Register(new RegisterDto
            {
                FirstName = "Ayşe", LastName = "Yılmaz", Email = "cancel@test.local", Password = ExamplePassword, Role = "Student"
            }, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Taban beklemesi iptal edilen istekte fırlatabilir — kayıt tamamlandıktan SONRA; kabul edilebilir.
        }

        cts.IsCancellationRequested.ShouldBeTrue();
        var user = await _db.NewContext().Users.SingleOrDefaultAsync(u => u.Email == "cancel@test.local");
        user.ShouldNotBeNull();
        user.KeycloakId.ShouldBe("kc-cancel");
        (await _db.NewContext().OutboxMessages.CountAsync()).ShouldBe(1);
        await _keycloak.DidNotReceiveWithAnyArgs().DeleteUserAsync(default!);
    }

    [Fact]
    public async Task Register_keycloak_outage_gives_registered_and_new_email_the_same_500()
    {
        await SeedUserAsync("taken@test.local");
        _keycloak.FindUserIdByUsernameAsync(default!, default).ReturnsForAnyArgs<Task<string?>>(_ => throw new HttpRequestException("Connection refused"));
        KeycloakCreateFailsFor("fresh@test.local", new HttpRequestException("Connection refused"));
        var client = await StartAsync();

        var taken = await client.PostAsJsonAsync("/api/auth/register", RegisterBody("taken@test.local"));
        var fresh = await client.PostAsJsonAsync("/api/auth/register", RegisterBody("fresh@test.local"));

        taken.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        fresh.StatusCode.ShouldBe(taken.StatusCode);
        var body = await fresh.Content.ReadAsStringAsync();
        body.ShouldBe(await taken.Content.ReadAsStringAsync());
        body.ShouldNotContain("Connection refused");
        (await MessageOfAsync(fresh)).ShouldBe(Text("auth.register.failed"));
    }

    [Fact]
    public async Task Register_response_does_not_expose_user_id_or_keycloak_id()
    {
        KeycloakCreatesUser("kc-new-secret-id");
        var client = await StartAsync();

        var response = await client.PostAsJsonAsync("/api/auth/register", RegisterBody("fresh2@test.local"));

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain("kc-new-secret-id");
        await ShouldBeExactlyAcceptedBodyAsync(response);
    }

    // ---- register: yanıt süresi tabanı ----

    [Fact]
    public async Task Register_already_registered_email_waits_for_the_floor()
    {
        await SeedUserAsync("taken@test.local");
        var client = await StartAsync(Floor);

        var (response, elapsed) = await TimedPostAsync(client, RegisterBody("taken@test.local"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        elapsed.ShouldBeGreaterThanOrEqualTo(FloorWithTimerSlack);
    }

    [Fact]
    public async Task Register_new_email_waits_for_the_floor()
    {
        KeycloakCreatesUser("kc-new");
        var client = await StartAsync(Floor);

        var (response, elapsed) = await TimedPostAsync(client, RegisterBody("fresh3@test.local"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        elapsed.ShouldBeGreaterThanOrEqualTo(FloorWithTimerSlack);
    }

    [Fact]
    public async Task Register_keycloak_conflict_waits_for_the_floor()
    {
        KeycloakCreateFailsFor("kc-only@test.local", new KeycloakException("conflict", 409, KeycloakFailureKind.Conflict));
        var client = await StartAsync(Floor);

        var (response, elapsed) = await TimedPostAsync(client, RegisterBody("kc-only@test.local"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        elapsed.ShouldBeGreaterThanOrEqualTo(FloorWithTimerSlack);
    }

    [Fact]
    public async Task Register_keycloak_validation_rejection_waits_for_the_floor()
    {
        KeycloakCreateFailsFor("fresh-invalid@test.local", KeycloakValidationError());
        var client = await StartAsync(Floor);

        var (response, elapsed) = await TimedPostAsync(client, RegisterBody("fresh-invalid@test.local"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        elapsed.ShouldBeGreaterThanOrEqualTo(FloorWithTimerSlack);
    }

    [Fact]
    public async Task Register_failure_500_waits_for_the_floor()
    {
        KeycloakCreateFailsFor("fresh@test.local", new HttpRequestException("Connection refused"));
        var client = await StartAsync(Floor);

        var (response, elapsed) = await TimedPostAsync(client, RegisterBody("fresh@test.local"));

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        elapsed.ShouldBeGreaterThanOrEqualTo(FloorWithTimerSlack);
    }

    // ---- register: e-postadan bağımsız doğrulamalar (DB/Keycloak'tan önce) ----

    [Theory]
    [InlineData("Admin")]
    [InlineData("admin")]
    [InlineData("exam-service")]
    [InlineData("offline_access")]
    [InlineData("Student,Admin")]
    public async Task Register_rejects_non_app_roles_with_invalid_role_message_before_touching_keycloak(string role)
    {
        await SeedUserAsync("taken@test.local");
        var client = await StartAsync();

        // Kayıtlı e-postada da aynı 400 — karar e-postadan bağımsız.
        foreach (var email in new[] { "x@test.local", "taken@test.local" })
        {
            var response = await client.PostAsJsonAsync("/api/auth/register", RegisterBody(email, role));

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await MessageOfAsync(response)).ShouldBe(Text("auth.register.invalidRole"));
        }
        await _keycloak.DidNotReceiveWithAnyArgs().CreateUserAsync(default!, default!, default!, default!, default!);
        await _keycloak.DidNotReceiveWithAnyArgs().FindUserIdByUsernameAsync(default!, default);
        await _keycloak.DidNotReceiveWithAnyArgs().SetRoleAsync(default!, default!);
    }

    [Fact]
    public async Task Register_whitespace_role_is_rejected_by_model_validation_before_touching_keycloak()
    {
        var client = await StartAsync();

        var response = await client.PostAsJsonAsync("/api/auth/register", RegisterBody("x@test.local", " "));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await _keycloak.DidNotReceiveWithAnyArgs().CreateUserAsync(default!, default!, default!, default!, default!);
    }

    [Theory]
    [InlineData("user@localhost")]
    [InlineData("user@test.")]
    [InlineData("\"a b\"@test.local")]
    [InlineData("Ayşe <ayse@test.local>")]
    public async Task Register_rejects_malformed_email_with_invalid_email_message_before_touching_keycloak(string email)
    {
        var client = await StartAsync();

        var response = await client.PostAsJsonAsync("/api/auth/register", RegisterBody(email));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await _keycloak.DidNotReceiveWithAnyArgs().CreateUserAsync(default!, default!, default!, default!, default!);
        await _keycloak.DidNotReceiveWithAnyArgs().FindUserIdByUsernameAsync(default!, default);
    }

    [Fact]
    public async Task Register_malformed_email_that_passes_model_validation_gets_the_invalid_email_message()
    {
        var client = await StartAsync();

        var response = await client.PostAsJsonAsync("/api/auth/register", RegisterBody("user@localhost"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await MessageOfAsync(response)).ShouldBe(Text("auth.register.invalidEmail"));
    }

    [Theory]
    [InlineData("student", "Student")]
    [InlineData("TEACHER", "Teacher")]
    [InlineData("Parent", "Parent")]
    public async Task Register_normalizes_app_role_casing(string requested, string expected)
    {
        KeycloakCreatesUser("kc-role");
        var client = await StartAsync();

        var response = await client.PostAsJsonAsync("/api/auth/register", RegisterBody("role@test.local", requested));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await _keycloak.Received(1).SetRoleAsync("kc-role", expected);
        (await _db.NewContext().Users.SingleAsync(u => u.Email == "role@test.local")).Role.ShouldBe(expected);
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
