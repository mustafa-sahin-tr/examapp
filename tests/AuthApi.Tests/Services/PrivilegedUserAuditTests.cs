using System.Text.Json;
using AuthApi.Tests.Support;
using ExamApp.Api.Commands;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Audit;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Cls = ExamApp.Api.Models.Audit.PrivilegedAccountClass;
using Ev = ExamApp.Api.Services.PrivilegedUserAuditService.ServiceAccountEvidence;
using Svc = ExamApp.Api.Services.PrivilegedUserAuditService;

namespace AuthApi.Tests.Services;

/// <summary>
/// Yetkili hesap denetimi (issue #267): sınıflandırma kuralları (addan türeyen işaretler meşruiyet sayılmaz, KNOWN yalnızca
/// id ile, servis hesabı fail-closed, yetkili roldeki seed hesabı UNEXPLAINED), dolaylı yetki (grup/kompozit), e-posta
/// maskeleme, identity eşleştirmesi (soft-delete dahil), rol özeti, komutun çıkış kodu/biçimleri ve DI kabı.
/// </summary>
public class PrivilegedUserAuditTests
{
    private static readonly IReadOnlySet<string> NoKnown = new HashSet<string>();
    private static readonly Ev CleanServiceAccount = new(HasCredentials: false, HasFederatedIdentity: false);

    private static KeycloakRoleMember Member(string id, string username, string? email = null, string? saClient = null, bool enabled = true)
        => new(id, username, email, enabled, DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000), saClient);

    private static Svc.IdentityInfo Identity(bool seed = false, string role = "Student", bool deleted = false, int rows = 1)
        => new(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), deleted, role, seed, rows);

    // ---- Sınıflandırma: servis hesabı ----

    [Fact]
    public void Service_account_with_client_id_from_keycloak_is_accepted()
        => Svc.Classify(Member("sa", "service-account-exam-admin", saClient: "exam-admin"), null, NoKnown)
            .ShouldBe((Cls.ServiceAccount, "client=exam-admin"));

    [Fact]
    public void Service_account_without_client_id_needs_no_email_no_credentials_no_federated_identity()
    {
        // Keycloak 26 temsilde serviceAccountClientId döndürmüyor.
        Svc.Classify(Member("sa", "service-account-exam-admin"), null, NoKnown, CleanServiceAccount)
            .ShouldBe((Cls.ServiceAccount, "client=exam-admin (addan; e-posta, kimlik bilgisi ve federated identity yok)"));

        // Parolası var → taklit (register açığıyla #240 önekli ad alınabilirdi).
        var (cls, note) = Svc.Classify(Member("sa2", "service-account-x"), Identity(role: "Admin"), NoKnown, new Ev(true, false));
        cls.ShouldBe(Cls.Unexplained);
        note!.ShouldContain("kimlik bilgisi (parola/OTP) taşıyor");
        note!.ShouldContain("identity rolü=Admin");

        // E-postası var (register daima e-posta yazar) → taklit.
        Svc.Classify(Member("sa3", "service-account-x", "x@evil.com"), null, NoKnown, CleanServiceAccount).Class.ShouldBe(Cls.Unexplained);
    }

    [Fact]
    public void Service_account_with_federated_identity_is_not_accepted()
    {
        var (cls, note) = Svc.Classify(Member("sa", "service-account-x"), null, NoKnown, new Ev(false, true));
        cls.ShouldBe(Cls.Unexplained);
        note!.ShouldContain("federated identity bağlı");
    }

    [Fact]
    public void Service_account_evidence_unreadable_fails_closed()
    {
        // Kimlik bilgisi ucu 404/hata → null → doğrulanamadı.
        Svc.Classify(Member("sa", "service-account-x"), null, NoKnown, new Ev(null, false)).Note!.ShouldContain("doğrulanamadı");
        Svc.Classify(Member("sa", "service-account-x"), null, NoKnown, new Ev(false, null)).Class.ShouldBe(Cls.Unexplained);
        Svc.Classify(Member("sa", "service-account-x"), null, NoKnown).Class.ShouldBe(Cls.Unexplained); // hiç kontrol yok
    }

    // ---- Sınıflandırma: seed / known ----

    [Fact]
    public void Seed_account_in_privileged_role_is_unexplained()
    {
        const string seedName = "seed.t.123.mat.1@seed.examapp.local";

        var (cls, note) = Svc.Classify(Member("s1", seedName, seedName), Identity(seed: true, role: "Teacher"), NoKnown);
        cls.ShouldBe(Cls.Unexplained);
        note!.ShouldContain("SEED hesabı, rol sonradan eklenmiş");

        // Seed desenli ad ama identity satırı seed değil.
        Svc.Classify(Member("s2", seedName, seedName), Identity(seed: false), NoKnown).Note!.ShouldContain("IsSeedData=true satırı yok");
        // Yetim.
        Svc.Classify(Member("s3", seedName, seedName), null, NoKnown).Class.ShouldBe(Cls.Unexplained);
    }

    [Fact]
    public void Known_matches_only_keycloak_id_exactly_never_username()
    {
        var known = new HashSet<string>(["kc-known-id", "ops.admin@example.com"], StringComparer.Ordinal);

        Svc.Classify(Member("kc-known-id", "someone"), null, known).Class.ShouldBe(Cls.Known);
        // Kullanıcı adı listede ama id farklı → KNOWN DEĞİL.
        Svc.Classify(Member("x1", "ops.admin@example.com"), null, known).ShouldBe((Cls.Unexplained, "identity kaydı yok"));
        // id büyük/küçük harf farkı → eşleşmez (tam eşleşme).
        Svc.Classify(Member("KC-KNOWN-ID", "someone"), null, known).Class.ShouldBe(Cls.Unexplained);
    }

    [Fact]
    public void Service_account_wins_over_known_and_multiple_identity_rows_are_noted()
    {
        var known = new HashSet<string>(["sa"], StringComparer.Ordinal);
        Svc.Classify(Member("sa", "service-account-exam-admin", saClient: "exam-admin"), null, known).Class.ShouldBe(Cls.ServiceAccount);

        Svc.Classify(Member("u", "u@example.com"), Identity(rows: 2, role: "Admin"), NoKnown).Note
            .ShouldBe("identity'de 2 satır; identity rolü=Admin");
    }

    // ---- Maskeleme ----

    [Theory]
    [InlineData("alice@example.com", "a***@e***.com")]
    [InlineData("a@d.com", "a***@d***.com")]
    [InlineData("john.doe@mail.example.co.uk", "j***@m***.uk")]
    [InlineData("user@localhost", "u***@l***")]
    [InlineData("noatsign", "n***")]
    [InlineData("x@", "x***@***")]
    [InlineData(null, "-")]
    [InlineData("  ", "-")]
    public void Mask_email(string? input, string expected) => Svc.MaskEmail(input).ShouldBe(expected);

    [Fact]
    public void Mask_username_keeps_only_verified_service_account_names()
    {
        Svc.MaskUsername("service-account-exam-admin", verifiedServiceAccount: true).ShouldBe("service-account-exam-admin");
        Svc.MaskUsername("service-account-exam-admin").ShouldBe("s***");
        Svc.MaskUsername("alice@example.com").ShouldBe("a***@e***.com");
        Svc.MaskUsername("admin").ShouldBe("a***");
    }

    // ---- Servis: Keycloak + identity DB ----

    private static IKeycloakService FakeKeycloak(
        Dictionary<string, KeycloakRoleMember[]> membersByRole,
        Dictionary<string, KeycloakGroupRef[]>? groupsByRole = null,
        Dictionary<string, KeycloakRoleMember[]>? membersByGroup = null,
        Dictionary<string, KeycloakGroupRef[]>? childrenByGroup = null,
        Dictionary<string, IReadOnlyList<string>>? composites = null)
    {
        var keycloak = Substitute.For<IKeycloakService>();
        keycloak.GetUsersInRoleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => (IReadOnlyList<KeycloakRoleMember>)(membersByRole.TryGetValue(ci.Arg<string>(), out var m) ? m : []));
        keycloak.GetGroupsInRoleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => (IReadOnlyList<KeycloakGroupRef>)(groupsByRole?.TryGetValue(ci.Arg<string>(), out var g) == true ? g : []));
        keycloak.GetGroupMembersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => (IReadOnlyList<KeycloakRoleMember>)(membersByGroup?.TryGetValue(ci.Arg<string>(), out var m) == true ? m : []));
        keycloak.GetSubGroupsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => (IReadOnlyList<KeycloakGroupRef>)(childrenByGroup?.TryGetValue(ci.Arg<string>(), out var c) == true ? c : []));
        keycloak.GetRealmRoleCompositesAsync(Arg.Any<CancellationToken>())
            .Returns(composites ?? new Dictionary<string, IReadOnlyList<string>>());
        // Kanıt: "fake" parolalı, "gone" credentials ucu 404, "fed" federated bağlı; diğerleri temiz.
        keycloak.GetUserCredentialTypesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<string>() switch
            {
                "kc-sa-fake" => (IReadOnlyList<string>)["password"],
                "kc-sa-gone" => throw new KeycloakException("Failed to read credentials (404)", 404),
                _ => (IReadOnlyList<string>)[]
            });
        keycloak.GetUserFederatedIdentityProvidersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => (IReadOnlyList<string>)(ci.Arg<string>() == "kc-sa-fed" ? ["google"] : []));
        return keycloak;
    }

    private static async Task<PrivilegedAuditReport> RunAuditAsync(TestDb db, IKeycloakService keycloak, params string[] knownAccounts)
    {
        await using var ctx = db.NewContext();
        var service = new Svc(keycloak, ctx, Options.Create(new PrivilegedAuditSettings { KnownAccounts = [.. knownAccounts] }));
        return await service.AuditAsync(Svc.DefaultRoles);
    }

    private static async Task SeedIdentityAsync(TestDb db, params User[] users)
    {
        await using var ctx = db.NewContext();
        ctx.Users.AddRange(users);
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Audit_joins_identity_including_soft_deleted_and_summarises_per_role()
    {
        using var db = TestDb.Create();
        const string seedName = "seed.t.1.mat.1@seed.examapp.local";
        await SeedIdentityAsync(db,
            new User { KeycloakId = "kc-seed", FullName = "S", Email = seedName, Role = "Teacher", IsSeedData = true },
            new User { KeycloakId = "kc-attacker", FullName = "A", Email = "evil@example.com", Role = "Admin", IsDeleted = true });

        var report = await RunAuditAsync(db, FakeKeycloak(new()
        {
            ["Admin"] =
            [
                Member("kc-seed", seedName, seedName),
                Member("kc-attacker", "evil@example.com", "evil@example.com"),
                Member("kc-ops", "ops@example.com", "ops@example.com")
            ],
            ["exam-service"] =
            [
                Member("kc-sa", "service-account-exam-admin", saClient: "exam-admin"),
                Member("kc-sa-nolink", "service-account-other"),
                Member("kc-sa-fake", "service-account-fake"),
                Member("kc-sa-gone", "service-account-gone"),
                Member("kc-sa-fed", "service-account-fed")
            ]
        }), "kc-ops");

        report.Summary.ShouldBe(
        [
            new PrivilegedRoleSummary("Admin", 3, 0, 1, 2),
            new PrivilegedRoleSummary("exam-service", 5, 2, 0, 3)
        ]);
        report.HasUnexplained.ShouldBeTrue();
        report.WarningList.ShouldBeEmpty();

        var attacker = report.Rows.Single(r => r.KeycloakId == "kc-attacker");
        attacker.Class.ShouldBe(Cls.Unexplained);
        attacker.Source.ShouldBe("direct");
        attacker.IdentityIsDeleted.ShouldBe(true); // soft-delete edilmiş satır da görünür
        attacker.IdentityRole.ShouldBe("Admin");
        attacker.EmailMasked.ShouldBe("e***@e***.com");
        report.Rows.First(r => r.Role == "Admin").Class.ShouldBe(Cls.Unexplained); // UNEXPLAINED önce

        report.Rows.Single(r => r.KeycloakId == "kc-ops").Class.ShouldBe(Cls.Known);
        report.Rows.Single(r => r.KeycloakId == "kc-seed").Class.ShouldBe(Cls.Unexplained);
        report.Rows.Single(r => r.KeycloakId == "kc-sa").UsernameMasked.ShouldBe("service-account-exam-admin");
        report.Rows.Single(r => r.KeycloakId == "kc-sa-nolink").Class.ShouldBe(Cls.ServiceAccount);
        report.Rows.Single(r => r.KeycloakId == "kc-sa-gone").Class.ShouldBe(Cls.Unexplained); // credentials 404 → fail-closed
        report.Rows.Single(r => r.KeycloakId == "kc-sa-fed").Class.ShouldBe(Cls.Unexplained);
        var fake = report.Rows.Single(r => r.KeycloakId == "kc-sa-fake");
        fake.Class.ShouldBe(Cls.Unexplained);
        fake.UsernameMasked.ShouldBe("s***"); // doğrulanmamış önekli ad maskelenir
    }

    [Fact]
    public async Task Username_listed_as_known_but_different_id_stays_unexplained()
    {
        using var db = TestDb.Create();
        var report = await RunAuditAsync(db, FakeKeycloak(new() { ["Admin"] = [Member("kc-imposter", "ops@example.com", "ops@example.com")] }),
            "ops@example.com", "kc-real-ops");

        report.Rows.Single().Class.ShouldBe(Cls.Unexplained);
        PrivilegedUserAuditCommand.ExitCodeFor(report).ShouldBe(2);
    }

    [Fact]
    public async Task Seed_account_with_admin_role_gives_exit_2()
    {
        using var db = TestDb.Create();
        const string seedName = "seed.t.9.fiz.1@seed.examapp.local";
        await SeedIdentityAsync(db, new User { KeycloakId = "kc-seed", FullName = "S", Email = seedName, Role = "Teacher", IsSeedData = true });

        var report = await RunAuditAsync(db, FakeKeycloak(new() { ["Admin"] = [Member("kc-seed", seedName, seedName)] }));

        report.Rows.Single().Note!.ShouldContain("SEED hesabı, rol sonradan eklenmiş");
        PrivilegedUserAuditCommand.ExitCodeFor(report).ShouldBe(PrivilegedUserAuditCommand.ExitUnexplained);
    }

    [Fact]
    public async Task Group_assignment_is_a_finding_and_group_and_subgroup_members_are_audited()
    {
        using var db = TestDb.Create();
        var keycloak = FakeKeycloak(
            new() { ["Admin"] = [Member("kc-direct", "d@example.com", "d@example.com")] },
            groupsByRole: new() { ["Admin"] = [new KeycloakGroupRef("g1", "ops", "/ops")] },
            membersByGroup: new()
            {
                ["g1"] = [Member("kc-direct", "d@example.com", "d@example.com"), Member("kc-g1", "g1@example.com", "g1@example.com")],
                ["g2"] = [Member("kc-g2", "g2@example.com", "g2@example.com")]
            },
            childrenByGroup: new() { ["g1"] = [new KeycloakGroupRef("g2", "night", "/ops/night")] });

        // Tüm üyeler KNOWN olsa bile grup ataması tek başına bulgu (exit 2).
        var report = await RunAuditAsync(db, keycloak, "kc-direct", "kc-g1", "kc-g2");

        report.HasUnexplained.ShouldBeFalse();
        report.WarningList.Count.ShouldBe(2);
        report.WarningList[0].ShouldContain("'/ops' grubuna atanmış");
        report.WarningList[1].ShouldContain("'/ops/night' alt grubuna miras");
        report.Rows.Select(r => (r.KeycloakId, r.Source)).ShouldBe(
            [("kc-direct", "direct"), ("kc-g1", "group:/ops"), ("kc-g2", "group:/ops/night")], ignoreOrder: true);
        PrivilegedUserAuditCommand.ExitCodeFor(report).ShouldBe(2);
        PrivilegedUserAuditCommand.FormatSummary(report).ShouldContain("UYARI: 'Admin' rolü '/ops' grubuna atanmış");
    }

    [Fact]
    public async Task Composite_role_containing_privileged_role_is_a_finding_including_transitive_and_default_roles()
    {
        using var db = TestDb.Create();
        var keycloak = FakeKeycloak(new(), composites: new Dictionary<string, IReadOnlyList<string>>
        {
            ["default-roles-exam-realm"] = ["offline_access", "helper"],
            ["helper"] = ["exam-service"],
            ["Admin"] = ["Teacher"],             // denetlenen rolün kendi kompozitleri uyarı değil
            ["unrelated"] = ["Student"]
        });

        var report = await RunAuditAsync(db, keycloak);

        report.WarningList.Count.ShouldBe(2);
        report.WarningList.ShouldContain(w => w.StartsWith("'default-roles-exam-realm' kompozit rolü 'exam-service'") && w.Contains("TÜM kullanıcılar"));
        report.WarningList.ShouldContain(w => w.StartsWith("'helper' kompozit rolü 'exam-service'"));
        PrivilegedUserAuditCommand.ExitCodeFor(report).ShouldBe(2);
    }

    [Fact]
    public async Task Unreadable_indirect_checks_propagate_and_command_exits_3()
    {
        using var db = TestDb.Create();
        var keycloak = FakeKeycloak(new());
        keycloak.GetRealmRoleCompositesAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyDictionary<string, IReadOnlyList<string>>>(_ => throw new KeycloakException("Failed to list realm roles (403): no"));
        await using var ctx = db.NewContext();
        var service = new Svc(keycloak, ctx, Options.Create(new PrivilegedAuditSettings()));

        var code = await PrivilegedUserAuditCommand.RunAsync(service, new(PrivilegedUserAuditCommand.OutputFormat.Table, false), new StringWriter(), new StringWriter());

        code.ShouldBe(PrivilegedUserAuditCommand.ExitFailed);
    }

    [Fact]
    public async Task Audit_with_no_members_has_no_findings()
    {
        using var db = TestDb.Create();
        var report = await RunAuditAsync(db, FakeKeycloak(new()));

        report.Rows.ShouldBeEmpty();
        report.Summary.Select(s => (s.Role, s.Total)).ShouldBe([("Admin", 0), ("exam-service", 0)]);
        report.HasFindings.ShouldBeFalse();
    }

    [Fact]
    public async Task Missing_role_is_reported_as_empty_not_as_failure()
    {
        using var db = TestDb.Create();
        var keycloak = FakeKeycloak(new() { ["Admin"] = [Member("kc-1", "a@example.com", "a@example.com")] });
        keycloak.GetUsersInRoleAsync("exam-service", Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<KeycloakRoleMember>>(_ => throw new KeycloakRoleNotFoundException("exam-service"));

        var report = await RunAuditAsync(db, keycloak);

        report.Summary.Single(s => s.Role == "exam-service").ShouldBe(new PrivilegedRoleSummary("exam-service", 0, 0, 0, 0, RoleMissing: true));
        report.Summary.Single(s => s.Role == "Admin").RoleMissing.ShouldBeFalse();
        await keycloak.DidNotReceive().GetGroupsInRoleAsync("exam-service", Arg.Any<CancellationToken>());
        PrivilegedUserAuditCommand.FormatSummary(report).ShouldContain("exam-service: toplam=0 SERVICE_ACCOUNT=0 KNOWN=0 UNEXPLAINED=0 (rol realm'de tanımlı değil)");
    }

    // ---- Komut ----

    [Theory]
    [InlineData(new[] { "audit-privileged-users" }, PrivilegedUserAuditCommand.OutputFormat.Table)]
    [InlineData(new[] { "audit-privileged-users", "--format", "csv" }, PrivilegedUserAuditCommand.OutputFormat.Csv)]
    [InlineData(new[] { "audit-privileged-users", "--format=JSON" }, PrivilegedUserAuditCommand.OutputFormat.Json)]
    public void Parse_formats(string[] args, PrivilegedUserAuditCommand.OutputFormat expected)
        => PrivilegedUserAuditCommand.Parse(args).Format.ShouldBe(expected);

    [Fact]
    public void Parse_rejects_bad_usage_and_supports_help()
    {
        Should.Throw<ArgumentException>(() => PrivilegedUserAuditCommand.Parse(["audit-privileged-users", "--format", "xml"]));
        Should.Throw<ArgumentException>(() => PrivilegedUserAuditCommand.Parse(["audit-privileged-users", "--format"]));
        Should.Throw<ArgumentException>(() => PrivilegedUserAuditCommand.Parse(["audit-privileged-users", "--fix"]));
        PrivilegedUserAuditCommand.Parse(["audit-privileged-users", "--help"]).ShowHelp.ShouldBeTrue();
        PrivilegedUserAuditCommand.IsRequested(["seed-users"]).ShouldBeFalse();
        PrivilegedUserAuditCommand.IsRequested([]).ShouldBeFalse();
    }

    [Fact]
    public void Separate_command_container_resolves_the_audit_service()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Keycloak:Host"] = "http://keycloak.test",
            ["Keycloak:AdminClientSecret"] = "example-secret-value",
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=identity;Username=x;Password=y",
            ["PrivilegedAudit:KnownAccounts:0"] = "kc-1"
        }).Build();

        using var provider = PrivilegedUserAuditCommand.BuildServices(config); // ValidateOnBuild: eksik kayıt burada patlar
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IPrivilegedUserAuditService>().ShouldBeOfType<Svc>();
        scope.ServiceProvider.GetRequiredService<IOptions<PrivilegedAuditSettings>>().Value.KnownAccounts.ShouldBe(["kc-1"]);
    }

    private static PrivilegedAccountRow Row(string role, Cls cls, string id = "kc-1", string email = "a***@e***.com", string? note = null)
        => new(role, id, email, email, true, DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000),
            false, null, null, null, null, cls, note);

    private static IPrivilegedUserAuditService AuditReturning(IReadOnlyList<string>? warnings, params PrivilegedAccountRow[] rows)
    {
        var summary = rows.GroupBy(r => r.Role).Select(g => new PrivilegedRoleSummary(g.Key, g.Count(),
            g.Count(r => r.Class == Cls.ServiceAccount), g.Count(r => r.Class == Cls.Known), g.Count(r => r.Class == Cls.Unexplained))).ToList();
        var audit = Substitute.For<IPrivilegedUserAuditService>();
        audit.AuditAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new PrivilegedAuditReport(DateTimeOffset.UnixEpoch, rows, summary, warnings));
        return audit;
    }

    private static IPrivilegedUserAuditService AuditReturning(params PrivilegedAccountRow[] rows) => AuditReturning(null, rows);

    [Fact]
    public async Task Exit_code_is_2_when_any_account_is_unexplained_or_a_warning_exists_and_0_otherwise()
    {
        var stdout = new StringWriter();
        var code = await PrivilegedUserAuditCommand.RunAsync(
            AuditReturning(Row("Admin", Cls.Known), Row("Admin", Cls.Unexplained, id: "kc-bad")),
            new(PrivilegedUserAuditCommand.OutputFormat.Table, false), stdout, new StringWriter());

        code.ShouldBe(2);
        stdout.ToString().ShouldContain("Admin: toplam=2 SERVICE_ACCOUNT=0 KNOWN=1 UNEXPLAINED=1");
        stdout.ToString().ShouldContain("sub listesi: kc-bad");

        var warned = await PrivilegedUserAuditCommand.RunAsync(
            AuditReturning(["'Admin' rolü '/ops' grubuna atanmış"], Row("Admin", Cls.Known)),
            new(PrivilegedUserAuditCommand.OutputFormat.Table, false), new StringWriter(), new StringWriter());
        warned.ShouldBe(2);

        var ok = await PrivilegedUserAuditCommand.RunAsync(
            AuditReturning(Row("exam-service", Cls.ServiceAccount)),
            new(PrivilegedUserAuditCommand.OutputFormat.Table, false), new StringWriter(), new StringWriter());
        ok.ShouldBe(PrivilegedUserAuditCommand.ExitOk);
    }

    [Fact]
    public async Task Failure_returns_3_and_writes_only_to_stderr()
    {
        var audit = Substitute.For<IPrivilegedUserAuditService>();
        audit.AuditAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns<PrivilegedAuditReport>(_ => throw new KeycloakException("boom\u001b[31m"));
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var code = await PrivilegedUserAuditCommand.RunAsync(audit, new(PrivilegedUserAuditCommand.OutputFormat.Json, false), stdout, stderr);

        code.ShouldBe(PrivilegedUserAuditCommand.ExitFailed);
        stdout.ToString().ShouldBeEmpty();
        stderr.ToString().ShouldContain("KeycloakException: boom?[31m"); // ESC temizlendi
    }

    [Fact]
    public async Task Csv_goes_to_stdout_with_summary_on_stderr_and_neutralises_injection()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        await PrivilegedUserAuditCommand.RunAsync(
            AuditReturning(
                Row("Admin", Cls.Unexplained, note: "identity'de 2 satır; x, \"y\""),
                Row("Admin", Cls.Known, id: "=cmd"),
                Row("Admin", Cls.Known, id: "\tcmd"),
                Row("Admin", Cls.Known, id: "\rcmd")),
            new(PrivilegedUserAuditCommand.OutputFormat.Csv, false), stdout, stderr);

        var lines = stdout.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines[0].ShouldStartWith("role,class,source,keycloakId,username,email,enabled");
        lines.Length.ShouldBe(5);
        lines[1].ShouldEndWith("\"identity'de 2 satır; x, \"\"y\"\"\"");
        lines[2].ShouldContain(",'=cmd,");
        // \t ve \r önce "?"'e temizlenir (kontrol karakteri) — hücre asla ham tab/CR ile başlamaz.
        lines[3].ShouldContain(",?cmd,");
        lines[4].ShouldContain(",?cmd,");
        stdout.ToString().ShouldNotContain("Özet");
        stderr.ToString().ShouldContain("UNEXPLAINED=1");
    }

    [Fact]
    public void Control_characters_in_raw_values_are_cleaned_in_table_output()
    {
        var report = new PrivilegedAuditReport(DateTimeOffset.UnixEpoch,
            [Row("Admin", Cls.Unexplained, id: "kc-1\nFAKE ROW", note: "x\u001b[2Jy")],
            [new PrivilegedRoleSummary("Admin", 1, 0, 0, 1)],
            ["grup '/a\nb'"]);

        var table = PrivilegedUserAuditCommand.FormatTable(report);
        table.ShouldContain("kc-1?FAKE ROW");
        table.ShouldContain("x?[2Jy");
        table.ShouldNotContain("\u001b");
        table.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length.ShouldBe(3); // başlık + çizgi + 1 satır
        PrivilegedUserAuditCommand.FormatSummary(report).ShouldContain("grup '/a?b'");
        PrivilegedUserAuditCommand.Clean("a\tb\rc").ShouldBe("a?b?c");
    }

    [Fact]
    public async Task Json_contains_summary_warnings_and_masked_accounts()
    {
        var stdout = new StringWriter();
        var code = await PrivilegedUserAuditCommand.RunAsync(
            AuditReturning(Row("Admin", Cls.Known)),
            new(PrivilegedUserAuditCommand.OutputFormat.Json, false), stdout, new StringWriter());

        code.ShouldBe(0);
        using var doc = JsonDocument.Parse(stdout.ToString());
        doc.RootElement.GetProperty("hasFindings").GetBoolean().ShouldBeFalse();
        doc.RootElement.GetProperty("warnings").GetArrayLength().ShouldBe(0);
        doc.RootElement.GetProperty("summary")[0].GetProperty("known").GetInt32().ShouldBe(1);
        var account = doc.RootElement.GetProperty("accounts")[0];
        account.GetProperty("class").GetString().ShouldBe("KNOWN");
        account.GetProperty("source").GetString().ShouldBe("direct");
        account.GetProperty("email").GetString().ShouldBe("a***@e***.com");
    }
}
