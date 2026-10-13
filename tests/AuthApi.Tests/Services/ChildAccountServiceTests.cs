using System.Collections.Concurrent;
using System.Reflection;
using AuthApi.Tests.Support;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using ExamApp.Foundation.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AuthApi.Tests.Services;

/// <summary>
/// Issue #438: auth-api çocuk hesabı servisi (yalnız exam API servis token'ı). Keycloak (substitute) + identity satırı; Keycloak
/// kullanıcısı oluştuktan sonraki her hatada kullanıcı silinir (parola politikası reddi, rol ataması, DB). Silme ve şifre sıfırlama
/// yalnız <c>parent_managed=true</c> işaretli hesaplarda. Parola hiçbir log satırında yer almaz.
/// </summary>
public class ChildAccountServiceTests : IDisposable
{
    private const string Pw = "Kalem2026x"; // test-only example
    private const string KcId = "kc-child-1";

    private readonly TestDb _db = TestDb.Create();
    private readonly IKeycloakService _keycloak = Substitute.For<IKeycloakService>();
    private readonly CapturingLogger _logger = new();
    private readonly Dictionary<string, string> _attributes = new()
    {
        [ChildAccountRules.KeycloakParentManagedAttribute] = ChildAccountRules.KeycloakParentManagedValue
    };

    public ChildAccountServiceTests()
    {
        _keycloak.CreateParentManagedUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(KcId);
        _keycloak.GetRealmRoleAsync("Student", Arg.Any<CancellationToken>()).Returns(new KeycloakRoleDto { id = "r-student", name = "Student" });
        _keycloak.GetUserAttributesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => (IReadOnlyDictionary<string, string>)_attributes);
        _keycloak.TryDeleteUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    public void Dispose() => _db.Dispose();

    private sealed class CapturingLogger : ILogger<ChildAccountService>
    {
        public ConcurrentQueue<string> Lines { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Lines.Enqueue(formatter(state, exception) + (exception == null ? string.Empty : " | " + exception));
    }

    private ChildAccountService Service(AppDbContext ctx) => new(ctx, _keycloak, _logger);

    private static CreateChildAccountRequest Request(string username = " Ali.Kaya ", string pw = Pw)
    {
        var r = new CreateChildAccountRequest { Username = username, FirstName = " Ali ", LastName = "Kaya" };
        r.Password = pw;
        return r;
    }

    private async Task<ChildAccountResult> CreateAsync(CreateChildAccountRequest? request = null, AppDbContext? ctx = null)
    {
        if (ctx != null)
            return await Service(ctx).CreateAsync(request ?? Request());
        await using var c = _db.NewContext();
        return await Service(c).CreateAsync(request ?? Request());
    }

    private void AssertNoPasswordInLogs() => _logger.Lines.ShouldAllBe(l => !l.Contains(Pw));

    [Fact]
    public async Task Create_makes_an_email_less_keycloak_user_with_the_student_role_and_an_identity_row()
    {
        var result = await CreateAsync();

        result.Outcome.ShouldBe(ChildAccountOutcome.Success);
        result.Created!.KeycloakId.ShouldBe(KcId);
        await _keycloak.Received(1).CreateParentManagedUserAsync("ali.kaya", "Ali", "Kaya", Arg.Any<CancellationToken>());
        await _keycloak.Received(1).ResetPasswordAsync(KcId, Pw, Arg.Any<CancellationToken>());
        await _keycloak.Received(1).AddRealmRoleMappingAsync(KcId, Arg.Is<KeycloakRoleDto>(r => r.name == "Student"), Arg.Any<CancellationToken>());
        await _keycloak.DidNotReceive().TryDeleteUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        await using var check = _db.NewContext();
        var user = await check.Users.SingleAsync();
        user.Id.ShouldBe(result.Created.UserId);
        user.KeycloakId.ShouldBe(KcId);
        user.Email.ShouldBeEmpty();
        user.Role.ShouldBe("Student");
        user.FullName.ShouldBe("Ali Kaya");
        user.PasswordHash.ShouldBeNull();
        (await check.OutboxMessages.SingleAsync()).Type.ShouldBe(OutboxEventRegistry.NameFor<UserPreferredLocaleChangedEvent>());
        AssertNoPasswordInLogs();
    }

    [Fact]
    public async Task Username_conflict_is_409_without_compensation()
    {
        _keycloak.CreateParentManagedUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new KeycloakException("exists", 409, KeycloakFailureKind.Conflict));

        var result = await CreateAsync();

        result.Outcome.ShouldBe(ChildAccountOutcome.Conflict);
        result.ErrorCode.ShouldBe(ChildAccountErrorCodes.UsernameTaken);
        await _keycloak.DidNotReceive().TryDeleteUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _keycloak.DidNotReceive().ResetPasswordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Keycloak_rejecting_the_user_is_400_InvalidUsername()
    {
        _keycloak.CreateParentManagedUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new KeycloakException("bad", 400, KeycloakFailureKind.Validation));

        (await CreateAsync()).ErrorCode.ShouldBe(ChildAccountErrorCodes.InvalidUsername);
    }

    [Fact]
    public async Task Keycloak_unreachable_on_create_is_upstream_failure()
    {
        _keycloak.CreateParentManagedUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new HttpRequestException("down"));

        (await CreateAsync()).Outcome.ShouldBe(ChildAccountOutcome.UpstreamFailure);
        await using var check = _db.NewContext();
        (await check.Users.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Password_policy_rejection_deletes_the_keycloak_user()
    {
        _keycloak.ResetPasswordAsync(KcId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new KeycloakException("invalidPasswordMinLengthMessage", 400, KeycloakFailureKind.Validation));

        var result = await CreateAsync();

        result.Outcome.ShouldBe(ChildAccountOutcome.Invalid);
        result.ErrorCode.ShouldBe(ChildAccountErrorCodes.PasswordRejected);
        await _keycloak.Received(1).TryDeleteUserAsync(KcId, Arg.Any<CancellationToken>());
        await using var check = _db.NewContext();
        (await check.Users.CountAsync()).ShouldBe(0);
        AssertNoPasswordInLogs();
    }

    [Fact]
    public async Task Role_assignment_failure_deletes_the_keycloak_user()
    {
        _keycloak.AddRealmRoleMappingAsync(KcId, Arg.Any<KeycloakRoleDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new KeycloakException("role failed"));

        var result = await CreateAsync();

        result.Outcome.ShouldBe(ChildAccountOutcome.UpstreamFailure);
        await _keycloak.Received(1).TryDeleteUserAsync(KcId, Arg.Any<CancellationToken>());
        AssertNoPasswordInLogs();
    }

    private sealed class FailOnUserInsert : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<User>().Any(e => e.State == EntityState.Added))
                throw new DbUpdateException("simulated identity DB failure");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task Identity_db_failure_deletes_the_keycloak_user()
    {
        await using var ctx = _db.NewContext(new FailOnUserInsert());

        var result = await CreateAsync(ctx: ctx);

        result.Outcome.ShouldBe(ChildAccountOutcome.UpstreamFailure);
        await _keycloak.Received(1).TryDeleteUserAsync(KcId, Arg.Any<CancellationToken>());
        AssertNoPasswordInLogs();
    }

    [Theory]
    [InlineData("ab", Pw, ChildAccountErrorCodes.InvalidUsername)]
    [InlineData("ali@kaya.com", Pw, ChildAccountErrorCodes.InvalidUsername)]
    [InlineData("ali.kaya", "kisa1", ChildAccountErrorCodes.PasswordRejected)]
    [InlineData("ali.kaya", "ali.kaya2026", ChildAccountErrorCodes.PasswordRejected)]
    public async Task Invalid_input_never_reaches_keycloak(string username, string pw, string code)
    {
        var result = await CreateAsync(Request(username, pw));

        result.Outcome.ShouldBe(ChildAccountOutcome.Invalid);
        result.ErrorCode.ShouldBe(code);
        await _keycloak.DidNotReceive().CreateParentManagedUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Blank_names_are_rejected()
    {
        var r = Request();
        r.FirstName = "  ";
        (await CreateAsync(r)).ErrorCode.ShouldBe(ChildAccountErrorCodes.InvalidName);
    }

    // ---- silme (kompanzasyon) -----------------------------------------------------------------------------------------

    private async Task<int> SeedUserAsync(string role = "Student", string kc = KcId)
    {
        await using var ctx = _db.NewContext();
        var user = new User { KeycloakId = kc, Email = string.Empty, FullName = "Ali Kaya", Role = role };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task Delete_removes_a_parent_managed_account_from_keycloak_and_identity()
    {
        var id = await SeedUserAsync();
        await using var ctx = _db.NewContext();

        (await Service(ctx).DeleteAsync(id)).Outcome.ShouldBe(ChildAccountOutcome.Success);

        await _keycloak.Received(1).TryDeleteUserAsync(KcId, Arg.Any<CancellationToken>());
        await using var check = _db.NewContext();
        (await check.Users.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Delete_refuses_an_account_without_the_parent_managed_marker()
    {
        _attributes.Clear();
        var id = await SeedUserAsync();
        await using var ctx = _db.NewContext();

        (await Service(ctx).DeleteAsync(id)).Outcome.ShouldBe(ChildAccountOutcome.NotFound);

        await _keycloak.DidNotReceive().TryDeleteUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await using var check = _db.NewContext();
        (await check.Users.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Delete_refuses_non_student_accounts_and_unknown_ids()
    {
        var teacher = await SeedUserAsync("Teacher", "kc-teacher");
        await using var ctx = _db.NewContext();

        (await Service(ctx).DeleteAsync(teacher)).Outcome.ShouldBe(ChildAccountOutcome.NotFound);
        (await Service(ctx).DeleteAsync(999)).Outcome.ShouldBe(ChildAccountOutcome.NotFound);
        await _keycloak.DidNotReceive().TryDeleteUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_when_keycloak_user_is_already_gone_still_removes_the_identity_row()
    {
        _keycloak.GetUserAttributesAsync(KcId, Arg.Any<CancellationToken>())
            .Returns<IReadOnlyDictionary<string, string>>(_ => throw new KeycloakException("missing", 404));
        var id = await SeedUserAsync();
        await using var ctx = _db.NewContext();

        (await Service(ctx).DeleteAsync(id)).Outcome.ShouldBe(ChildAccountOutcome.Success);

        await using var check = _db.NewContext();
        (await check.Users.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    // ---- şifre sıfırlama ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Reset_sets_a_permanent_password_and_ends_sessions()
    {
        var id = await SeedUserAsync();
        await using var ctx = _db.NewContext();

        (await Service(ctx).ResetPasswordAsync(id, Pw)).Outcome.ShouldBe(ChildAccountOutcome.Success);

        await _keycloak.Received(1).ResetPasswordAsync(KcId, Pw, Arg.Any<CancellationToken>());
        await _keycloak.Received(1).LogoutAsync(KcId);
        AssertNoPasswordInLogs();
    }

    [Fact]
    public async Task Reset_succeeds_even_if_session_logout_fails()
    {
        _keycloak.LogoutAsync(KcId).Returns(_ => throw new KeycloakException("logout failed"));
        var id = await SeedUserAsync();
        await using var ctx = _db.NewContext();

        (await Service(ctx).ResetPasswordAsync(id, Pw)).Outcome.ShouldBe(ChildAccountOutcome.Success);
    }

    [Fact]
    public async Task Reset_refuses_unmanaged_non_student_and_unknown_accounts()
    {
        var teacher = await SeedUserAsync("Teacher", "kc-teacher");
        await using var ctx = _db.NewContext();
        (await Service(ctx).ResetPasswordAsync(teacher, Pw)).Outcome.ShouldBe(ChildAccountOutcome.NotFound);
        (await Service(ctx).ResetPasswordAsync(999, Pw)).Outcome.ShouldBe(ChildAccountOutcome.NotFound);

        _attributes.Clear();
        var student = await SeedUserAsync();
        (await Service(ctx).ResetPasswordAsync(student, Pw)).Outcome.ShouldBe(ChildAccountOutcome.NotFound);

        await _keycloak.DidNotReceive().ResetPasswordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reset_maps_policy_rejection_and_validates_locally_first()
    {
        var id = await SeedUserAsync();
        await using var ctx = _db.NewContext();
        (await Service(ctx).ResetPasswordAsync(id, "kisa")).ErrorCode.ShouldBe(ChildAccountErrorCodes.PasswordRejected);
        await _keycloak.DidNotReceive().ResetPasswordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        _keycloak.ResetPasswordAsync(KcId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new KeycloakException("policy", 400, KeycloakFailureKind.Validation));
        var result = await Service(ctx).ResetPasswordAsync(id, Pw);
        result.Outcome.ShouldBe(ChildAccountOutcome.Invalid);
        result.ErrorCode.ShouldBe(ChildAccountErrorCodes.PasswordRejected);
        AssertNoPasswordInLogs();
    }

    [Fact]
    public void Controller_is_service_only()
    {
        var authorize = typeof(ChildAccountsController).GetCustomAttribute<AuthorizeAttribute>().ShouldNotBeNull();
        authorize.Policy.ShouldBe("Service");
        typeof(ChildAccountsController).GetMethods()
            .SelectMany(m => m.GetCustomAttributes<AllowAnonymousAttribute>())
            .ShouldBeEmpty();
    }
}
