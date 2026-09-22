using ExamApp.Api.Data;
using ExamApp.Api.Services.Teachers.Seed;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// seed-tutors (issue #218): yarım formülü (il/branş; tek sayıda floor; sıfırda sıfır), deterministik e-posta/ad,
/// idempotency, pending oranı, tutor profili varsayılanları (aramada görünür), SchoolId=null, ortam guard'ı.
/// </summary>
public class TutorSeedServiceTests : IDisposable
{
    private const string TestSeedPassword = "seed-pw-example"; // example credential, test-only

    private readonly TestDb _db = TestDb.Create();
    private readonly Dictionary<string, int> _subjectIds = new();

    /// <summary>
    /// Kars: seed okul öğretmenleri — Matematik 5, Türkçe 4, Fen 1, İngilizce 0 (+ 1 elle açılmış Matematik öğretmeni, +1 seed-dışı okul).
    /// Antalya: Matematik 2. → Kars: mat 2, türkçe 2, fen 0; Antalya: mat 1.
    /// </summary>
    private async Task SeedReferenceAsync()
    {
        await using var ctx = _db.NewContext();
        var kars = new Province { Name = "Kars" };
        var antalya = new Province { Name = "Antalya" };
        ctx.Provinces.AddRange(kars, antalya);
        var subjects = new[] { "Türkçe", "Matematik", "Fen Bilimleri", "Sosyal Bilgiler", "İngilizce", "Din Kültürü ve Ahlak Bilgisi" }
            .Select(n => new Subject { Name = n }).ToList();
        ctx.Subjects.AddRange(subjects);
        await ctx.SaveChangesAsync();
        foreach (var s in subjects) _subjectIds[s.Name] = s.Id;

        var karsSchool = new School { Name = "Cumhuriyet Ortaokulu", ProvinceId = kars.Id, ExternalCode = "100004", IsSeedData = true };
        var karsSchool2 = new School { Name = "Atatürk İlkokulu", ProvinceId = kars.Id, ExternalCode = "100002", IsSeedData = true };
        var manualSchool = new School { Name = "Elle Açılmış Ortaokulu", ProvinceId = kars.Id, IsSeedData = false };
        var antalyaSchool = new School { Name = "A İlkokulu", ProvinceId = antalya.Id, ExternalCode = "200001", IsSeedData = true };
        ctx.Schools.AddRange(karsSchool, karsSchool2, manualSchool, antalyaSchool);
        await ctx.SaveChangesAsync();

        var nextUser = 500;
        void AddTeacher(School school, string subject, bool seed = true)
        {
            var t = new Teacher { UserId = nextUser++, SchoolId = school.Id, IsSeedData = seed, ApprovalStatus = TeacherApprovalStatus.Approved };
            t.TeacherSubjects.Add(new TeacherSubject { SubjectId = _subjectIds[subject] });
            ctx.Teachers.Add(t);
        }
        for (var i = 0; i < 3; i++) AddTeacher(karsSchool, "Matematik");
        for (var i = 0; i < 2; i++) AddTeacher(karsSchool2, "Matematik");
        for (var i = 0; i < 4; i++) AddTeacher(karsSchool, "Türkçe");
        AddTeacher(karsSchool2, "Fen Bilimleri");
        AddTeacher(karsSchool, "Matematik", seed: false);   // gerçek öğretmen seed okulda — tabana sayılmaz
        AddTeacher(manualSchool, "Matematik");              // seed öğretmen ama seed-dışı okulda — tabana sayılmaz
        for (var i = 0; i < 2; i++) AddTeacher(antalyaSchool, "Matematik");
        await ctx.SaveChangesAsync();
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

    private sealed class FakeAuthApi : IAuthApiSeedClient
    {
        private readonly Dictionary<string, int> _users = new(StringComparer.OrdinalIgnoreCase);
        private int _nextId = 9000;
        public List<DevSeedUsersRequest> Requests { get; } = new();
        public HashSet<string> FailEmails { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<DevSeedCleanupResponse> CleanupSeedUsersAsync(DevSeedCleanupRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<DevSeedUsersResponse> SeedUsersAsync(DevSeedUsersRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            var response = new DevSeedUsersResponse { Mode = request.Mode };
            foreach (var u in request.Users)
            {
                var r = new DevSeedUserResult { Email = u.Email };
                if (FailEmails.Contains(u.Email))
                {
                    r.KeycloakStatus = r.IdentityStatus = DevSeedUsersResponse.StatusFailed;
                    r.Error = "simulated";
                }
                else if (_users.TryGetValue(u.Email, out var id))
                {
                    r.UserId = id; r.KeycloakId = "kc-" + id;
                    r.KeycloakStatus = r.IdentityStatus = DevSeedUsersResponse.StatusExisting;
                }
                else
                {
                    id = _nextId++; _users[u.Email] = id;
                    r.UserId = id; r.KeycloakId = "kc-" + id;
                    r.KeycloakStatus = r.IdentityStatus = DevSeedUsersResponse.StatusCreated;
                }
                response.Results.Add(r);
            }
            return Task.FromResult(response);
        }
    }

    private static TutorSeedService NewService(AppDbContext ctx, IAuthApiSeedClient authApi, string environment = "Development", IConfiguration? config = null)
        => new(ctx, authApi, config ?? Config(), Env(environment), NullLogger<TutorSeedService>.Instance);

    private static TutorSeedOptions Opts(params string[] provinces) => new() { Provinces = provinces };

    // ---- plan yardımcıları (saf) ----

    [Theory]
    [InlineData(400, 200)]
    [InlineData(5, 2)]
    [InlineData(1, 0)]
    [InlineData(0, 0)]
    [InlineData(-3, 0)]
    public void HalfOf_floors_odd_counts_and_zero_gives_zero(int n, int expected)
        => TutorSeedPlan.HalfOf(n).ShouldBe(expected);

    [Theory]
    [InlineData(4, 0.0, 0)]
    [InlineData(4, 0.5, 2)]
    [InlineData(3, 0.5, 1)]
    [InlineData(4, 1.0, 4)]
    [InlineData(0, 0.5, 0)]
    public void PendingCountFor_floors_ratio(int count, double ratio, int expected)
        => TutorSeedPlan.PendingCountFor(count, ratio).ShouldBe(expected);

    [Theory]
    [InlineData("Kars", "kars")]
    [InlineData("İstanbul", "istanbul")]
    [InlineData("Şanlıurfa", "sanliurfa")]
    [InlineData("Ağrı", "agri")]
    [InlineData("Afyonkarahisar", "afyonkarahisar")]
    [InlineData("Kahramanmaraş", "kahramanmaras")]
    public void ProvinceSlug_is_ascii_and_email_matches_seed_regex(string province, string slug)
    {
        TutorSeedPlan.ProvinceSlug(province).ShouldBe(slug);
        var email = TutorSeedPlan.Email(slug, TeacherSeedPlan.Branches[1], 7);
        email.ShouldBe($"seed.i.{slug}.matematik.7@seed.examapp.local");
        SeedDataConventions.IsSeedEmail(email).ShouldBeTrue();
        TutorSeedPlan.ProvinceSlugFromEmail(email).ShouldBe(slug);
        TutorSeedPlan.IsTutorSeedEmail(email).ShouldBeTrue();
        TutorSeedPlan.IsTutorSeedEmail("seed.t.100004.matematik.1@seed.examapp.local").ShouldBeFalse();
    }

    [Fact]
    public void ProfileFor_is_deterministic_and_searchable()
    {
        var a = TutorSeedPlan.ProfileFor("seed.i.kars.matematik.1@seed.examapp.local", "Kars", TeacherSeedPlan.Branches[1]);
        var b = TutorSeedPlan.ProfileFor("seed.i.kars.matematik.1@seed.examapp.local", "Kars", TeacherSeedPlan.Branches[1]);
        a.ShouldBe(b);
        a.HourlyRate.ShouldBeInRange(250, 900);
        (a.HourlyRate % 50).ShouldBe(0);
        a.TeachesOnline.ShouldBeTrue();
        a.Bio.ShouldContain("Matematik");
        a.Bio.ShouldContain("Kars");
        a.Bio.Length.ShouldBeLessThanOrEqualTo(TutorSeedPlan.BioMaxLength);
    }

    // ---- guard ----

    [Fact]
    public async Task Production_refuses_before_touching_auth_api_or_db()
    {
        await SeedReferenceAsync();
        var authApi = new FakeAuthApi();
        await using var ctx = _db.NewContext();

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => NewService(ctx, authApi, "Production").RunAsync(Opts("Kars")));

        ex.Message.ShouldContain("Production");
        authApi.Requests.ShouldBeEmpty();
        (await _db.NewContext().Teachers.CountAsync(t => t.IsIndependentTutor)).ShouldBe(0);
    }

    [Fact]
    public async Task Invalid_pending_ratio_is_rejected()
    {
        await SeedReferenceAsync();
        await using var ctx = _db.NewContext();
        await Should.ThrowAsync<ArgumentException>(() => NewService(ctx, new FakeAuthApi()).RunAsync(new TutorSeedOptions { Provinces = ["Kars"], PendingRatio = 1.5 }));
    }

    // ---- yarım formülü ----

    [Fact]
    public async Task Half_of_school_teachers_per_province_and_branch_floor_zero_stays_zero()
    {
        await SeedReferenceAsync();
        var authApi = new FakeAuthApi();
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx, authApi).RunAsync(Opts("Kars", "Antalya"));

        result.SchoolTeachersCounted.ShouldBe(12); // Kars 5+4+1, Antalya 2 — gerçek öğretmen ve seed-dışı okul dışarıda
        Group(result, "Kars", TeacherSeedBranch.Matematik).ShouldSatisfyAllConditions(g => g.SchoolTeachers.ShouldBe(5), g => g.Planned.ShouldBe(2));
        Group(result, "Kars", TeacherSeedBranch.Turkce).ShouldSatisfyAllConditions(g => g.SchoolTeachers.ShouldBe(4), g => g.Planned.ShouldBe(2));
        Group(result, "Kars", TeacherSeedBranch.FenBilimleri).ShouldSatisfyAllConditions(g => g.SchoolTeachers.ShouldBe(1), g => g.Planned.ShouldBe(0));
        Group(result, "Kars", TeacherSeedBranch.Ingilizce).ShouldSatisfyAllConditions(g => g.SchoolTeachers.ShouldBe(0), g => g.Planned.ShouldBe(0));
        Group(result, "Antalya", TeacherSeedBranch.Matematik).Planned.ShouldBe(1);
        result.Planned.ShouldBe(5);
        result.TutorsCreated.ShouldBe(5);
        result.Failed.ShouldBe(0);

        await using var check = _db.NewContext();
        var tutors = await check.Teachers.Include(t => t.TeacherSubjects).Where(t => t.IsIndependentTutor).ToListAsync();
        tutors.Count.ShouldBe(5);
        tutors.ShouldAllBe(t => t.IsSeedData && t.SchoolId == null && t.ApprovalStatus == TeacherApprovalStatus.Approved);
        tutors.ShouldAllBe(t => t.HourlyRate != null && t.TeachesOnline && t.Bio != null && t.TeacherSubjects.Count == 1);
        tutors.Count(t => t.TeacherSubjects.Single().SubjectId == _subjectIds["Matematik"]).ShouldBe(3);

        // auth-api: rol Teacher, SchoolId yok (attribute üretilmez)
        var request = authApi.Requests.ShouldHaveSingleItem();
        request.Role.ShouldBe("Teacher");
        request.Users.ShouldAllBe(u => u.SchoolId == null);
        request.Users.Select(u => u.Email).ShouldBe([
            "seed.i.kars.turkce.1@seed.examapp.local", "seed.i.kars.turkce.2@seed.examapp.local",
            "seed.i.kars.matematik.1@seed.examapp.local", "seed.i.kars.matematik.2@seed.examapp.local",
            "seed.i.antalya.matematik.1@seed.examapp.local"]);
    }

    [Fact]
    public async Task Limit_schools_per_province_counts_only_first_n_schools_by_name()
    {
        await SeedReferenceAsync();
        await using var ctx = _db.NewContext();

        // Kars ada göre: Atatürk İlkokulu (mat 2, fen 1), Cumhuriyet Ortaokulu (mat 3, türkçe 4) → limit 1: yalnızca Atatürk
        var result = await NewService(ctx, new FakeAuthApi()).RunAsync(new TutorSeedOptions { Provinces = ["Kars"], LimitSchoolsPerProvince = 1, DryRun = true });

        result.SchoolsCounted.ShouldBe(1);
        result.SchoolsSkippedByLimit.ShouldBe(1);
        Group(result, "Kars", TeacherSeedBranch.Matematik).ShouldSatisfyAllConditions(g => g.SchoolTeachers.ShouldBe(2), g => g.Planned.ShouldBe(1));
        Group(result, "Kars", TeacherSeedBranch.Turkce).Planned.ShouldBe(0);
    }

    [Fact]
    public async Task Existing_tutors_are_not_counted_as_base()
    {
        await SeedReferenceAsync();
        var authApi = new FakeAuthApi();
        await using (var ctx = _db.NewContext())
            await NewService(ctx, authApi).RunAsync(Opts("Kars"));

        await using var ctx2 = _db.NewContext();
        var second = await NewService(ctx2, authApi).RunAsync(new TutorSeedOptions { Provinces = ["Kars"], DryRun = true });
        second.SchoolTeachersCounted.ShouldBe(10);
        second.Planned.ShouldBe(4);
    }

    // ---- deterministik / idempotent ----

    [Fact]
    public async Task Dry_run_is_deterministic_needs_no_password_and_writes_nothing()
    {
        await SeedReferenceAsync();
        var authApi = new FakeAuthApi();

        TutorSeedResult first, second;
        await using (var ctx = _db.NewContext())
            first = await NewService(ctx, authApi, config: Config(pw: null)).RunAsync(new TutorSeedOptions { Provinces = ["Kars"], DryRun = true });
        await using (var ctx = _db.NewContext())
            second = await NewService(ctx, authApi, config: Config(pw: null)).RunAsync(new TutorSeedOptions { Provinces = ["Kars"], DryRun = true });

        first.Accounts.Select(a => (a.Email, a.FullName, a.HourlyRate, a.TeachesInPerson)).ShouldBe(second.Accounts.Select(a => (a.Email, a.FullName, a.HourlyRate, a.TeachesInPerson)));
        first.Accounts.ShouldAllBe(a => a.Status == "Planned" && a.FullName.Contains(' '));
        first.Accounts.Select(a => a.Email).ShouldBeUnique();
        authApi.Requests.ShouldBeEmpty();
        (await _db.NewContext().Teachers.CountAsync(t => t.IsIndependentTutor)).ShouldBe(0);
    }

    [Fact]
    public async Task Second_run_creates_nothing_and_completes_missing_subject()
    {
        await SeedReferenceAsync();
        var authApi = new FakeAuthApi();
        await using (var ctx = _db.NewContext())
            (await NewService(ctx, authApi).RunAsync(Opts("Kars"))).TutorsCreated.ShouldBe(4);

        // Kısmi bozulma: bir tutor'un ders eşlemesi silinmiş.
        await using (var ctx = _db.NewContext())
        {
            var lost = await ctx.TeacherSubjects.FirstAsync(ts => ts.Teacher.IsIndependentTutor);
            ctx.TeacherSubjects.Remove(lost);
            await ctx.SaveChangesAsync();
        }

        TutorSeedResult second;
        await using (var ctx = _db.NewContext())
            second = await NewService(ctx, authApi).RunAsync(Opts("Kars"));

        second.TutorsCreated.ShouldBe(0);
        second.TutorsExisting.ShouldBe(4);
        second.KeycloakExisting.ShouldBe(4);
        second.Failed.ShouldBe(0);

        await using var check = _db.NewContext();
        (await check.Teachers.CountAsync(t => t.IsIndependentTutor)).ShouldBe(4);
        (await check.TeacherSubjects.CountAsync(ts => ts.Teacher.IsIndependentTutor)).ShouldBe(4);
    }

    [Fact]
    public async Task Failed_accounts_are_reported_and_get_no_teacher_row()
    {
        await SeedReferenceAsync();
        var authApi = new FakeAuthApi();
        authApi.FailEmails.Add("seed.i.kars.matematik.2@seed.examapp.local");
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx, authApi).RunAsync(Opts("Kars"));

        result.Failed.ShouldBe(1);
        result.TutorsCreated.ShouldBe(3);
        result.Errors.Single().ShouldContain("simulated");
        Group(result, "Kars", TeacherSeedBranch.Matematik).Failed.ShouldBe(1);
        (await _db.NewContext().Teachers.CountAsync(t => t.IsIndependentTutor)).ShouldBe(3);
    }

    // ---- pending oranı ----

    [Fact]
    public async Task Pending_ratio_leaves_highest_ordinals_pending_deterministically()
    {
        await SeedReferenceAsync();
        var authApi = new FakeAuthApi();
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx, authApi).RunAsync(new TutorSeedOptions { Provinces = ["Kars"], PendingRatio = 0.5 });

        result.Planned.ShouldBe(4);
        result.PlannedPending.ShouldBe(2); // mat 2 → 1, türkçe 2 → 1
        result.Accounts.Where(a => a.Pending).Select(a => a.Email).ShouldBe(
            ["seed.i.kars.turkce.2@seed.examapp.local", "seed.i.kars.matematik.2@seed.examapp.local"]);

        await using var check = _db.NewContext();
        var tutors = await check.Teachers.Where(t => t.IsIndependentTutor).ToListAsync();
        tutors.Count(t => t.ApprovalStatus == TeacherApprovalStatus.Pending).ShouldBe(2);
        tutors.Count(t => t.ApprovalStatus == TeacherApprovalStatus.Approved).ShouldBe(2);
    }

    [Fact]
    public async Task Pending_ratio_one_makes_all_pending()
    {
        await SeedReferenceAsync();
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx, new FakeAuthApi()).RunAsync(new TutorSeedOptions { Provinces = ["Kars"], PendingRatio = 1 });

        result.PlannedPending.ShouldBe(4);
        result.Accounts.ShouldAllBe(a => a.Pending);
        (await _db.NewContext().Teachers.CountAsync(t => t.IsIndependentTutor && t.ApprovalStatus == TeacherApprovalStatus.Pending)).ShouldBe(4);
    }

    [Fact]
    public async Task Pending_ratio_defaults_to_1_on_staging_and_0_on_development()
    {
        await SeedReferenceAsync();
        await using (var ctx = _db.NewContext())
        {
            var staging = await NewService(ctx, new FakeAuthApi(), "Staging").RunAsync(new TutorSeedOptions { Provinces = ["Kars"], DryRun = true });
            staging.PendingRatio.ShouldBe(1);
            staging.PlannedPending.ShouldBe(4);
        }
        await using (var ctx = _db.NewContext())
        {
            var dev = await NewService(ctx, new FakeAuthApi(), "Development").RunAsync(new TutorSeedOptions { Provinces = ["Kars"], DryRun = true });
            dev.PendingRatio.ShouldBe(0);
            dev.PlannedPending.ShouldBe(0);
        }
        await using (var ctx = _db.NewContext())
        {
            // Açık değer ortamı ezer
            var stagingExplicit = await NewService(ctx, new FakeAuthApi(), "Staging").RunAsync(new TutorSeedOptions { Provinces = ["Kars"], DryRun = true, PendingRatio = 0 });
            stagingExplicit.PlannedPending.ShouldBe(0);
        }
    }

    [Fact]
    public void OrderSchoolsDeterministic_sorts_by_tr_name_then_code_then_id()
    {
        var schools = new List<School>
        {
            new() { Id = 5, Name = "Şehit Ali İlkokulu", ExternalCode = "3" },
            new() { Id = 4, Name = "Cumhuriyet Ortaokulu", ExternalCode = "2" },
            new() { Id = 3, Name = "Cumhuriyet Ortaokulu", ExternalCode = "1" },
            new() { Id = 2, Name = "Cumhuriyet Ortaokulu", ExternalCode = "1" },
            new() { Id = 1, Name = "Atatürk İlkokulu", ExternalCode = null },
            new() { Id = 6, Name = "Ilıca İlkokulu", ExternalCode = "9" },
            new() { Id = 7, Name = "İnönü İlkokulu", ExternalCode = "8" },
        };

        var ordered = TeacherSeedPlan.OrderSchoolsDeterministic(schools).Select(s => s.Id).ToList();

        // tr-TR: A < C < I (ı) < İ < Ş ; aynı ad → kod → id
        ordered.ShouldBe([1, 2, 3, 4, 6, 7, 5]);
        TeacherSeedPlan.OrderSchoolsDeterministic(schools.AsEnumerable().Reverse()).Select(s => s.Id).ShouldBe(ordered);
    }

    [Fact]
    public async Task Unknown_province_is_reported()
    {
        await SeedReferenceAsync();
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx, new FakeAuthApi()).RunAsync(new TutorSeedOptions { Provinces = ["Yalova", "kars"], DryRun = true });
        result.UnmatchedProvinces.ShouldBe(["Yalova"]);
        result.Planned.ShouldBe(4);
        // Etiket/slug/Bio DB'deki il adından: "kars" istense de "Kars"
        result.Groups.ShouldAllBe(g => g.Province == "Kars");
        result.Accounts.ShouldAllBe(a => a.Province == "Kars" && a.Email.StartsWith("seed.i.kars."));
    }

    private static TutorSeedGroupSummary Group(TutorSeedResult r, string province, TeacherSeedBranch branch)
        => r.Groups.Single(g => g.Province == province && g.Branch == branch);

    public void Dispose() => _db.Dispose();
}
