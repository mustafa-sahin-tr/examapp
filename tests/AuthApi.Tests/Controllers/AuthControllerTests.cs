using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using AuthApi.Tests.Support;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AuthApi.Tests.Controllers;

/// <summary>
/// Issue #84: every login attempt (success or failure) through <see cref="AuthController.Login"/>
/// and <see cref="AuthController.EchangeCode"/> must be persisted via the outbox, without
/// leaking credentials, and without swallowing the underlying Keycloak failure.
/// </summary>
public class AuthControllerTests : IDisposable
{
    private const string TestPassword = "pw1234"; // example credential, test-only
    private const string TestSecretPassword = "s3cr3t9"; // example credential, test-only

    private readonly TestDb _db = TestDb.Create();

    private AuthController NewController(IKeycloakService keycloakService, AppDbContext context)
    {
        var controller = new AuthController(
            context, Options.Create(new KeycloakSettings()), Substitute.For<IHttpClientFactory>(), keycloakService,
            Substitute.For<ILogger<AuthController>>());
        // EchangeCode appends a refresh_token cookie to Response — needs a real HttpContext,
        // which a bare `new AuthController(...)` doesn't have outside ASP.NET's request pipeline.
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    private static string BuildJwt(string sub, string email, params string[] roles)
    {
        var claims = new List<Claim>
        {
            new("sub", sub),
            new("email", email),
        };
        if (roles.Length > 0)
        {
            var realmAccessJson = JsonSerializer.Serialize(new { roles });
            claims.Add(new Claim("realm_access", realmAccessJson));
        }

        var token = new JwtSecurityToken(claims: claims);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static TokenResponseDto SuccessfulTokenResponse(string sub, string email, params string[] roles)
    {
        var exampleAccessToken = BuildJwt(sub, email, roles); // example unsigned jwt, test-only
        return new TokenResponseDto
        {
            AccessToken = exampleAccessToken,
            RefreshToken = "refresh-token-example",
            TokenType = "Bearer",
            ExpiresIn = 300,
            RefreshExpiresIn = 3600,
            Scope = "openid",
        };
    }

    // example credential wrapper, test-only
    private static LoginDto ExampleLogin(string exampleEmail, string examplePassword) =>
        new() { Email = exampleEmail, Password = examplePassword }; // example

    // ---- Login: success ----

    [Fact]
    public async Task Login_valid_credentials_writes_a_successful_login_outbox_row()
    {
        await using var context = _db.NewContext();
        var keycloak = Substitute.For<IKeycloakService>();
        keycloak.LoginAsync("student@test.local", TestPassword)
            .Returns(SuccessfulTokenResponse("kc-sub-1", "student@test.local", "Student"));

        var controller = NewController(keycloak, context);

        var result = await controller.Login(ExampleLogin("student@test.local", TestPassword));

        result.ShouldBeOfType<OkObjectResult>();

        await using var check = _db.NewContext();
        var row = await check.OutboxMessages.SingleAsync();
        row.Type.ShouldBe(OutboxEventRegistry.NameFor<LoginAttemptedEvent>());

        var evt = JsonSerializer.Deserialize<LoginAttemptedEvent>(row.Content)!;
        evt.KeycloakUserId.ShouldBe("kc-sub-1");
        evt.Role.ShouldBe("Student");
        evt.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Login_valid_credentials_outbox_content_does_not_contain_the_password()
    {
        await using var context = _db.NewContext();
        var keycloak = Substitute.For<IKeycloakService>();
        keycloak.LoginAsync("student@test.local", TestSecretPassword)
            .Returns(SuccessfulTokenResponse("kc-sub-1", "student@test.local", "Student"));

        var controller = NewController(keycloak, context);

        await controller.Login(ExampleLogin("student@test.local", TestSecretPassword));

        await using var check = _db.NewContext();
        var row = await check.OutboxMessages.SingleAsync();
        row.Content.ShouldNotContain(TestSecretPassword);
    }

    // ---- Login: failure ----

    [Fact]
    public async Task Login_invalid_credentials_writes_a_failed_login_outbox_row_and_rethrows()
    {
        await using var context = _db.NewContext();
        var keycloak = Substitute.For<IKeycloakService>();
        keycloak.LoginAsync("bad@test.local", TestPassword)
            .Returns<TokenResponseDto>(_ => throw new KeycloakException("invalid_grant", 401));

        var controller = NewController(keycloak, context);

        await Should.ThrowAsync<KeycloakException>(() =>
            controller.Login(ExampleLogin("bad@test.local", TestPassword)));

        await using var check = _db.NewContext();
        var row = await check.OutboxMessages.SingleAsync();
        row.Type.ShouldBe(OutboxEventRegistry.NameFor<LoginAttemptedEvent>());

        var evt = JsonSerializer.Deserialize<LoginAttemptedEvent>(row.Content)!;
        evt.Success.ShouldBeFalse();
        evt.KeycloakUserId.ShouldBe("bad@test.local"); // no sub available yet — email used as correlation key
        row.Content.ShouldNotContain(TestPassword);
    }

    // ---- EchangeCode: success ----

    [Fact]
    public async Task EchangeCode_valid_code_writes_a_successful_login_outbox_row()
    {
        await using var context = _db.NewContext();
        var keycloak = Substitute.For<IKeycloakService>();
        keycloak.ExchangeTokenAsync("auth-code-123")
            .Returns(SuccessfulTokenResponse("kc-sub-2", "teacher@test.local", "Teacher"));

        var controller = NewController(keycloak, context);

        var result = await controller.EchangeCode(new CodeDto { Code = "auth-code-123" });

        result.ShouldBeOfType<OkObjectResult>();

        await using var check = _db.NewContext();
        var row = await check.OutboxMessages.SingleAsync();
        var evt = JsonSerializer.Deserialize<LoginAttemptedEvent>(row.Content)!;
        evt.KeycloakUserId.ShouldBe("kc-sub-2");
        evt.Role.ShouldBe("Teacher");
        evt.Success.ShouldBeTrue();
    }

    // ---- EchangeCode: failure ----

    [Fact]
    public async Task EchangeCode_invalid_code_writes_a_failed_login_outbox_row_and_rethrows()
    {
        await using var context = _db.NewContext();
        var keycloak = Substitute.For<IKeycloakService>();
        keycloak.ExchangeTokenAsync("bad-code-example")
            .Returns<TokenResponseDto>(_ => throw new KeycloakException("invalid_grant", 401));

        var controller = NewController(keycloak, context);

        await Should.ThrowAsync<KeycloakException>(() =>
            controller.EchangeCode(new CodeDto { Code = "bad-code-example" }));

        await using var check = _db.NewContext();
        var row = await check.OutboxMessages.SingleAsync();
        var evt = JsonSerializer.Deserialize<LoginAttemptedEvent>(row.Content)!;
        evt.Success.ShouldBeFalse();
    }

    // ---- Outbox write failure must never break the login response (code review finding) ----

    [Fact]
    public async Task Login_still_succeeds_when_outbox_write_fails()
    {
        await using var context = _db.NewContext();
        // Force SaveChangesAsync to throw by closing the underlying connection out from under
        // the context — simulates a transient outbox-write DB failure.
        await context.Database.CloseConnectionAsync();

        var keycloak = Substitute.For<IKeycloakService>();
        keycloak.LoginAsync("student@test.local", TestPassword)
            .Returns(SuccessfulTokenResponse("kc-sub-1", "student@test.local", "Student"));

        var controller = NewController(keycloak, context);

        var result = await controller.Login(ExampleLogin("student@test.local", TestPassword));

        // Keycloak authentication succeeded — the caller must get their token back even though
        // the audit-only outbox write behind it failed.
        result.ShouldBeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Login_failure_is_not_masked_by_an_outbox_write_failure()
    {
        await using var context = _db.NewContext();
        await context.Database.CloseConnectionAsync();

        var keycloak = Substitute.For<IKeycloakService>();
        keycloak.LoginAsync("bad@test.local", TestPassword)
            .Returns<TokenResponseDto>(_ => throw new KeycloakException("invalid_grant", 401));

        var controller = NewController(keycloak, context);

        // The original Keycloak failure must still surface — not an unrelated DB exception
        // from the outbox write that ran (and failed) inside the catch block.
        await Should.ThrowAsync<KeycloakException>(() =>
            controller.Login(ExampleLogin("bad@test.local", TestPassword)));
    }

    public void Dispose() => _db.Dispose();
}
