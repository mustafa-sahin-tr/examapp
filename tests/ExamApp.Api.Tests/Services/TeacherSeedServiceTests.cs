using ExamApp.Api.Data;
using ExamApp.Api.Services.Schools.Seed;
using ExamApp.Api.Services.Teachers.Seed;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// seed-teachers (issue #217): branş dağılımı okul türüne göre, deterministik e-posta/ad, idempotency,
/// kısmi durum tamamlama, dry-run, parola config guard'ı, ortam guard'ı, il başına okul limiti.
/// auth-api (Keycloak + identity) <see cref="IAuthApiSeedClient"/> substitute'u ile taklit edilir.
/// </summary>
public class TeacherSeedServiceTests : IDisposable
{
    private const string TestSeedPassword = "seed-pw-example"; // example credential, test-only

    private readonly TestDb _db = TestDb.Create();

    private int _karsId;
    private int _ilkokulId;
    private int _ortaokulId;

    /// <summary>Kars: 1 ilkokul + 1 ortaokul (seed), 1 türü bilinmeyen seed okul, 1 elle açılmış (seed olmayan) ortaokul. Antalya: 3 ilkokul.</summary>
    private async Task SeedReferenceAsync()
    {
        await using var ctx = _db.NewContext();
        var kars = new Province { Name = "Kars" };
        var antalya = new Province { Name = "Antalya" };
        ctx.Provinces.AddRange(kars, antalya);
        ctx.Subjects.AddRange(
            new Subject { Name = "Türkçe" }, new Subject { Name = "Matematik" }, new Subject { Name = "Fen Bilimleri" },
            new Subject { Name = "Sosyal Bilgiler" }, new Subject { Name = "İngilizce" }, new Subject { Name = "Din Kültürü ve Ahlak Bilgisi" });
        await ctx.SaveChangesAsync();
        _karsId = kars.Id;

        var ilkokul = new School { Name = "Atatürk İlkokulu", ProvinceId = kars.Id, ExternalCode = "100002", IsSeedData = true };
        var ortaokul = new School { Name = "Cumhuriyet Ortaokulu", ProvinceId = kars.Id, ExternalCode = "100004", IsSeedData = true };
        ctx.Schools.AddRange(
            ilkokul, ortaokul,
            new School { Name = "Kars Fen Lisesi", ProvinceId = kars.Id, ExternalCode = "100007", IsSeedData = true },
            new School { Name = "Elle Açılmış Ortaokulu", ProvinceId = kars.Id, IsSeedData = false },
            new School { Name = "A İlkokulu", ProvinceId = antalya.Id, ExternalCode = "200001", IsSeedData = true },
            new School { Name = "B İlkokulu", ProvinceId = antalya.Id, ExternalCode = "200002", IsSeedData = true },
            new School { Name = "C İlkokulu", ProvinceId = antalya.Id, ExternalCode = "200003", IsSeedData = true });
        await ctx.SaveChangesAsync();
        _ilkokulId = ilkokul.Id;
        _ortaokulId = ortaokul.Id;
    }

    private static IHostEnvironment Env(string name)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(name);
        return env;
    }

    private static IConfiguration Config(string? pw = TestSeedPassword)
    {
        var values = new Dictionary<string, string?>();
        if (pw is not null) values[TeacherSeedService.PasswordConfigKey] = pw;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    /// <summary>
    /// auth-api taklidi: e-posta → identity UserId sözlüğü tutar; ilk görüşte Created, sonra Existing.
    /// Belirli e-postalar için <see cref="FailEmails"/> ile hata, <see cref="KeycloakOnlyEmails"/> ile
    /// "Keycloak'ta var, identity'de yok" kısmi durumu simüle edilir.
    /// </summary>
    private sealed class FakeAuthApi : IAuthApiSeedClient
    {
        private readonly Dictionary<string, int> _users = new(StringComparer.OrdinalIgnoreCase);
        private int _nextId = 1000;

        public List<DevSeedUsersRequest> Requests { get; } = new();
        public HashSet<string> FailEmails { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> KeycloakOnlyEmails { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ForeignEmails { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int UserIdOf(string email) => _users[email];

        public Task<DevSeedUsersResponse> SeedUsersAsync(DevSeedUsersRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            var response = new DevSeedUsersResponse { Mode = request.Mode, KeycloakElapsedMs = 7, IdentityDbElapsedMs = 3 };
            foreach (var u in request.Users)
            {
                var r = new DevSeedUserResult { Email = u.Email };
                if (ForeignEmails.Contains(u.Email))
                {
                    r.KeycloakStatus = DevSeedUsersResponse.StatusSkippedForeign;
                    r.IdentityStatus = DevSeedUsersResponse.StatusSkippedForeign;
                    r.Error = "Keycloak'ta var ama identity'de seed kaydı yok — yabancı hesap, dokunulmadı.";
                }
                else if (FailEmails.Contains(u.Email))
                {
                    r.KeycloakStatus = DevSeedUsersResponse.StatusFailed;
                    r.IdentityStatus = DevSeedUsersResponse.StatusFailed;
                    r.Error = "simulated keycloak failure";
                }
                else if (_users.TryGetValue(u.Email, out var id))
                {
                    r.KeycloakId = "kc-" + id;
                    r.UserId = id;
                    r.KeycloakStatus = DevSeedUsersResponse.StatusExisting;
                    r.IdentityStatus = DevSeedUsersResponse.StatusExisting;
                }
                else
                {
                    id = _nextId++;
                    _users[u.Email] = id;
                    r.KeycloakId = "kc-" + id;
                    r.UserId = id;
                    r.KeycloakStatus = KeycloakOnlyEmails.Contains(u.Email) ? DevSeedUsersResponse.StatusExisting : DevSeedUsersResponse.StatusCreated;
                    r.IdentityStatus = DevSeedUsersResponse.StatusCreated;
                }
                response.Results.Add(r);
            }
            return Task.FromResult(response);
        }
    }

    private static TeacherSeedService NewService(AppDbContext ctx, IAuthApiSeedClient authApi, string environment = "Development", IConfiguration? config = null)
        => new(ctx, authApi, config ?? Config(), Env(environment), NullLogger<TeacherSeedService>.Instance);

    private static TeacherSeedOptions Opts(params string[] provinces) => new() { Provinces = provinces };

    // ---- guard'lar ----

    [Fact]
    public async Task Production_refuses_before_touching_auth_api_or_db()
    {
        await SeedReferenceAsync();
        var authApi = new FakeAuthApi();
        await using var ctx = _db.NewContext();

        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            NewService(ctx, authApi, "Production").RunAsync(Opts("Kars")));

        ex.Message.ShouldContain("Production");
        authApi.Requests.ShouldBeEmpty();
        (await _db.NewContext().Teachers.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Missing_password_config_fails_with_instructions_before_calling_auth_api()
    {
        await SeedReferenceAsync();
        var authApi = new FakeAuthApi();
        await using var ctx = _db.NewContext();

        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            NewService(ctx, authApi, config: Config(pw: null)).RunAsync(Opts("Kars")));

        ex.Message.ShouldContain(TeacherSeedService.PasswordConfigKey);
        ex.Message.ShouldContain("SeedData__Password");
        authApi.Requests.ShouldBeEmpty();
        (await _db.NewContext().Teachers.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Dry_run_needs_no_password_calls_nothing_and_writes_nothing()
    {
        await SeedReferenceAsync();
        var authApi = new FakeAuthApi();
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx, authApi, config: Config(pw: null))
            .RunAsync(new TeacherSeedOptions { Provinces = ["Kars"], DryRun = true });

        result.DryRun.ShouldBeTrue();
        result.Planned.ShouldBe(15);
        result.TeachersCreated.ShouldBe(0);
        result.Accounts.Count.ShouldBe(15);
        result.Accounts.ShouldAllBe(a => a.Status == "Planned");
        authApi.Requests.ShouldBeEmpty();
        (await _db.NewContext().Teachers.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Missing_subject_reference_data_fails_clearly()
    {
        await using (var ctx0 = _db.NewContext())
        {
            ctx0.Provinces.Add(new Province { Name = "Kars" });
            ctx0.Subjects.Add(new Subject { Name = "Matematik" });
            await ctx0.SaveChangesAsync();
        }
        await using var ctx = _db.NewContext();

        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            NewService(ctx, new FakeAuthApi()).RunAsync(Opts("Kars")));

        ex.Message.ShouldContain("Türkçe");
        ex.Message.ShouldNotContain("Matematik,");
    }

    // ---- dağılım ----

    [Fact]
    public async Task Ilkokul_gets_5_and_ortaokul_gets_10_teachers_with_correct_branch_split()
    {
        await SeedReferenceAsync();
        var authApi = new FakeAuthApi();
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx, authApi).RunAsync(Opts("Kars"));

        result.SchoolsSelected.ShouldBe(2);
        result.SchoolsIlkokul.ShouldBe(1);
        result.SchoolsOrtaokul.ShouldBe(1);
        result.SchoolsUnknownKind.ShouldBe(1); // Kars Fen Lisesi
        result.Planned.ShouldBe(15);
        result.TeachersCreated.ShouldBe(15);
        result.Failed.ShouldBe(0);

        var byBranch = result.Branches.ToDictionary(b => b.Branch, b => b.Created);
        byBranch[TeacherSeedBranch.Turkce].ShouldBe(3);          // 1 + 2
        byBranch[TeacherSeedBranch.Matematik].ShouldBe(3);
        byBranch[TeacherSeedBranch.FenBilimleri].ShouldBe(3);
        byBranch[TeacherSeedBranch.SosyalBilgiler].ShouldBe(3);
        byBranch[TeacherSeedBranch.Ingilizce].ShouldBe(2);       // 1 + 1
        byBranch[TeacherSeedBranch.DinKulturu].ShouldBe(1);      // 0 + 1

        await using var check = _db.NewContext();
        var teachers = await check.Teachers.Include(t => t.TeacherSubjects).ThenInclude(ts => ts.Subject).ToListAsync();
        teachers.Count.ShouldBe(15);
        teachers.Count(t => t.SchoolId == _ilkokulId).ShouldBe(5);
        teachers.Count(t => t.SchoolId == _ortaokulId).ShouldBe(10);
        teachers.ShouldAllBe(t => t.IsSeedData && t.ApprovalStatus == TeacherApprovalStatus.Approved && !t.IsIndependentTutor);
        teachers.ShouldAllBe(t => t.TeacherSubjects.Count == 1);
        teachers.Where(t => t.SchoolId == _ortaokulId).Count(t => t.TeacherSubjects.Single().Subject.Name == "Matematik").ShouldBe(2);
        teachers.Where(t => t.SchoolId == _ilkokulId).ShouldNotContain(t => t.TeacherSubjects.Single().Subject.Name == "Din Kültürü ve Ahlak Bilgisi");

        // Elle açılmış (IsSeedData=false) okula hiç öğretmen üretilmez.
        var manualSchoolId = await check.Schools.Where(s => s.Name == "Elle Açılmış Ortaokulu").Select(s => s.Id).SingleAsync();
        teachers.ShouldNotContain(t => t.SchoolId == manualSchoolId);
    }

    [Fact]
    public async Task Auth_api_request_carries_role_password_school_id_and_event_flag()
    {
        await SeedReferenceAsync();
        var authApi = new FakeAuthApi();
        await using var ctx = _db.NewContext();

        await NewService(ctx, authApi).RunAsync(new TeacherSeedOptions
        {
            Provinces = ["Kars"], EmitEvents = false, KeycloakMode = TeacherSeedOptions.KeycloakModePartialImport
        });

        var request = authApi.Requests.ShouldHaveSingleItem();
        request.Role.ShouldBe("Teacher");
        request.Password.ShouldBe(TestSeedPassword);
        request.EmitLocaleEvents.ShouldBeFalse();
        request.Mode.ShouldBe(TeacherSeedOptions.KeycloakModePartialImport);
        request.Users.Count.ShouldBe(15);
        request.Users.Where(u => u.Email.Contains(".100004.")).ShouldAllBe(u => u.SchoolId == _ortaokulId);
        request.Users.Where(u => u.Email.Contains(".100002.")).ShouldAllBe(u => u.SchoolId == _ilkokulId);
        request.Users.ShouldAllBe(u => u.SchoolId > 0);
    }

    [Fact]
    public async Task Batch_size_splits_requests()
    {
        await SeedReferenceAsync();
        var authApi = new FakeAuthApi();
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx, authApi).RunAsync(new TeacherSeedOptions { Provinces = ["Kars"], BatchSize = 4 });

        result.Batches.ShouldBe(4); // 15 → 4+4+4+3
        authApi.Requests.Select(r => r.Users.Count).ShouldBe([4, 4, 4, 3]);
        result.TeachersCreated.ShouldBe(15);
        result.KeycloakElapsedMs.ShouldBe(4 * 7);
    }

    // ---- deterministik isim / e-posta ----

    [Fact]
    public async Task Emails_and_names_are_deterministic_across_runs_and_follow_the_cleanup_pattern()
    {
        await SeedReferenceAsync();
        var authApi = new FakeAuthApi();

        TeacherSeedResult first, second;
        await using (var ctx = _db.NewContext())
            first = await NewService(ctx, authApi).RunAsync(new TeacherSeedOptions { Provinces = ["Kars"], DryRun = true });
        await using (var ctx = _db.NewContext())
            second = await NewService(ctx, authApi).RunAsync(new TeacherSeedOptions { Provinces = ["Kars"], DryRun = true });

        first.Accounts.Select(a => (a.Email, a.FullName)).ShouldBe(second.Accounts.Select(a => (a.Email, a.FullName)));
        first.Accounts.ShouldAllBe(a => a.Email.StartsWith(TeacherSeedPlan.EmailLocalPrefix) && a.Email.EndsWith("@" + TeacherSeedPlan.EmailDomain));
        first.Accounts.Select(a => a.Email).ShouldBeUnique();
        first.Accounts.ShouldContain(a => a.Email == "seed.t.100004.matematik.2@seed.examapp.local");
        first.Accounts.ShouldContain(a => a.Email == "seed.t.100002.ingilizce.1@seed.examapp.local");
        first.Accounts.ShouldAllBe(a => a.FullName.Contains(' '));
        // Aynı okulun 10 öğretmeni birbirinden farklı adlar almalı (hash çakışması yok — küçük havuzda kontrol).
        first.Accounts.Where(a => a.SchoolId == _ortaokulId).Select(a => a.FullName).Distinct().Count().ShouldBeGreaterThan(7);
    }

    [Fact]
    public void PickName_is_pure_and_school_code_falls_back_to_id()
    {
        TeacherSeedPlan.PickName("x@y").ShouldBe(TeacherSeedPlan.PickName("x@y"));
        TeacherSeedPlan.SchoolCode("775804", 5).ShouldBe("775804");
        TeacherSeedPlan.SchoolCode(" 77-58 ", 5).ShouldBe("7758");
        TeacherSeedPlan.SchoolCode(null, 5).ShouldBe("s5");
        TeacherSeedPlan.SchoolCode("--", 5).ShouldBe("s5");
        TeacherSeedPlan.ClassifyBySchoolName("KARS İMAM HATİP ORTAOKULU").ShouldBe(SchoolSeedKind.Ortaokul);
        TeacherSeedPlan.ClassifyBySchoolName("Şehit Ali Yıldırım ilkokulu").ShouldBe(SchoolSeedKind.Ilkokul);
        TeacherSeedPlan.ClassifyBySchoolName("Kars Fen Lisesi").ShouldBeNull();
        TeacherSeedPlan.TotalFor(SchoolSeedKind.Ilkokul).ShouldBe(5);
        TeacherSeedPlan.TotalFor(SchoolSeedKind.Ortaokul).ShouldBe(10);
    }

    // ---- idempotency / kısmi durum ----

    [Fact]
    public async Task Second_run_creates_nothing_and_reports_everything_existing()
    {
        await SeedReferenceAsync();
        var authApi = new FakeAuthApi();

        await using (var ctx = _db.NewContext())
            (await NewService(ctx, authApi).RunAsync(Opts("Kars"))).TeachersCreated.ShouldBe(15);

        TeacherSeedResult second;
        await using (var ctx = _db.NewContext())
            second = await NewService(ctx, authApi).RunAsync(Opts("Kars"));

        second.TeachersCreated.ShouldBe(0);
        second.TeachersExisting.ShouldBe(15);
        second.KeycloakExisting.ShouldBe(15);
        second.IdentityExisting.ShouldBe(15);
        second.Failed.ShouldBe(0);

        await using var check = _db.NewContext();
        (await check.Teachers.CountAsync()).ShouldBe(15);
        (await check.TeacherSubjects.CountAsync()).ShouldBe(15);
    }

    [Fact]
    public async Task Partial_state_keycloak_exists_but_identity_and_exam_missing_is_completed()
    {
        await SeedReferenceAsync();
        var authApi = new FakeAuthApi();
        authApi.KeycloakOnlyEmails.Add("seed.t.100002.turkce.1@seed.examapp.local");
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx, authApi).RunAsync(Opts("Kars"));

        result.KeycloakExisting.ShouldBe(1);
        result.KeycloakCreated.ShouldBe(14);
        result.IdentityCreated.ShouldBe(15);
        result.TeachersCreated.ShouldBe(15);
        result.Failed.ShouldBe(0);
    }

    [Fact]
    public async Task Partial_state_identity_exists_but_exam_teacher_missing_or_subject_missing_is_completed()
    {
        await SeedReferenceAsync();
        var authApi = new FakeAuthApi();

        // 1. koşu: her şey oluşur.
        await using (var ctx = _db.NewContext())
            await NewService(ctx, authApi).RunAsync(Opts("Kars"));

        // Exam tarafında kısmi bozulma: bir Teacher satırı silinmiş, birinin ders eşlemesi kaybolmuş.
        string deletedEmail = "seed.t.100004.fen.2@seed.examapp.local";
        string subjectLostEmail = "seed.t.100002.matematik.1@seed.examapp.local";
        await using (var ctx = _db.NewContext())
        {
            var deleted = await ctx.Teachers.SingleAsync(t => t.UserId == authApi.UserIdOf(deletedEmail));
            ctx.Teachers.Remove(deleted); // soft delete (global filter dışına çıkar)
            var lost = await ctx.TeacherSubjects.SingleAsync(ts => ts.Teacher.UserId == authApi.UserIdOf(subjectLostEmail));
            ctx.TeacherSubjects.Remove(lost);
            await ctx.SaveChangesAsync();
        }

        TeacherSeedResult second;
        await using (var ctx = _db.NewContext())
            second = await NewService(ctx, authApi).RunAsync(Opts("Kars"));

        second.KeycloakExisting.ShouldBe(15);
        second.IdentityExisting.ShouldBe(15);
        second.TeachersCreated.ShouldBe(1);   // silinen yeniden açıldı
        second.TeachersExisting.ShouldBe(14);
        second.Failed.ShouldBe(0);

        await using var check = _db.NewContext();
        (await check.Teachers.CountAsync()).ShouldBe(15);
        (await check.TeacherSubjects.CountAsync(ts => ts.Teacher.UserId == authApi.UserIdOf(subjectLostEmail))).ShouldBe(1);
    }

    [Fact]
    public async Task Failed_accounts_are_reported_and_do_not_get_a_teacher_row()
    {
        await SeedReferenceAsync();
        var authApi = new FakeAuthApi();
        authApi.FailEmails.Add("seed.t.100004.turkce.1@seed.examapp.local");
        authApi.FailEmails.Add("seed.t.100004.turkce.2@seed.examapp.local");
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx, authApi).RunAsync(Opts("Kars"));

        result.Failed.ShouldBe(2);
        result.TeachersCreated.ShouldBe(13);
        result.Errors.Count.ShouldBe(2);
        result.Errors.ShouldAllBe(e => e.Contains("simulated keycloak failure"));
        result.Branches.Single(b => b.Branch == TeacherSeedBranch.Turkce).Failed.ShouldBe(2);
        result.Accounts.Count(a => a.Status == "Failed").ShouldBe(2);
        (await _db.NewContext().Teachers.CountAsync()).ShouldBe(13);
    }

    [Fact]
    public async Task Colliding_school_codes_are_reported_failed_not_silently_existing()
    {
        await using (var ctx0 = _db.NewContext())
        {
            var kars = new Province { Name = "Kars" };
            ctx0.Provinces.Add(kars);
            ctx0.Subjects.AddRange(
                new Subject { Name = "Türkçe" }, new Subject { Name = "Matematik" }, new Subject { Name = "Fen Bilimleri" },
                new Subject { Name = "Sosyal Bilgiler" }, new Subject { Name = "İngilizce" }, new Subject { Name = "Din Kültürü ve Ahlak Bilgisi" });
            await ctx0.SaveChangesAsync();
            ctx0.Schools.AddRange(
                new School { Name = "A İlkokulu", ProvinceId = kars.Id, ExternalCode = "77-58", IsSeedData = true },
                new School { Name = "B İlkokulu", ProvinceId = kars.Id, ExternalCode = "7758", IsSeedData = true });
            await ctx0.SaveChangesAsync();
        }
        var authApi = new FakeAuthApi();
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx, authApi).RunAsync(Opts("Kars"));

        result.Planned.ShouldBe(10);
        result.TeachersCreated.ShouldBe(5);   // yalnızca ilk okul (ada göre A)
        result.Failed.ShouldBe(5);
        result.TeachersExisting.ShouldBe(0);
        result.Errors.Count.ShouldBe(5);
        result.Errors.ShouldAllBe(e => e.Contains("çakışma") && e.Contains("B İlkokulu"));
        authApi.Requests.Single().Users.Count.ShouldBe(5);
        (await _db.NewContext().Teachers.CountAsync()).ShouldBe(5);
    }

    [Fact]
    public async Task Foreign_accounts_skipped_by_auth_api_are_counted_failed_and_get_no_teacher()
    {
        await SeedReferenceAsync();
        var authApi = new FakeAuthApi();
        authApi.ForeignEmails.Add("seed.t.100002.turkce.1@seed.examapp.local");
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx, authApi).RunAsync(Opts("Kars"));

        result.Failed.ShouldBe(1);
        result.TeachersCreated.ShouldBe(14);
        result.Errors.Single().ShouldContain("yabancı");
        (await _db.NewContext().Teachers.CountAsync()).ShouldBe(14);
    }

    // ---- il / limit ----

    [Fact]
    public async Task Limit_schools_per_province_takes_first_n_by_name_deterministically()
    {
        await SeedReferenceAsync();
        var authApi = new FakeAuthApi();
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx, authApi).RunAsync(new TeacherSeedOptions
        {
            Provinces = ["Antalya", "Kars"], LimitSchoolsPerProvince = 2
        });

        var antalya = result.Provinces.Single(p => p.Province == "Antalya");
        antalya.SchoolsIlkokul.ShouldBe(2);
        antalya.SkippedByLimit.ShouldBe(1);
        antalya.Planned.ShouldBe(10);
        result.Accounts.Where(a => a.Province == "Antalya").Select(a => a.School).Distinct().ShouldBe(["A İlkokulu", "B İlkokulu"]);

        var kars = result.Provinces.Single(p => p.Province == "Kars");
        kars.SkippedByLimit.ShouldBe(1); // ada göre: Atatürk İlkokulu, Cumhuriyet Ortaokulu, Kars Fen Lisesi → ilk 2
        kars.Planned.ShouldBe(15);
        result.SchoolsSkippedByLimit.ShouldBe(2);
        result.TeachersCreated.ShouldBe(25);
    }

    [Fact]
    public async Task Unknown_province_is_reported_not_silently_ignored()
    {
        await SeedReferenceAsync();
        var authApi = new FakeAuthApi();
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx, authApi).RunAsync(Opts("Yalova", "kars"));

        result.UnmatchedProvinces.ShouldBe(["Yalova"]);
        result.Provinces.Single(p => p.Province == "Yalova").ProvinceMatched.ShouldBeFalse();
        result.Provinces.Single(p => p.Province == "kars").ProvinceMatched.ShouldBeTrue(); // İ/I duyarsız
        result.TeachersCreated.ShouldBe(15);
    }

    public void Dispose() => _db.Dispose();
}
