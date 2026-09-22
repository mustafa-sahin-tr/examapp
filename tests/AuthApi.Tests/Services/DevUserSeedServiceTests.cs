using System.Text.Json;
using AuthApi.Tests.Support;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using ExamApp.Foundation.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace AuthApi.Tests.Services;

/// <summary>
/// Dev-only toplu kullanıcı oluşturma (issue #217): yalnızca seed alanı e-postaları, Keycloak (substitute) +
/// identity User upsert, outbox event'i, idempotency, kısmi durum, yabancı hesap koruması, soft-delete geri
/// açma, ortam guard'ı, partial import eşlemesi.
/// </summary>
public class DevUserSeedServiceTests : IDisposable
{
    private const string Pw = "seed-pw-example"; // example credential, test-only
    private const string DefaultRole = "default-roles-exam-realm";

    private static string Seed(string local) => $"seed.t.{local}@{SeedDataConventions.EmailDomain}";

    private readonly TestDb _db = TestDb.Create();

    private static IHostEnvironment Env(string name)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(name);
        return env;
    }

    private DevUserSeedService NewService(AppDbContext ctx, IKeycloakService keycloak, string environment = "Development")
        => new(ctx, keycloak, Env(environment), NullLogger<DevUserSeedService>.Instance);

    private static DevSeedUsersRequest Request(params string[] emails) => new()
    {
        Password = Pw,
        Role = "Teacher",
        Users = emails.Select(e => new DevSeedUserItem { Email = e, FirstName = "Ad", LastName = "Soyad", SchoolId = 42 }).ToList()
    };

    /// <summary>Keycloak taklidi: username → id; ilk create Created, sonra AlreadyExisted.</summary>
    private static IKeycloakService FakeKeycloak(Dictionary<string, string>? store = null)
    {
        store ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var kc = Substitute.For<IKeycloakService>();
        kc.GetRealmRoleAsync("Teacher", Arg.Any<CancellationToken>()).Returns(new KeycloakRoleDto { id = "role-teacher", name = "Teacher" });
        kc.GetRealmRoleAsync(DefaultRole, Arg.Any<CancellationToken>()).Returns(new KeycloakRoleDto { id = "role-default", name = DefaultRole });
        kc.GetRealmDefaultRoleNameAsync(Arg.Any<CancellationToken>()).Returns(DefaultRole);
        kc.CreateSeedUserAsync(Arg.Any<KeycloakSeedUser>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var user = call.Arg<KeycloakSeedUser>();
            if (store.TryGetValue(user.Username, out var existing))
                return new KeycloakUserCreateResult(existing, AlreadyExisted: true);
            var id = "kc-" + (store.Count + 1);
            store[user.Username] = id;
            return new KeycloakUserCreateResult(id, AlreadyExisted: false);
        });
        kc.GetUserRealmRoleNamesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new List<string> { DefaultRole, "Teacher" });
        return kc;
    }

    private async Task AddIdentityUserAsync(string email, string keycloakId, bool isSeed, bool deleted = false)
    {
        await using var ctx = _db.NewContext();
        var u = new User { Email = email, FullName = "Var Olan", Role = "Teacher", KeycloakId = keycloakId, IsSeedData = isSeed };
        ctx.Users.Add(u);
        await ctx.SaveChangesAsync();
        if (deleted)
        {
            ctx.Users.Remove(u); // ApplyAuditInfo → soft delete
            await ctx.SaveChangesAsync();
        }
    }

    // ---- guard'lar / doğrulama ----

    [Fact]
    public async Task Production_refuses_with_environment_exception_before_touching_keycloak()
    {
        var kc = FakeKeycloak();
        await using var ctx = _db.NewContext();

        var ex = await Should.ThrowAsync<DevSeedEnvironmentException>(() =>
            NewService(ctx, kc, "Production").SeedAsync(Request(Seed("a"))));

        ex.Message.ShouldContain("Production");
        await kc.DidNotReceiveWithAnyArgs().CreateSeedUserAsync(default!, default!, default);
        (await _db.NewContext().Users.CountAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData("ceo@x.com")]
    [InlineData("seed.t.1@seed.examapp.local.evil.com")]
    [InlineData("Seed.T.1@seed.examapp.local")]     // büyük harf kabul edilmez
    [InlineData("t.1@seed.examapp.local")]          // seed. öneki yok
    [InlineData("seed.a b@seed.examapp.local")]
    [InlineData("")]
    public async Task Non_seed_email_is_rejected_as_400_and_nothing_is_written(string email)
    {
        var kc = FakeKeycloak();
        await using var ctx = _db.NewContext();

        await Should.ThrowAsync<ArgumentException>(() => NewService(ctx, kc).SeedAsync(Request(Seed("ok"), email)));

        await kc.DidNotReceiveWithAnyArgs().CreateSeedUserAsync(default!, default!, default);
        await kc.DidNotReceiveWithAnyArgs().PartialImportUsersAsync(default!, default!, default!, default);
        await kc.DidNotReceiveWithAnyArgs().AddRealmRoleMappingAsync(default!, default!, default);
        (await _db.NewContext().Users.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Existing_real_user_email_with_teacher_role_is_rejected_and_no_role_is_added()
    {
        await AddIdentityUserAsync("ceo@x.com", "kc-ceo", isSeed: false);
        var kc = FakeKeycloak(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["ceo@x.com"] = "kc-ceo" });
        await using var ctx = _db.NewContext();

        await Should.ThrowAsync<ArgumentException>(() => NewService(ctx, kc).SeedAsync(Request("ceo@x.com")));

        await kc.DidNotReceiveWithAnyArgs().AddRealmRoleMappingAsync(default!, default!, default);
        await kc.DidNotReceiveWithAnyArgs().GetUserRealmRoleNamesAsync(default!, default);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    public async Task Short_or_missing_password_is_rejected(string value)
    {
        await using var ctx = _db.NewContext();
        var request = Request(Seed("a"));
        request.Password = value;

        await Should.ThrowAsync<ArgumentException>(() => NewService(ctx, FakeKeycloak()).SeedAsync(request));
    }

    [Fact]
    public async Task Duplicate_emails_bad_role_bad_school_id_and_too_many_users_are_rejected()
    {
        await using var ctx = _db.NewContext();
        var kc = FakeKeycloak();

        await Should.ThrowAsync<ArgumentException>(() => NewService(ctx, kc).SeedAsync(Request(Seed("a"), Seed("a"))));

        var badRole = Request(Seed("a"));
        badRole.Role = "Admin";
        await Should.ThrowAsync<ArgumentException>(() => NewService(ctx, kc).SeedAsync(badRole));

        var badSchool = Request(Seed("a"));
        badSchool.Users[0].SchoolId = 0;
        await Should.ThrowAsync<ArgumentException>(() => NewService(ctx, kc).SeedAsync(badSchool));

        var tooMany = Request(Enumerable.Range(1, DevSeedUsersRequest.MaxUsersPerRequest + 1).Select(i => Seed(i.ToString())).ToArray());
        var ex = await Should.ThrowAsync<ArgumentException>(() => NewService(ctx, kc).SeedAsync(tooMany));
        ex.Message.ShouldContain("500");

        await kc.DidNotReceiveWithAnyArgs().CreateSeedUserAsync(default!, default!, default);
    }

    // ---- oluşturma ----

    [Fact]
    public async Task Creates_keycloak_user_with_role_and_school_id_attribute_then_identity_user_and_outbox_event()
    {
        var kc = FakeKeycloak();
        await using var ctx = _db.NewContext();

        var response = await NewService(ctx, kc).SeedAsync(Request(Seed("t1"), Seed("t2")));

        response.Results.Count.ShouldBe(2);
        response.Results.ShouldAllBe(r => r.KeycloakStatus == DevSeedUsersResponse.StatusCreated && r.IdentityStatus == DevSeedUsersResponse.StatusCreated);
        response.Results.ShouldAllBe(r => r.UserId > 0 && r.KeycloakId!.StartsWith("kc-"));
        response.Mode.ShouldBe(DevSeedUsersRequest.ModeAdminApi);

        await kc.Received(2).CreateSeedUserAsync(
            Arg.Is<KeycloakSeedUser>(u => u.Attributes!.Count == 1 && u.Attributes["school_id"] == "42" && u.Username == u.Email),
            Pw, Arg.Any<CancellationToken>());
        await kc.Received(2).AddRealmRoleMappingAsync(Arg.Any<string>(), Arg.Is<KeycloakRoleDto>(r => r.name == "Teacher"), Arg.Any<CancellationToken>());

        await using var check = _db.NewContext();
        var users = await check.Users.OrderBy(u => u.Id).ToListAsync();
        users.Count.ShouldBe(2);
        users.ShouldAllBe(u => u.IsSeedData && u.Role == "Teacher" && u.FullName == "Ad Soyad" && u.PreferredLocale == "tr");
        users[0].KeycloakId.ShouldBe(response.Results[0].KeycloakId);

        var outbox = await check.OutboxMessages.ToListAsync();
        outbox.Count.ShouldBe(2);
        outbox.ShouldAllBe(m => m.Type == OutboxEventRegistry.NameFor<UserPreferredLocaleChangedEvent>());
        var events = outbox.Select(m => JsonSerializer.Deserialize<UserPreferredLocaleChangedEvent>(m.Content)!).ToList();
        events.Select(e => e.UserId).Order().ShouldBe(users.Select(u => u.Id));
        events.Single(e => e.UserId == users[0].Id).KeycloakId.ShouldBe(users[0].KeycloakId);
        outbox.ShouldAllBe(m => !m.Content.Contains(Pw));
    }

    [Fact]
    public async Task Null_school_id_sends_no_attributes()
    {
        var kc = FakeKeycloak();
        await using var ctx = _db.NewContext();
        var request = Request(Seed("t1"));
        request.Users[0].SchoolId = null;

        await NewService(ctx, kc).SeedAsync(request);

        await kc.Received(1).CreateSeedUserAsync(Arg.Is<KeycloakSeedUser>(u => u.Attributes == null), Pw, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_events_flag_skips_outbox()
    {
        await using var ctx = _db.NewContext();
        var request = Request(Seed("t1"));
        request.EmitLocaleEvents = false;

        await NewService(ctx, FakeKeycloak()).SeedAsync(request);

        (await _db.NewContext().OutboxMessages.CountAsync()).ShouldBe(0);
        (await _db.NewContext().Users.CountAsync()).ShouldBe(1);
    }

    // ---- idempotency / kısmi durum ----

    [Fact]
    public async Task Second_run_is_idempotent_and_reports_existing()
    {
        var store = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        DevSeedUsersResponse first, second;
        await using (var ctx = _db.NewContext())
            first = await NewService(ctx, FakeKeycloak(store)).SeedAsync(Request(Seed("t1"), Seed("t2")));
        await using (var ctx = _db.NewContext())
            second = await NewService(ctx, FakeKeycloak(store)).SeedAsync(Request(Seed("t1"), Seed("t2")));

        second.Results.ShouldAllBe(r => r.KeycloakStatus == DevSeedUsersResponse.StatusExisting && r.IdentityStatus == DevSeedUsersResponse.StatusExisting);
        second.Results.Select(r => r.UserId).ShouldBe(first.Results.Select(r => r.UserId));
        second.Results.Select(r => r.KeycloakId).ShouldBe(first.Results.Select(r => r.KeycloakId));

        await using var check = _db.NewContext();
        (await check.Users.CountAsync()).ShouldBe(2);
        (await check.OutboxMessages.CountAsync()).ShouldBe(2); // ikinci koşu event üretmez
    }

    [Fact]
    public async Task Existing_keycloak_seed_user_missing_roles_gets_teacher_and_default_role_repaired()
    {
        var email = Seed("t1");
        await AddIdentityUserAsync(email, "kc-old", isSeed: true);
        var kc = FakeKeycloak(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [email] = "kc-old" });
        kc.GetUserRealmRoleNamesAsync("kc-old", Arg.Any<CancellationToken>()).Returns(new List<string>());
        await using var ctx = _db.NewContext();

        var response = await NewService(ctx, kc).SeedAsync(Request(email));

        var r = response.Results.Single();
        r.KeycloakStatus.ShouldBe(DevSeedUsersResponse.StatusExisting);
        r.IdentityStatus.ShouldBe(DevSeedUsersResponse.StatusExisting);
        await kc.Received(1).AddRealmRoleMappingAsync("kc-old", Arg.Is<KeycloakRoleDto>(x => x.name == "Teacher"), Arg.Any<CancellationToken>());
        await kc.Received(1).AddRealmRoleMappingAsync("kc-old", Arg.Is<KeycloakRoleDto>(x => x.name == DefaultRole), Arg.Any<CancellationToken>());
        (await _db.NewContext().Users.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Existing_keycloak_user_without_seed_identity_row_is_skipped_as_foreign_and_untouched()
    {
        var email = Seed("t1");
        var kc = FakeKeycloak(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [email] = "kc-foreign" });
        kc.GetUserRealmRoleNamesAsync("kc-foreign", Arg.Any<CancellationToken>()).Returns(new List<string>());
        await using var ctx = _db.NewContext();

        var response = await NewService(ctx, kc).SeedAsync(Request(email, Seed("t2")));

        var foreign = response.Results.Single(r => r.Email == email);
        foreign.KeycloakStatus.ShouldBe(DevSeedUsersResponse.StatusSkippedForeign);
        foreign.IdentityStatus.ShouldBe(DevSeedUsersResponse.StatusSkippedForeign);
        foreign.UserId.ShouldBeNull();
        foreign.KeycloakId.ShouldBeNull();
        await kc.DidNotReceive().GetUserRealmRoleNamesAsync("kc-foreign", Arg.Any<CancellationToken>());
        await kc.DidNotReceive().AddRealmRoleMappingAsync("kc-foreign", Arg.Any<KeycloakRoleDto>(), Arg.Any<CancellationToken>());

        response.Results.Single(r => r.Email == Seed("t2")).IdentityStatus.ShouldBe(DevSeedUsersResponse.StatusCreated);
        (await _db.NewContext().Users.CountAsync()).ShouldBe(1); // yabancı için identity satırı açılmadı
    }

    [Fact]
    public async Task Existing_non_seed_identity_row_with_seed_email_is_not_modified()
    {
        var email = Seed("t1");
        await AddIdentityUserAsync(email, "kc-manual", isSeed: false);
        await using var ctx = _db.NewContext();

        var response = await NewService(ctx, FakeKeycloak()).SeedAsync(Request(email));

        response.Results.Single().IdentityStatus.ShouldBe(DevSeedUsersResponse.StatusSkippedForeign);
        response.Results.Single().UserId.ShouldBeNull();
        (await _db.NewContext().Users.SingleAsync()).KeycloakId.ShouldBe("kc-manual");
    }

    [Fact]
    public async Task Soft_deleted_seed_user_is_revived_instead_of_duplicated()
    {
        var email = Seed("t1");
        await AddIdentityUserAsync(email, "kc-old", isSeed: true, deleted: true);
        await using var ctx = _db.NewContext();

        var response = await NewService(ctx, FakeKeycloak()).SeedAsync(Request(email));

        var r = response.Results.Single();
        r.KeycloakStatus.ShouldBe(DevSeedUsersResponse.StatusCreated);
        r.IdentityStatus.ShouldBe(DevSeedUsersResponse.StatusExisting);

        await using var check = _db.NewContext();
        var users = await check.Users.IgnoreQueryFilters().ToListAsync();
        users.Count.ShouldBe(1);
        users[0].IsDeleted.ShouldBeFalse();
        users[0].DeleteTime.ShouldBeNull();
        users[0].KeycloakId.ShouldBe(r.KeycloakId);
        users[0].Id.ShouldBe(r.UserId!.Value);
        (await check.OutboxMessages.CountAsync()).ShouldBe(1); // geri açılan için BadgeService'e event
    }

    [Fact]
    public async Task Existing_identity_user_with_stale_keycloak_id_is_repaired()
    {
        var email = Seed("t1");
        await AddIdentityUserAsync(email, "kc-stale", isSeed: true);
        await using var ctx = _db.NewContext();

        var response = await NewService(ctx, FakeKeycloak()).SeedAsync(Request(email));

        var r = response.Results.Single();
        r.KeycloakStatus.ShouldBe(DevSeedUsersResponse.StatusCreated);
        r.IdentityStatus.ShouldBe(DevSeedUsersResponse.StatusExisting);
        (await _db.NewContext().Users.SingleAsync()).KeycloakId.ShouldBe(r.KeycloakId);
        (await _db.NewContext().OutboxMessages.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Keycloak_failure_for_one_user_does_not_block_the_others()
    {
        var kc = FakeKeycloak();
        kc.CreateSeedUserAsync(Arg.Is<KeycloakSeedUser>(u => u.Username == Seed("bad")), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<KeycloakUserCreateResult>(_ => throw new KeycloakException("boom"));
        await using var ctx = _db.NewContext();

        var response = await NewService(ctx, kc).SeedAsync(Request(Seed("ok"), Seed("bad")));

        var bad = response.Results.Single(r => r.Email == Seed("bad"));
        bad.KeycloakStatus.ShouldBe(DevSeedUsersResponse.StatusFailed);
        bad.IdentityStatus.ShouldBe(DevSeedUsersResponse.StatusFailed);
        bad.Error.ShouldContain("boom");
        bad.UserId.ShouldBeNull();

        response.Results.Single(r => r.Email == Seed("ok")).IdentityStatus.ShouldBe(DevSeedUsersResponse.StatusCreated);
        (await _db.NewContext().Users.CountAsync()).ShouldBe(1);
    }

    // ---- partial import ----

    private static IKeycloakService FakePartialImportKeycloak(KeycloakPartialImportResult result)
    {
        var kc = Substitute.For<IKeycloakService>();
        kc.GetRealmDefaultRoleNameAsync(Arg.Any<CancellationToken>()).Returns(DefaultRole);
        kc.GetRealmRoleAsync("Teacher", Arg.Any<CancellationToken>()).Returns(new KeycloakRoleDto { id = "role-teacher", name = "Teacher" });
        kc.GetRealmRoleAsync(DefaultRole, Arg.Any<CancellationToken>()).Returns(new KeycloakRoleDto { id = "role-default", name = DefaultRole });
        kc.PartialImportUsersAsync(Arg.Any<IReadOnlyList<KeycloakSeedUser>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<KeycloakHashedCredential>(), Arg.Any<CancellationToken>())
            .Returns(result);
        return kc;
    }

    private static KeycloakPartialImportResult ImportResult(params (string Email, string Action, string? Id)[] entries) =>
        new(entries.Count(e => e.Action == "ADDED"), entries.Count(e => e.Action == "SKIPPED"), 0,
            entries.ToDictionary(e => e.Email, e => new KeycloakPartialImportEntry(e.Action, e.Id), StringComparer.OrdinalIgnoreCase));

    [Fact]
    public async Task Partial_import_sends_default_role_and_prehashed_credential_and_repairs_skipped_seed_users_only()
    {
        var newEmail = Seed("new");
        var oldSeed = Seed("oldseed");
        var foreign = Seed("foreign");
        await AddIdentityUserAsync(oldSeed, "kc-oldseed", isSeed: true);

        var kc = FakePartialImportKeycloak(ImportResult((newEmail, "ADDED", "kc-new"), (oldSeed, "SKIPPED", null), (foreign, "SKIPPED", "kc-foreign")));
        kc.FindUserIdByUsernameAsync(oldSeed, Arg.Any<CancellationToken>()).Returns("kc-oldseed");
        kc.GetUserRealmRoleNamesAsync("kc-oldseed", Arg.Any<CancellationToken>()).Returns(new List<string> { "Teacher" });
        await using var ctx = _db.NewContext();
        var request = Request(newEmail, oldSeed, foreign);
        request.Mode = DevSeedUsersRequest.ModePartialImport;

        var response = await NewService(ctx, kc).SeedAsync(request);

        response.Mode.ShouldBe(DevSeedUsersRequest.ModePartialImport);
        response.Results.Single(r => r.Email == newEmail).KeycloakStatus.ShouldBe(DevSeedUsersResponse.StatusCreated);
        response.Results.Single(r => r.Email == newEmail).IdentityStatus.ShouldBe(DevSeedUsersResponse.StatusCreated);
        response.Results.Single(r => r.Email == oldSeed).KeycloakStatus.ShouldBe(DevSeedUsersResponse.StatusExisting);
        response.Results.Single(r => r.Email == oldSeed).KeycloakId.ShouldBe("kc-oldseed");
        response.Results.Single(r => r.Email == foreign).KeycloakStatus.ShouldBe(DevSeedUsersResponse.StatusSkippedForeign);
        response.Results.Single(r => r.Email == foreign).UserId.ShouldBeNull();

        await kc.Received(1).PartialImportUsersAsync(
            Arg.Is<IReadOnlyList<KeycloakSeedUser>>(l => l.Count == 3),
            Arg.Is<IReadOnlyList<string>>(roles => roles.Contains("Teacher") && roles.Contains(DefaultRole)),
            Arg.Is<KeycloakHashedCredential>(c => c.CredentialData.Contains("pbkdf2-sha512") && !c.SecretData.Contains(Pw)),
            Arg.Any<CancellationToken>());
        await kc.Received(1).AddRealmRoleMappingAsync("kc-oldseed", Arg.Is<KeycloakRoleDto>(r => r.name == DefaultRole), Arg.Any<CancellationToken>());
        await kc.DidNotReceive().AddRealmRoleMappingAsync("kc-oldseed", Arg.Is<KeycloakRoleDto>(r => r.name == "Teacher"), Arg.Any<CancellationToken>());
        await kc.DidNotReceive().GetUserRealmRoleNamesAsync("kc-foreign", Arg.Any<CancellationToken>());
        await kc.DidNotReceiveWithAnyArgs().CreateSeedUserAsync(default!, default!, default);
        (await _db.NewContext().Users.CountAsync()).ShouldBe(2); // new + oldseed; foreign yok
    }

    [Fact]
    public async Task Partial_import_keycloak_exception_marks_every_row_failed_and_writes_nothing()
    {
        var kc = Substitute.For<IKeycloakService>();
        kc.GetRealmDefaultRoleNameAsync(Arg.Any<CancellationToken>()).Returns(DefaultRole);
        kc.PartialImportUsersAsync(Arg.Any<IReadOnlyList<KeycloakSeedUser>>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<KeycloakHashedCredential>(), Arg.Any<CancellationToken>())
            .Returns<KeycloakPartialImportResult>(_ => throw new KeycloakException("403 manage-realm"));
        await using var ctx = _db.NewContext();
        var request = Request(Seed("a"), Seed("b"));
        request.Mode = DevSeedUsersRequest.ModePartialImport;

        var response = await NewService(ctx, kc).SeedAsync(request);

        response.Results.ShouldAllBe(r => r.KeycloakStatus == DevSeedUsersResponse.StatusFailed && r.IdentityStatus == DevSeedUsersResponse.StatusFailed);
        response.Results.ShouldAllBe(r => r.Error!.Contains("manage-realm") && r.UserId == null);
        (await _db.NewContext().Users.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Partial_import_added_without_id_falls_back_to_username_lookup_and_missing_row_is_failed()
    {
        var a = Seed("a");
        var b = Seed("b");
        var kc = FakePartialImportKeycloak(ImportResult((a, "ADDED", null)));
        kc.FindUserIdByUsernameAsync(a, Arg.Any<CancellationToken>()).Returns("kc-a");
        await using var ctx = _db.NewContext();
        var request = Request(a, b);
        request.Mode = DevSeedUsersRequest.ModePartialImport;

        var response = await NewService(ctx, kc).SeedAsync(request);

        var ra = response.Results.Single(r => r.Email == a);
        ra.KeycloakStatus.ShouldBe(DevSeedUsersResponse.StatusCreated);
        ra.KeycloakId.ShouldBe("kc-a");
        ra.IdentityStatus.ShouldBe(DevSeedUsersResponse.StatusCreated);

        var rb = response.Results.Single(r => r.Email == b);
        rb.KeycloakStatus.ShouldBe(DevSeedUsersResponse.StatusFailed);
        rb.Error.ShouldContain("içermiyor");
        (await _db.NewContext().Users.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public void Password_hasher_produces_keycloak_shaped_json_with_512bit_key()
    {
        var cred = KeycloakPasswordHasher.HashPbkdf2Sha512("example-pw", iterations: 1000);

        using var sd = JsonDocument.Parse(cred.SecretData);
        Convert.FromBase64String(sd.RootElement.GetProperty("value").GetString()!).Length.ShouldBe(64);
        Convert.FromBase64String(sd.RootElement.GetProperty("salt").GetString()!).Length.ShouldBe(16);

        using var cd = JsonDocument.Parse(cred.CredentialData);
        cd.RootElement.GetProperty("algorithm").GetString().ShouldBe("pbkdf2-sha512");
        cd.RootElement.GetProperty("hashIterations").GetInt32().ShouldBe(1000);

        // Tuz rastgele: iki çağrı farklı hash üretir.
        KeycloakPasswordHasher.HashPbkdf2Sha512("example-pw", 1000).SecretData.ShouldNotBe(cred.SecretData);
    }

    [Fact]
    public void Seed_email_convention_is_shared_and_locked()
    {
        SeedDataConventions.EmailDomain.ShouldBe("seed.examapp.local");
        SeedDataConventions.IsSeedEmail("seed.t.713965.turkce.1@seed.examapp.local").ShouldBeTrue();
        SeedDataConventions.IsSeedEmail("seed.i.34.matematik.12@seed.examapp.local").ShouldBeTrue();
        SeedDataConventions.IsSeedEmail("ceo@x.com").ShouldBeFalse();
        SeedDataConventions.IsSeedEmail("seed.t.1@seed.examapp.local ").ShouldBeFalse();
        SeedDataConventions.IsSeedEmail(null).ShouldBeFalse();
    }

    public void Dispose() => _db.Dispose();
}
