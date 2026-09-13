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

    // ---- UpdatePreferredLocale: outbox event writing (issue #185) ----

    [Fact]
    public async Task UpdatePreferredLocale_value_changes_writes_UserPreferredLocaleChangedEvent_to_outbox()
    {
        await using var context = _db.NewContext();
        var sub = "user-sub-outbox";
        var user = new User { KeycloakId = sub, Email = "user@test.local", FullName = "Test User", Role = "Student", PreferredLocale = "tr" };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var keycloak = Substitute.For<IKeycloakService>();
        var controller = NewController(keycloak, context);
        controller.ControllerContext.HttpContext.User = new System.Security.Principal.GenericPrincipal(
            new System.Security.Claims.ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, sub)
            }), null);

        var result = await controller.UpdatePreferredLocale(new ExamApp.Api.Models.Requests.UpdatePreferredLocaleRequest { PreferredLocale = "en" });

        result.ShouldBeOfType<OkObjectResult>();

        // Verify outbox message was created
        await using var check = _db.NewContext();
        var outboxRow = await check.OutboxMessages.SingleAsync();
        outboxRow.Type.ShouldBe(OutboxEventRegistry.NameFor<UserPreferredLocaleChangedEvent>());

        var evt = JsonSerializer.Deserialize<UserPreferredLocaleChangedEvent>(outboxRow.Content)!;
        evt.UserId.ShouldBe(user.Id);
        evt.KeycloakId.ShouldBe(sub);
        evt.PreferredLocale.ShouldBe("en");
        evt.ChangedAtUtc.ShouldNotBe(default(DateTime));
    }

    [Fact]
    public async Task UpdatePreferredLocale_same_value_does_not_write_outbox_event()
    {
        await using var context = _db.NewContext();
        var sub = "user-sub-same";
        var user = new User { KeycloakId = sub, Email = "user@test.local", FullName = "Test User", Role = "Student", PreferredLocale = "en" };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var keycloak = Substitute.For<IKeycloakService>();
        var controller = NewController(keycloak, context);
        controller.ControllerContext.HttpContext.User = new System.Security.Principal.GenericPrincipal(
            new System.Security.Claims.ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, sub)
            }), null);

        await controller.UpdatePreferredLocale(new ExamApp.Api.Models.Requests.UpdatePreferredLocaleRequest { PreferredLocale = "en" });

        // Verify no outbox message was created (no change)
        await using var check = _db.NewContext();
        (await check.OutboxMessages.CountAsync()).ShouldBe(0);
    }

    // ---- UpdatePreferredLocale: valid requests ----

    [Fact]
    public async Task UpdatePreferredLocale_valid_locale_updates_profile_and_returns_200()
    {
        await using var context = _db.NewContext();
        var sub = "user-sub-1";
        var user = new User { KeycloakId = sub, Email = "user@test.local", FullName = "Test User", Role = "Student" };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var keycloak = Substitute.For<IKeycloakService>();
        var controller = NewController(keycloak, context);
        controller.ControllerContext.HttpContext.User = new System.Security.Principal.GenericPrincipal(
            new System.Security.Claims.ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, sub)
            }), null);

        var result = await controller.UpdatePreferredLocale(new ExamApp.Api.Models.Requests.UpdatePreferredLocaleRequest { PreferredLocale = "en" });

        result.ShouldBeOfType<OkObjectResult>();
        var returnedProfile = ((OkObjectResult)result).Value.ShouldBeOfType<UserProfileDto>();
        returnedProfile.PreferredLocale.ShouldBe("en");

        // Verify the database change persisted
        await using var check = _db.NewContext();
        var updatedUser = await check.Users.FirstOrDefaultAsync(u => u.KeycloakId == sub);
        updatedUser.ShouldNotBeNull();
        updatedUser.PreferredLocale.ShouldBe("en");
    }

    [Fact]
    public async Task UpdatePreferredLocale_case_insensitive_locale_normalizes_and_updates()
    {
        await using var context = _db.NewContext();
        var sub = "user-sub-2";
        var user = new User { KeycloakId = sub, Email = "user2@test.local", FullName = "Test User 2", Role = "Teacher", PreferredLocale = "tr" };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var keycloak = Substitute.For<IKeycloakService>();
        var controller = NewController(keycloak, context);
        controller.ControllerContext.HttpContext.User = new System.Security.Principal.GenericPrincipal(
            new System.Security.Claims.ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, sub)
            }), null);

        var result = await controller.UpdatePreferredLocale(new ExamApp.Api.Models.Requests.UpdatePreferredLocaleRequest { PreferredLocale = "EN" });

        result.ShouldBeOfType<OkObjectResult>();
        var returnedProfile = ((OkObjectResult)result).Value.ShouldBeOfType<UserProfileDto>();
        returnedProfile.PreferredLocale.ShouldBe("en");
    }

    [Fact]
    public async Task UpdatePreferredLocale_same_value_returns_200_without_updating_timestamp()
    {
        await using var context = _db.NewContext();
        var sub = "user-sub-3";
        var user = new User { KeycloakId = sub, Email = "user3@test.local", FullName = "Test User 3", Role = "Student", PreferredLocale = "en" };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var originalUpdateTime = user.UpdateTime;

        var keycloak = Substitute.For<IKeycloakService>();
        var controller = NewController(keycloak, context);
        controller.ControllerContext.HttpContext.User = new System.Security.Principal.GenericPrincipal(
            new System.Security.Claims.ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, sub)
            }), null);

        await controller.UpdatePreferredLocale(new ExamApp.Api.Models.Requests.UpdatePreferredLocaleRequest { PreferredLocale = "en" });

        // Verify UpdateTime didn't change (optimization)
        await using var check = _db.NewContext();
        var unchangedUser = await check.Users.FirstOrDefaultAsync(u => u.KeycloakId == sub);
        unchangedUser.ShouldNotBeNull();
        unchangedUser.UpdateTime.ShouldBe(originalUpdateTime);
    }

    // ---- UpdatePreferredLocale: validation errors ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdatePreferredLocale_null_or_empty_locale_returns_400(string? locale)
    {
        await using var context = _db.NewContext();
        var keycloak = Substitute.For<IKeycloakService>();
        var controller = NewController(keycloak, context);
        controller.ControllerContext.HttpContext.User = new System.Security.Principal.GenericPrincipal(
            new System.Security.Claims.ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "user-sub")
            }), null);

        var result = await controller.UpdatePreferredLocale(new ExamApp.Api.Models.Requests.UpdatePreferredLocaleRequest { PreferredLocale = locale });

        result.ShouldBeOfType<ObjectResult>();
        ((ObjectResult)result).StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task UpdatePreferredLocale_unsupported_locale_returns_400()
    {
        await using var context = _db.NewContext();
        var keycloak = Substitute.For<IKeycloakService>();
        var controller = NewController(keycloak, context);
        controller.ControllerContext.HttpContext.User = new System.Security.Principal.GenericPrincipal(
            new System.Security.Claims.ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "user-sub")
            }), null);

        var result = await controller.UpdatePreferredLocale(new ExamApp.Api.Models.Requests.UpdatePreferredLocaleRequest { PreferredLocale = "de" });

        result.ShouldBeOfType<ObjectResult>();
        ((ObjectResult)result).StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task UpdatePreferredLocale_no_sub_claim_returns_401()
    {
        await using var context = _db.NewContext();
        var keycloak = Substitute.For<IKeycloakService>();
        var controller = NewController(keycloak, context);
        controller.ControllerContext.HttpContext.User = new System.Security.Principal.GenericPrincipal(
            new System.Security.Claims.ClaimsIdentity(new[] { new Claim("email", "test@test.local") }), null);

        var result = await controller.UpdatePreferredLocale(new ExamApp.Api.Models.Requests.UpdatePreferredLocaleRequest { PreferredLocale = "en" });

        result.ShouldBeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task UpdatePreferredLocale_user_not_found_returns_404()
    {
        await using var context = _db.NewContext();
        var keycloak = Substitute.For<IKeycloakService>();
        var controller = NewController(keycloak, context);
        controller.ControllerContext.HttpContext.User = new System.Security.Principal.GenericPrincipal(
            new System.Security.Claims.ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "nonexistent-user-sub")
            }), null);

        var result = await controller.UpdatePreferredLocale(new ExamApp.Api.Models.Requests.UpdatePreferredLocaleRequest { PreferredLocale = "en" });

        result.ShouldBeOfType<ObjectResult>();
        ((ObjectResult)result).StatusCode.ShouldBe(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task UpdatePreferredLocale_soft_deleted_user_returns_404()
    {
        await using var context = _db.NewContext();
        var sub = "deleted-user-sub";
        var user = new User { KeycloakId = sub, Email = "deleted@test.local", FullName = "Deleted User", Role = "Student", IsDeleted = true };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var keycloak = Substitute.For<IKeycloakService>();
        var controller = NewController(keycloak, context);
        controller.ControllerContext.HttpContext.User = new System.Security.Principal.GenericPrincipal(
            new System.Security.Claims.ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, sub)
            }), null);

        var result = await controller.UpdatePreferredLocale(new ExamApp.Api.Models.Requests.UpdatePreferredLocaleRequest { PreferredLocale = "en" });

        result.ShouldBeOfType<ObjectResult>();
        ((ObjectResult)result).StatusCode.ShouldBe(StatusCodes.Status404NotFound);
    }

    // ---- GetUserProfile returns PreferredLocale ----

    [Fact]
    public async Task UserProfile_returns_preferred_locale()
    {
        await using var context = _db.NewContext();
        var sub = "user-with-pref";
        var user = new User { KeycloakId = sub, Email = "pref@test.local", FullName = "Pref User", Role = "Student", PreferredLocale = "en" };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var keycloak = Substitute.For<IKeycloakService>();
        var controller = NewController(keycloak, context);
        controller.ControllerContext.HttpContext.User = new System.Security.Principal.GenericPrincipal(
            new System.Security.Claims.ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, sub)
            }), null);

        var result = await controller.UserProfile();

        result.ShouldBeOfType<OkObjectResult>();
        var profile = ((OkObjectResult)result).Value.ShouldBeOfType<UserProfileDto>();
        profile.PreferredLocale.ShouldBe("en");
    }

    public void Dispose() => _db.Dispose();
}
