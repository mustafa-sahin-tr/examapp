using ExamApp.Api.Data;
using ExamApp.Api.Services.Seed.Cleanup;
using ExamApp.Api.Services.Teachers.Seed;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// seed-cleanup (issue #218): dry-run yazmaz; --apply seed öğretmen/okul satırlarını hard-delete eder, seed-dışı
/// hiçbir satıra dokunmaz (gerçek öğretmen + gerçek okul + gerçek öğrenci fixture'ı); bağımlı verili seed öğretmen
/// atlanır (--force ile silinir); seed-dışı öğretmeni/öğrencisi olan seed okul atlanır; ikinci koşu 0; auth-api'ye
/// ExcludeUserIds gider; auth-api hatası exam tarafını geri almaz ve raporlanır; Production reddi.
/// </summary>
public class SeedCleanupServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    private int _seedSchoolCleanId, _seedSchoolWithRealTeacherId, _seedSchoolWithStudentId, _realSchoolId;
    private int _seedTeacherAId, _seedTeacherBId, _seedTutorId, _seedTeacherWithBookingId, _seedTeacherAuthorId, _seedTeacherSlotOnlyId;
    private int _realTeacherId, _realTeacherInSeedSchoolId;
    private const int RealUserId = 1, RealUser2Id = 2;
    private const int SeedUserA = 1001, SeedUserB = 1002, SeedUserTutor = 1003, SeedUserBooking = 1004, SeedUserAuthor = 1005, SeedUserSlotOnly = 1006;

    private async Task SeedFixtureAsync()
    {
        await using var ctx = _db.NewContext();
        var kars = new Province { Name = "Kars" };
        ctx.Provinces.Add(kars);
        var mat = new Subject { Name = "Matematik" };
        var turkce = new Subject { Name = "Türkçe" };
        ctx.Subjects.AddRange(mat, turkce);
        var grade = new Grade { Name = "5" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();

        var seedClean = new School { Name = "Cumhuriyet Ortaokulu", ProvinceId = kars.Id, ExternalCode = "100004", IsSeedData = true };
        var seedWithReal = new School { Name = "Atatürk İlkokulu", ProvinceId = kars.Id, ExternalCode = "100002", IsSeedData = true };
        var seedWithStudent = new School { Name = "Fatih İlkokulu", ProvinceId = kars.Id, ExternalCode = "100009", IsSeedData = true };
        var real = new School { Name = "Gerçek Okul", ProvinceId = kars.Id, IsSeedData = false };
        ctx.Schools.AddRange(seedClean, seedWithReal, seedWithStudent, real);
        await ctx.SaveChangesAsync();
        (_seedSchoolCleanId, _seedSchoolWithRealTeacherId, _seedSchoolWithStudentId, _realSchoolId) = (seedClean.Id, seedWithReal.Id, seedWithStudent.Id, real.Id);

        Teacher T(int userId, School? school, bool seed, bool tutor = false, TeacherApprovalStatus status = TeacherApprovalStatus.Approved)
        {
            var t = new Teacher { UserId = userId, SchoolId = school?.Id, IsSeedData = seed, IsIndependentTutor = tutor, ApprovalStatus = status };
            t.TeacherSubjects.Add(new TeacherSubject { SubjectId = mat.Id });
            ctx.Teachers.Add(t);
            return t;
        }
        var seedA = T(SeedUserA, seedClean, seed: true);
        var seedB = T(SeedUserB, seedClean, seed: true);
        seedB.TeacherSubjects.Add(new TeacherSubject { SubjectId = turkce.Id });
        var seedTutor = T(SeedUserTutor, null, seed: true, tutor: true, status: TeacherApprovalStatus.Pending);
        var seedBooking = T(SeedUserBooking, seedWithReal, seed: true);
        var seedAuthor = T(SeedUserAuthor, seedWithReal, seed: true);
        var seedSlotOnly = T(SeedUserSlotOnly, seedClean, seed: true); // müsaitlik verisi var, randevu yok
        var realTeacher = T(RealUserId, real, seed: false);
        var realInSeed = T(RealUser2Id, seedWithReal, seed: false);
        var realStudent = new Student { UserId = 77, StudentNumber = "S-1", SchoolId = seedWithStudent.Id };
        ctx.Students.Add(realStudent);
        await ctx.SaveChangesAsync();
        (_seedTeacherAId, _seedTeacherBId, _seedTutorId, _seedTeacherWithBookingId, _seedTeacherAuthorId, _seedTeacherSlotOnlyId, _realTeacherId, _realTeacherInSeedSchoolId) =
            (seedA.Id, seedB.Id, seedTutor.Id, seedBooking.Id, seedAuthor.Id, seedSlotOnly.Id, realTeacher.Id, realInSeed.Id);

        // Gerçek öğrencinin randevusu: seedBooking ve gerçek öğretmen (hiçbir modda silinmemeli).
        BookingSeed.Add(ctx, seedBooking.Id, realStudent.Id, BookingStatus.Approved, hour: 9);
        BookingSeed.Add(ctx, realTeacher.Id, realStudent.Id, BookingStatus.Approved, hour: 10);
        ctx.RecurringAvailabilityRules.Add(new RecurringAvailabilityRule
        {
            TeacherId = seedBooking.Id, DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(10, 0), EffectiveFrom = new DateOnly(2026, 9, 1)
        });
        // Yalnızca müsaitlik (slot + kural), randevu yok: --force ile silinebilir.
        ctx.TeacherAvailabilitySlots.Add(new TeacherAvailabilitySlot
        {
            TeacherId = seedSlotOnly.Id, Date = new DateOnly(2026, 10, 2), StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(10, 0), CreatedAt = DateTime.UtcNow
        });
        ctx.RecurringAvailabilityRules.Add(new RecurringAvailabilityRule
        {
            TeacherId = seedSlotOnly.Id, DayOfWeek = DayOfWeek.Tuesday, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(10, 0), EffectiveFrom = new DateOnly(2026, 9, 1)
        });

        // İçerik: seedAuthor bir worksheet yazmış; gerçek öğretmen de.
        var wsSeed = new Worksheet { Name = "Seed WS", Description = "d", GradeId = grade.Id };
        var wsReal = new Worksheet { Name = "Real WS", Description = "d", GradeId = grade.Id };
        ctx.Worksheets.AddRange(wsSeed, wsReal);
        await ctx.SaveChangesAsync();
        wsSeed.CreateUserId = SeedUserAuthor;
        wsReal.CreateUserId = RealUserId;
        await ctx.SaveChangesAsync();
    }

    private sealed class FakeAuthApi : IAuthApiSeedClient
    {
        public List<DevSeedCleanupRequest> Requests { get; } = new();
        public Func<DevSeedCleanupRequest, DevSeedCleanupResponse>? Handler { get; set; }
        public Exception? Throw { get; set; }

        public Task<DevSeedUsersResponse> SeedUsersAsync(DevSeedUsersRequest request, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<DevSeedCleanupResponse> CleanupSeedUsersAsync(DevSeedCleanupRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            if (Throw is not null) throw Throw;
            return Task.FromResult(Handler?.Invoke(request) ?? DefaultResponse(request));
        }

        /// <summary>Altı seed kullanıcı; Exclude edilenler Excluded, kalanlar Planned/Deleted. Tutor e-postası il taşır.</summary>
        public static DevSeedCleanupResponse DefaultResponse(DevSeedCleanupRequest r)
        {
            var emails = new Dictionary<int, string>
            {
                [SeedUserA] = "seed.t.100004.matematik.1@seed.examapp.local",
                [SeedUserB] = "seed.t.100004.matematik.2@seed.examapp.local",
                [SeedUserTutor] = "seed.i.kars.matematik.1@seed.examapp.local",
                [SeedUserBooking] = "seed.t.100002.matematik.1@seed.examapp.local",
                [SeedUserAuthor] = "seed.t.100002.matematik.2@seed.examapp.local",
                [SeedUserSlotOnly] = "seed.t.100004.matematik.3@seed.examapp.local"
            };
            var resp = new DevSeedCleanupResponse { DryRun = r.DryRun };
            foreach (var (id, email) in emails)
            {
                var excluded = r.ExcludeUserIds.Contains(id);
                var status = excluded ? DevSeedCleanupResponse.StatusExcluded : r.DryRun ? DevSeedCleanupResponse.StatusPlanned : DevSeedCleanupResponse.StatusDeleted;
                resp.Users.Add(new DevSeedCleanupUser { Email = email, UserId = id, KeycloakId = "kc-" + id, KeycloakStatus = status, IdentityStatus = status });
                if (excluded) { resp.KeycloakExcluded++; resp.IdentityExcluded++; }
                else if (!r.DryRun) { resp.KeycloakDeleted++; resp.IdentityDeleted++; }
            }
            return resp;
        }
    }

    private static IHostEnvironment Env(string name)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(name);
        return env;
    }

    private static SeedCleanupService NewService(AppDbContext ctx, IAuthApiSeedClient authApi, string environment = "Development")
        => new(ctx, authApi, Env(environment), NullLogger<SeedCleanupService>.Instance);

    private async Task<(int Teachers, int Schools, int TeacherSubjects, int Bookings, int Slots, int Rules, int Worksheets, int Students)> CountsAsync()
    {
        await using var ctx = _db.NewContext();
        return (
            await ctx.Teachers.IgnoreQueryFilters().CountAsync(),
            await ctx.Schools.IgnoreQueryFilters().CountAsync(),
            await ctx.TeacherSubjects.IgnoreQueryFilters().CountAsync(),
            await ctx.Bookings.IgnoreQueryFilters().CountAsync(),
            await ctx.TeacherAvailabilitySlots.IgnoreQueryFilters().CountAsync(),
            await ctx.RecurringAvailabilityRules.IgnoreQueryFilters().CountAsync(),
            await ctx.Worksheets.IgnoreQueryFilters().CountAsync(),
            await ctx.Students.IgnoreQueryFilters().CountAsync());
    }

    // ---- guard ----

    [Fact]
    public async Task Production_refuses_before_reading_or_calling_auth_api()
    {
        await SeedFixtureAsync();
        var authApi = new FakeAuthApi();
        await using var ctx = _db.NewContext();

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => NewService(ctx, authApi, "Production").RunAsync(new SeedCleanupOptions { Apply = true }));

        ex.Message.ShouldContain("Production");
        authApi.Requests.ShouldBeEmpty();
        (await CountsAsync()).Teachers.ShouldBe(8);
    }

    // ---- dry-run = rapor ----

    [Fact]
    public async Task Dry_run_writes_nothing_reports_inventory_and_calls_auth_api_in_dry_run()
    {
        await SeedFixtureAsync();
        var before = await CountsAsync();
        var authApi = new FakeAuthApi();
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx, authApi).RunAsync(new SeedCleanupOptions());

        r.Applied.ShouldBeFalse();
        r.SeedSchools.ShouldBe(3);
        r.SeedSchoolTeachers.ShouldBe(5);
        r.SeedTutors.ShouldBe(1);
        r.SeedTutorsPending.ShouldBe(1);
        r.TeachersDeleted.ShouldBe(3);                    // A, B, tutor
        r.TeachersSkippedRealStudentBooking.ShouldBe(1);  // gerçek öğrenci randevusu
        r.RealStudentBookings.ShouldBe(1);
        r.TeachersSkippedScheduling.ShouldBe(1);          // yalnızca slot/kural
        r.TeachersSkippedContent.ShouldBe(1);             // worksheet yazan
        r.SchoolsDeleted.ShouldBe(0);             // Cumhuriyet: korunan slotOnly; Atatürk: gerçek + korunanlar; Fatih: öğrenci
        r.SchoolsSkippedNonSeedTeacher.ShouldBe(1);
        r.SchoolsSkippedSeedTeacherKept.ShouldBe(2);
        r.SchoolsSkippedStudent.ShouldBe(1);

        // Özet rapor: il/tür ve il/branş kırılımı (tutor ili e-postadan)
        r.SchoolGroups.ShouldContain(g => g.Province == "Kars" && g.Kind == "Ortaokul" && g.Count == 1);
        r.SchoolGroups.ShouldContain(g => g.Province == "Kars" && g.Kind == "İlkokul" && g.Count == 2);
        r.TeacherGroups.Single(g => g.Province == "Kars" && g.Subject == "Matematik").ShouldSatisfyAllConditions(
            g => g.SchoolTeachers.ShouldBe(5), g => g.Tutors.ShouldBe(1)); // tutor ili e-postadaki slug'dan Province adına çözülür
        r.TeacherGroups.Single(g => g.Province == "Kars" && g.Subject == "Türkçe").SchoolTeachers.ShouldBe(1);
        r.TeacherGroups.Count.ShouldBe(2);

        var req = authApi.Requests.ShouldHaveSingleItem();
        req.DryRun.ShouldBeTrue();
        req.ExcludeUserIds.ShouldBe([SeedUserBooking, SeedUserAuthor, SeedUserSlotOnly], ignoreOrder: true);
        r.AuthPlanned.ShouldBe(3);
        r.IdentityExcluded.ShouldBe(3);

        (await CountsAsync()).ShouldBe(before);
    }

    // ---- apply ----

    [Fact]
    public async Task Apply_hard_deletes_seed_rows_only_and_skips_dependents_without_force()
    {
        await SeedFixtureAsync();
        var authApi = new FakeAuthApi();
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx, authApi).RunAsync(new SeedCleanupOptions { Apply = true });

        r.Applied.ShouldBeTrue();
        r.TeachersDeleted.ShouldBe(3);
        r.TeacherSubjectsDeleted.ShouldBe(4); // A(1) + B(2) + tutor(1)
        r.SchoolsDeleted.ShouldBe(0);         // Cumhuriyet: korunan seed öğretmen (slotOnly) var
        r.SchoolsSkippedSeedTeacherKept.ShouldBe(2);
        r.SlotsDeleted.ShouldBe(0);
        r.KeycloakDeleted.ShouldBe(3);
        r.IdentityDeleted.ShouldBe(3);
        r.TotalFailed.ShouldBe(0);

        var req = authApi.Requests.ShouldHaveSingleItem();
        req.DryRun.ShouldBeFalse();
        req.ExcludeUserIds.ShouldBe([SeedUserBooking, SeedUserAuthor, SeedUserSlotOnly], ignoreOrder: true);

        await using var check = _db.NewContext();
        var teacherIds = await check.Teachers.IgnoreQueryFilters().Select(t => t.Id).ToListAsync();
        teacherIds.ShouldBe([_seedTeacherWithBookingId, _seedTeacherAuthorId, _seedTeacherSlotOnlyId, _realTeacherId, _realTeacherInSeedSchoolId], ignoreOrder: true);
        (await check.Schools.IgnoreQueryFilters().CountAsync()).ShouldBe(4);
        // Hard delete: soft-delete kalıntısı yok
        (await check.Teachers.IgnoreQueryFilters().AnyAsync(t => t.Id == _seedTeacherAId || t.Id == _seedTutorId)).ShouldBeFalse();
        // Gerçek veriler yerinde
        (await check.Bookings.IgnoreQueryFilters().CountAsync()).ShouldBe(2);
        (await check.Worksheets.IgnoreQueryFilters().CountAsync()).ShouldBe(2);
        (await check.Students.IgnoreQueryFilters().CountAsync()).ShouldBe(1);
        (await check.TeacherSubjects.IgnoreQueryFilters().CountAsync(ts => ts.TeacherId == _realTeacherId)).ShouldBe(1);
    }

    [Fact]
    public async Task Apply_with_force_deletes_availability_rows_but_never_bookings_worksheets_or_real_rows()
    {
        await SeedFixtureAsync();
        var authApi = new FakeAuthApi();
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx, authApi).RunAsync(new SeedCleanupOptions { Apply = true, Force = true });

        r.TeachersDeleted.ShouldBe(5);                    // A, B, tutor, author, slotOnly
        r.TeachersSkippedRealStudentBooking.ShouldBe(1);  // booking'li öğretmen --force ile de kalır
        r.TeachersForceDeleted.ShouldBe(1);               // slotOnly
        r.TeachersOrphanedContent.ShouldBe(1);
        r.WorksheetsOrphaned.ShouldBe(1);
        r.SlotsDeleted.ShouldBe(1);
        r.RulesDeleted.ShouldBe(1);
        r.SchoolsDeleted.ShouldBe(1); // Cumhuriyet; Atatürk gerçek + booking'li öğretmenli, Fatih öğrencili
        authApi.Requests.Single().ExcludeUserIds.ShouldBe([SeedUserBooking]);
        r.KeycloakDeleted.ShouldBe(5);

        await using var check = _db.NewContext();
        (await check.Teachers.IgnoreQueryFilters().Select(t => t.Id).ToListAsync())
            .ShouldBe([_seedTeacherWithBookingId, _realTeacherId, _realTeacherInSeedSchoolId], ignoreOrder: true);
        (await check.Bookings.IgnoreQueryFilters().Select(b => b.TeacherId).ToListAsync()).ShouldBe([_seedTeacherWithBookingId, _realTeacherId], ignoreOrder: true);
        (await check.TeacherAvailabilitySlots.IgnoreQueryFilters().Select(s => s.TeacherId).ToListAsync()).ShouldBe([_seedTeacherWithBookingId, _realTeacherId], ignoreOrder: true);
        (await check.RecurringAvailabilityRules.IgnoreQueryFilters().Select(x => x.TeacherId).ToListAsync()).ShouldBe([_seedTeacherWithBookingId]);
        (await check.Worksheets.IgnoreQueryFilters().CountAsync()).ShouldBe(2); // worksheet asla silinmez
        (await check.Schools.IgnoreQueryFilters().CountAsync()).ShouldBe(3);
    }

    [Fact]
    public async Task Seed_teacher_who_authored_a_question_is_skipped_without_force()
    {
        await SeedFixtureAsync();
        await using (var ctx0 = _db.NewContext())
        {
            var q = new Question { Text = "seed q", Point = 1 };
            ctx0.Questions.Add(q);
            await ctx0.SaveChangesAsync();
            q.CreateUserId = SeedUserA;
            await ctx0.SaveChangesAsync();
        }
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx, new FakeAuthApi()).RunAsync(new SeedCleanupOptions { Apply = true });

        r.TeachersSkippedContent.ShouldBe(2); // author (worksheet) + A (soru)
        r.TeachersDeleted.ShouldBe(2);        // B, tutor
        r.Skipped.ShouldContain(x => x.Contains($"user {SeedUserA}") && x.Contains("worksheet/soru=1"));
        (await _db.NewContext().Teachers.IgnoreQueryFilters().AnyAsync(t => t.Id == _seedTeacherAId)).ShouldBeTrue();
        (await _db.NewContext().Questions.IgnoreQueryFilters().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Second_apply_run_finds_nothing_left()
    {
        await SeedFixtureAsync();
        var authApi = new FakeAuthApi();
        await using (var ctx = _db.NewContext())
            await NewService(ctx, authApi).RunAsync(new SeedCleanupOptions { Apply = true, Force = true });

        // İkinci koşu: auth-api de boş döner
        authApi.Handler = r => new DevSeedCleanupResponse { DryRun = r.DryRun };
        SeedCleanupResult second;
        await using (var ctx = _db.NewContext())
            second = await NewService(ctx, authApi).RunAsync(new SeedCleanupOptions { Apply = true, Force = true });

        second.TeachersDeleted.ShouldBe(0);
        second.TeacherSubjectsDeleted.ShouldBe(0);
        second.SchoolsDeleted.ShouldBe(0);
        second.SlotsDeleted.ShouldBe(0);
        second.TeachersSkippedRealStudentBooking.ShouldBe(1); // her koşuda aynı: dokunulmaz
        second.SeedSchools.ShouldBe(2);          // hâlâ engelli seed okullar (gerçek veri bağlı) — dokunulmaz
        second.SchoolsSkippedNonSeedTeacher.ShouldBe(1);
        second.SchoolsSkippedStudent.ShouldBe(1);
        second.KeycloakDeleted.ShouldBe(0);
        (await CountsAsync()).Teachers.ShouldBe(3);
    }

    [Fact]
    public async Task Soft_deleted_seed_teacher_is_also_hard_deleted()
    {
        await SeedFixtureAsync();
        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.Remove(await ctx.Teachers.SingleAsync(t => t.Id == _seedTeacherAId)); // soft delete
            await ctx.SaveChangesAsync();
        }
        await using var ctx2 = _db.NewContext();

        var r = await NewService(ctx2, new FakeAuthApi()).RunAsync(new SeedCleanupOptions { Apply = true });

        r.TeachersDeleted.ShouldBe(3);
        (await _db.NewContext().Teachers.IgnoreQueryFilters().AnyAsync(t => t.Id == _seedTeacherAId)).ShouldBeFalse();
    }

    [Fact]
    public async Task Auth_api_failure_is_reported_exam_side_is_kept_and_rerun_only_needs_auth()
    {
        await SeedFixtureAsync();
        var authApi = new FakeAuthApi { Throw = new TeacherSeedAuthApiException("auth-api'ye ulaşılamadı") };
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx, authApi).RunAsync(new SeedCleanupOptions { Apply = true });

        r.TeachersDeleted.ShouldBe(3);
        r.AuthApiCalled.ShouldBeFalse();
        r.AuthApiError.ShouldContain("ulaşılamadı");
        r.Errors.ShouldContain(e => e.Contains("auth-api"));
        (await CountsAsync()).Teachers.ShouldBe(5);
        // Tutor ili e-postasız → "?"
        r.TeacherGroups.Single(g => g.Tutors == 1).Province.ShouldBe("?");
    }

    [Fact]
    public async Task Skip_auth_api_option_never_calls_it()
    {
        await SeedFixtureAsync();
        var authApi = new FakeAuthApi();
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx, authApi).RunAsync(new SeedCleanupOptions { Apply = true, SkipAuthApi = true });

        authApi.Requests.ShouldBeEmpty();
        r.AuthApiCalled.ShouldBeFalse();
        r.AuthApiError.ShouldBeNull();
        r.TeachersDeleted.ShouldBe(3);
    }

    [Fact]
    public async Task Nothing_seeded_gives_all_zero_and_touches_nothing()
    {
        await using (var ctx0 = _db.NewContext())
        {
            ctx0.Schools.Add(new School { Name = "Gerçek", IsSeedData = false });
            ctx0.Teachers.Add(new Teacher { UserId = 5, IsSeedData = false });
            await ctx0.SaveChangesAsync();
        }
        var authApi = new FakeAuthApi { Handler = r => new DevSeedCleanupResponse { DryRun = r.DryRun } };
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx, authApi).RunAsync(new SeedCleanupOptions { Apply = true, Force = true });

        r.SeedSchools.ShouldBe(0);
        r.TeachersDeleted.ShouldBe(0);
        r.SchoolsDeleted.ShouldBe(0);
        (await CountsAsync()).ShouldSatisfyAllConditions(c => c.Teachers.ShouldBe(1), c => c.Schools.ShouldBe(1));
    }

    public void Dispose() => _db.Dispose();
}
