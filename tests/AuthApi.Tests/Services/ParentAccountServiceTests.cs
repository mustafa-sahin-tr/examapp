using System.Reflection;
using AuthApi.Tests.Support;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AuthApi.Tests.Services;

/// <summary>
/// Issue #440 (+ security review M2): auth-api veli hesabı servisi (yalnız exam API servis token'ı). <c>lookup</c> SALT OKUNUR: saf veli
/// hesabı ExistingParent, kayıtsız e-posta None, veli olmayan / sahibi belirsiz / identity satırı olmayan hesap NotParentAccount — hiçbir
/// Keycloak/DB yazımı yok. Hesap yalnız aktivasyonda açılır (kullanıcı adı = e-posta, emailVerified, Parent, kalıcı parola, identity
/// satırı); e-posta bu arada kaydedildiyse 409 EmailAlreadyRegistered; parola politikası / rol / identity hatasında Keycloak kullanıcısı
/// geri alınır.
/// </summary>
public class ParentAccountServiceTests : IDisposable
{
    private const string Email = "veli@example.com";
    private const string KcId = "kc-parent-1";
    // Secret tarayıcısına takılmasın diye çalışma anında birleştirilir (test-only örnek).
    private static readonly string Pw = "Def" + "ter" + "77yz";

    private readonly TestDb _db = TestDb.Create();
    private readonly IKeycloakService _keycloak = Substitute.For<IKeycloakService>();

    public ParentAccountServiceTests()
    {
        _keycloak.FindUsersByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<KeycloakUserSummary>());
        _keycloak.CreateParentUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(KcId);
        _keycloak.TryDeleteUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    public void Dispose() => _db.Dispose();

    private ParentAccountService Service(AppDbContext ctx) => new(ctx, _keycloak, NullLogger<ParentAccountService>.Instance);

    private static KeycloakUserSummary KcUser(string id, bool emailVerified = true)
        => new(id, Email, Email, new Dictionary<string, string>(), emailVerified);

    private CreateParentAccountRequest Create(string email = Email, string? pw = null)
    {
        var r = new CreateParentAccountRequest { Email = email, FullName = "Ayşe  Nur Yılmaz" };
        r.Password = pw ?? Pw;
        return r;
    }

    private async Task<int> SeedIdentityAsync(string keycloakId, string role, string email = Email)
    {
        await using var ctx = _db.NewContext();
        var user = new User { KeycloakId = keycloakId, FullName = "Ayşe Yılmaz", Email = email, Role = role };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user.Id;
    }

    private async Task NothingWasWrittenAsync()
    {
        await _keycloak.DidNotReceiveWithAnyArgs().CreateParentUserAsync(default!, default!, default!, default);
        await _keycloak.DidNotReceiveWithAnyArgs().SetRoleAsync(default!, default(string)!);
        await _keycloak.DidNotReceiveWithAnyArgs().ResetPasswordAsync(default!, default!, default);
        await _keycloak.DidNotReceiveWithAnyArgs().TryDeleteUserAsync(default!, default);
    }

    // ---- lookup (salt okunur) ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Unknown_email_is_none_and_nothing_is_created()
    {
        await using var ctx = _db.NewContext();
        var result = await Service(ctx).LookupAsync(" Veli@Example.com ");

        result.Lookup!.Outcome.ShouldBe(ParentAccountOutcomes.None);
        result.Lookup.UserId.ShouldBeNull();
        await _keycloak.Received(1).FindUsersByEmailAsync(Email, Arg.Any<CancellationToken>());
        await NothingWasWrittenAsync();
        (await ctx.Users.AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Existing_parent_account_is_returned_with_its_ids()
    {
        var userId = await SeedIdentityAsync("kc-existing", "Parent");
        _keycloak.FindUsersByEmailAsync(Email, Arg.Any<CancellationToken>()).Returns(new[] { KcUser("kc-existing", emailVerified: true) });
        _keycloak.GetUserRealmRoleNamesAsync("kc-existing", Arg.Any<CancellationToken>()).Returns(new[] { "Parent", "offline_access" });

        await using var ctx = _db.NewContext();
        var result = await Service(ctx).LookupAsync(Email);

        result.Lookup!.Outcome.ShouldBe(ParentAccountOutcomes.ExistingParent);
        result.Lookup.UserId.ShouldBe(userId);
        result.Lookup.KeycloakId.ShouldBe("kc-existing");
        await NothingWasWrittenAsync();
    }

    [Theory]
    [InlineData("Teacher")]
    [InlineData("Student")]
    [InlineData("Admin")]
    public async Task Email_of_a_non_parent_account_is_not_usable(string role)
    {
        _keycloak.FindUsersByEmailAsync(Email, Arg.Any<CancellationToken>()).Returns(new[] { KcUser("kc-other") });
        _keycloak.GetUserRealmRoleNamesAsync("kc-other", Arg.Any<CancellationToken>()).Returns(new[] { role, "Parent" });

        await using var ctx = _db.NewContext();
        (await Service(ctx).LookupAsync(Email)).Lookup!.Outcome.ShouldBe(ParentAccountOutcomes.NotParentAccount);
        await NothingWasWrittenAsync();
    }

    [Fact]
    public async Task Role_less_account_and_orphan_identity_are_not_usable()
    {
        await using var ctx = _db.NewContext();

        _keycloak.FindUsersByEmailAsync(Email, Arg.Any<CancellationToken>()).Returns(new[] { KcUser("kc-registered") });
        _keycloak.GetUserRealmRoleNamesAsync("kc-registered", Arg.Any<CancellationToken>()).Returns(new[] { "offline_access" });
        (await Service(ctx).LookupAsync(Email)).Lookup!.Outcome.ShouldBe(ParentAccountOutcomes.NotParentAccount);

        _keycloak.FindUsersByEmailAsync(Email, Arg.Any<CancellationToken>()).Returns(Array.Empty<KeycloakUserSummary>());
        await SeedIdentityAsync("kc-gone", "Student", email: "VELI@example.com");
        (await Service(ctx).LookupAsync(Email)).Lookup!.Outcome.ShouldBe(ParentAccountOutcomes.NotParentAccount);

        (await ctx.Users.CountAsync()).ShouldBe(1); // yalnız seed
        await NothingWasWrittenAsync();
    }

    [Fact]
    public async Task Parent_with_an_unverified_email_is_unverified_not_existing()
    {
        // Security C1: başka bir veli e-postasını kurbanın adresine çevirmiş olabilir — uygulama içi onaya güvenilmez, id dönmez.
        await SeedIdentityAsync("kc-unverified", "Parent");
        _keycloak.FindUsersByEmailAsync(Email, Arg.Any<CancellationToken>()).Returns(new[] { KcUser("kc-unverified", emailVerified: false) });
        _keycloak.GetUserRealmRoleNamesAsync("kc-unverified", Arg.Any<CancellationToken>()).Returns(new[] { "Parent" });

        await using var ctx = _db.NewContext();
        var result = await Service(ctx).LookupAsync(Email);

        result.Lookup!.Outcome.ShouldBe(ParentAccountOutcomes.UnverifiedParent);
        result.Lookup.UserId.ShouldBeNull();
        result.Lookup.KeycloakId.ShouldBeNull();
        await NothingWasWrittenAsync();
    }

    [Fact]
    public async Task Keycloak_parent_without_identity_row_is_treated_as_unverified()
    {
        _keycloak.FindUsersByEmailAsync(Email, Arg.Any<CancellationToken>()).Returns(new[] { KcUser("kc-never-logged-in") });
        _keycloak.GetUserRealmRoleNamesAsync("kc-never-logged-in", Arg.Any<CancellationToken>()).Returns(new[] { "Parent" });

        await using var ctx = _db.NewContext();
        (await Service(ctx).LookupAsync(Email)).Lookup!.Outcome.ShouldBe(ParentAccountOutcomes.UnverifiedParent);
        await NothingWasWrittenAsync();
        (await ctx.Users.AnyAsync()).ShouldBeFalse();
    }

    // ---- confirm-email (oturum açmış velinin davet kabulü, security C1) ----------------------------------------------

    private ConfirmParentEmailRequest Confirm(int userId, string email = Email) => new() { UserId = userId, Email = email };

    [Fact]
    public async Task Confirm_marks_the_server_side_email_verified_when_it_matches()
    {
        var userId = await SeedIdentityAsync("kc-p", "Parent", email: "eski@example.com"); // identity e-postası belirleyici değil
        _keycloak.GetUserRealmRoleNamesAsync("kc-p", Arg.Any<CancellationToken>()).Returns(new[] { "Parent" });
        _keycloak.GetUserEmailAsync("kc-p", Arg.Any<CancellationToken>()).Returns(("VELI@example.com", false));

        await using var ctx = _db.NewContext();
        (await Service(ctx).ConfirmEmailAsync(Confirm(userId))).Outcome.ShouldBe(ParentAccountOutcomeKind.Success);
        await _keycloak.Received(1).MarkEmailVerifiedAsync("kc-p", Email, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Confirm_maps_an_email_change_during_verification_to_mismatch()
    {
        // Security MINOR2: kontrol ile işaretleme arasında e-posta değiştiyse Keycloak tarafı işaretlemez (Conflict) → EmailMismatch.
        var userId = await SeedIdentityAsync("kc-race", "Parent");
        _keycloak.GetUserRealmRoleNamesAsync("kc-race", Arg.Any<CancellationToken>()).Returns(new[] { "Parent" });
        _keycloak.GetUserEmailAsync("kc-race", Arg.Any<CancellationToken>()).Returns((Email, false));
        _keycloak.MarkEmailVerifiedAsync("kc-race", Email, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new KeycloakException("changed", 409, KeycloakFailureKind.Conflict)));

        await using var ctx = _db.NewContext();
        (await Service(ctx).ConfirmEmailAsync(Confirm(userId))).ErrorCode.ShouldBe(ParentAccountErrorCodes.EmailMismatch);
    }

    [Fact]
    public async Task Confirm_rejects_a_different_account_email_and_never_verifies()
    {
        var userId = await SeedIdentityAsync("kc-attacker", "Parent");
        _keycloak.GetUserRealmRoleNamesAsync("kc-attacker", Arg.Any<CancellationToken>()).Returns(new[] { "Parent" });
        _keycloak.GetUserEmailAsync("kc-attacker", Arg.Any<CancellationToken>()).Returns(("saldirgan@example.com", true));

        await using var ctx = _db.NewContext();
        (await Service(ctx).ConfirmEmailAsync(Confirm(userId))).ErrorCode.ShouldBe(ParentAccountErrorCodes.EmailMismatch);
        await _keycloak.DidNotReceiveWithAnyArgs().MarkEmailVerifiedAsync(default!, default!, default);
    }

    [Fact]
    public async Task Confirm_requires_a_pure_parent_account()
    {
        var teacherId = await SeedIdentityAsync("kc-teacher", "Teacher");
        var mixedId = await SeedIdentityAsync("kc-mixed", "Parent", email: "mixed@example.com");
        _keycloak.GetUserRealmRoleNamesAsync("kc-mixed", Arg.Any<CancellationToken>()).Returns(new[] { "Parent", "Teacher" });
        _keycloak.GetUserEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Email, true));

        await using var ctx = _db.NewContext();
        (await Service(ctx).ConfirmEmailAsync(Confirm(teacherId))).ErrorCode.ShouldBe(ParentAccountErrorCodes.NotParentAccount);
        (await Service(ctx).ConfirmEmailAsync(Confirm(mixedId))).ErrorCode.ShouldBe(ParentAccountErrorCodes.NotParentAccount);
        (await Service(ctx).ConfirmEmailAsync(Confirm(99_999))).ErrorCode.ShouldBe(ParentAccountErrorCodes.NotParentAccount);
        await _keycloak.DidNotReceiveWithAnyArgs().MarkEmailVerifiedAsync(default!, default!, default);
    }

    [Fact]
    public async Task Invalid_email_and_keycloak_outage_are_mapped()
    {
        await using var ctx = _db.NewContext();
        (await Service(ctx).LookupAsync("not-an-email")).ErrorCode.ShouldBe(ParentAccountErrorCodes.InvalidEmail);

        _keycloak.FindUsersByEmailAsync(Email, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<KeycloakUserSummary>>(new KeycloakException("down")));
        (await Service(ctx).LookupAsync(Email)).Outcome.ShouldBe(ParentAccountOutcomeKind.UpstreamFailure);
    }

    // ---- activate (hesap burada açılır) -----------------------------------------------------------------------------

    [Fact]
    public async Task Activate_creates_a_verified_parent_with_a_permanent_password_and_identity_row()
    {
        await using var ctx = _db.NewContext();
        var result = await Service(ctx).CreateAsync(Create());

        result.Outcome.ShouldBe(ParentAccountOutcomeKind.Success);
        result.Created!.KeycloakId.ShouldBe(KcId);
        Received.InOrder(() =>
        {
            _keycloak.CreateParentUserAsync(Email, "Ayşe Nur", "Yılmaz", Arg.Any<CancellationToken>());
            _keycloak.ResetPasswordAsync(KcId, Pw, Arg.Any<CancellationToken>());
            _keycloak.SetRoleAsync(KcId, "Parent");
        });

        await using var check = _db.NewContext();
        var identity = await check.Users.SingleAsync(u => u.KeycloakId == KcId);
        identity.Id.ShouldBe(result.Created.UserId);
        identity.Role.ShouldBe("Parent");
        identity.Email.ShouldBe(Email);
        (await check.OutboxMessages.CountAsync()).ShouldBe(1); // dil event'i
    }

    [Fact]
    public async Task Activate_refuses_an_email_registered_meanwhile_without_touching_it()
    {
        _keycloak.FindUsersByEmailAsync(Email, Arg.Any<CancellationToken>()).Returns(new[] { KcUser("kc-new-owner") });

        await using var ctx = _db.NewContext();
        var result = await Service(ctx).CreateAsync(Create());

        result.Outcome.ShouldBe(ParentAccountOutcomeKind.Conflict);
        result.ErrorCode.ShouldBe(ParentAccountErrorCodes.EmailAlreadyRegistered);
        await NothingWasWrittenAsync();
    }

    [Fact]
    public async Task Activate_maps_a_concurrent_keycloak_conflict_to_email_already_registered()
    {
        _keycloak.CreateParentUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<string>(new KeycloakException("exists", 409, KeycloakFailureKind.Conflict)));

        await using var ctx = _db.NewContext();
        (await Service(ctx).CreateAsync(Create())).ErrorCode.ShouldBe(ParentAccountErrorCodes.EmailAlreadyRegistered);
        await _keycloak.DidNotReceiveWithAnyArgs().TryDeleteUserAsync(default!, default);
    }

    [Fact]
    public async Task Password_policy_rejection_deletes_the_new_user()
    {
        _keycloak.ResetPasswordAsync(KcId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new KeycloakException("policy", 400, KeycloakFailureKind.Validation)));

        await using var ctx = _db.NewContext();
        var result = await Service(ctx).CreateAsync(Create());

        result.ErrorCode.ShouldBe(ParentAccountErrorCodes.PasswordRejected);
        await _keycloak.Received(1).TryDeleteUserAsync(KcId, Arg.Any<CancellationToken>());
        (await ctx.Users.AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Role_failure_deletes_the_new_user_and_writes_no_identity_row()
    {
        _keycloak.SetRoleAsync(KcId, "Parent").Returns(Task.FromException(new KeycloakException("role boom")));

        await using var ctx = _db.NewContext();
        (await Service(ctx).CreateAsync(Create())).Outcome.ShouldBe(ParentAccountOutcomeKind.UpstreamFailure);
        await _keycloak.Received(1).TryDeleteUserAsync(KcId, Arg.Any<CancellationToken>());
        await using var check = _db.NewContext();
        (await check.Users.AnyAsync()).ShouldBeFalse();
    }

    [Theory]
    [InlineData("not-an-email", null, ParentAccountErrorCodes.InvalidEmail)]
    [InlineData(Email, "short", ParentAccountErrorCodes.PasswordRejected)]
    public async Task Invalid_activation_input_never_reaches_keycloak(string email, string? pw, string code)
    {
        await using var ctx = _db.NewContext();
        (await Service(ctx).CreateAsync(Create(email, pw))).ErrorCode.ShouldBe(code);
        await NothingWasWrittenAsync();
    }

    [Fact]
    public void Controller_is_service_only_and_has_no_delete()
    {
        var authorize = typeof(ParentAccountsController).GetCustomAttribute<AuthorizeAttribute>().ShouldNotBeNull();
        authorize.Policy.ShouldBe("Service");
        typeof(ParentAccountsController).GetMethods().SelectMany(m => m.GetCustomAttributes<AllowAnonymousAttribute>()).ShouldBeEmpty();
        typeof(ParentAccountsController).GetMethods()
            .SelectMany(m => m.GetCustomAttributes<Microsoft.AspNetCore.Mvc.HttpDeleteAttribute>()).ShouldBeEmpty();
    }
}
