using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #1092: okul lisansı — doğrulanmış üye atamasız lisans planını alır, kişisel plan yalnız üst kademedeyse kazanır, okuldan
/// ayrılan ve lisans bitince üye kendi planına döner; lisans kaydı ve audit. SQLite (gerçek ilişkisel davranış).
/// </summary>
public class SchoolLicenseTests : IDisposable
{
    private const int AdminUser = 1;
    private const int TeacherUser = 9101;
    private const int StudentUser = 9102;
    private const int PendingStudentUser = 9103;

    private readonly TestDb _db = TestDb.Create();
    private readonly MutableClock _clock = new(new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc));

    public void Dispose() => _db.Dispose();

    private DateTime Now => _clock.Now;

    private async Task<(int Licensed, int Other)> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var licensed = new School { Name = "Lisanslı" };
        var other = new School { Name = "Diğer" };
        ctx.AddRange(licensed, other);
        await ctx.SaveChangesAsync();
        ctx.AddRange(
            new Teacher { UserId = TeacherUser, SchoolId = licensed.Id, AccountApprovedAt = Now },
            new Student { UserId = StudentUser, StudentNumber = "s1", SchoolId = licensed.Id, SchoolVerifiedAt = Now },
            // Onay bekleyen üyelik okul kapsamı vermez (#361): lisans da vermez.
            new Student { UserId = PendingStudentUser, StudentNumber = "s2", SchoolId = licensed.Id });
        await ctx.SaveChangesAsync();
        return (licensed.Id, other.Id);
    }

    private async Task<SchoolLicenseError> SaveAsync(int schoolId, SaveSchoolLicenseDto request)
    {
        await using var ctx = _db.NewContext();
        return await new SchoolLicenseService(ctx, _clock).SaveAsync(schoolId, request, AdminUser);
    }

    private async Task<ResolvedPlan> ResolveAsync(int userId)
    {
        await using var ctx = _db.NewContext();
        return await new EntitlementService(ctx, _clock).ResolvePlanAsync(userId);
    }

    private async Task AssignAsync(int userId, string planKey)
    {
        await using var ctx = _db.NewContext();
        (await new EntitlementService(ctx, _clock).AssignPlanAsync(userId, planKey, null, null, AdminUser)).ShouldBe(AssignPlanError.None);
    }

    [Fact]
    public async Task Verified_members_get_their_audiences_pro_without_an_assignment()
    {
        var (school, _) = await SeedAsync();
        (await SaveAsync(school, new SaveSchoolLicenseDto { ValidToUtc = Now.AddMonths(12), Note = "Sözleşme 42" })).ShouldBe(SchoolLicenseError.None);

        (await ResolveAsync(TeacherUser)).ShouldSatisfyAllConditions(
            p => p.Key.ShouldBe(SubscriptionPlan.TeacherProKey), p => p.FromSchoolLicense.ShouldBeTrue(), p => p.ValidToUtc.ShouldBe(Now.AddMonths(12)));
        (await ResolveAsync(StudentUser)).Key.ShouldBe(SubscriptionPlan.StudentProKey);
        (await ResolveAsync(PendingStudentUser)).Key.ShouldBe(SubscriptionPlan.FreeKey);

        // Limitler lisans planından gelir.
        await using var ctx = _db.NewContext();
        var check = await new EntitlementService(ctx, _clock).CheckAsync(EntitlementSubject.Create(StudentUser), EntitlementFeatures.MockExam);
        check.PlanKey.ShouldBe(SubscriptionPlan.StudentProKey);
    }

    [Fact]
    public async Task A_student_who_joins_later_gets_the_license_and_one_who_leaves_drops_it()
    {
        var (school, other) = await SeedAsync();
        await SaveAsync(school, new SaveSchoolLicenseDto());

        await using (var ctx = _db.NewContext())
        {
            var pending = await ctx.Students.SingleAsync(s => s.UserId == PendingStudentUser);
            pending.SchoolVerifiedAt = Now;
            var member = await ctx.Students.SingleAsync(s => s.UserId == StudentUser);
            member.SchoolId = other;
            await ctx.SaveChangesAsync();
        }

        (await ResolveAsync(PendingStudentUser)).Key.ShouldBe(SubscriptionPlan.StudentProKey);
        (await ResolveAsync(StudentUser)).Key.ShouldBe(SubscriptionPlan.FreeKey);
    }

    [Fact]
    public async Task A_personal_plan_wins_only_when_it_is_a_higher_tier()
    {
        var (school, _) = await SeedAsync();
        await SaveAsync(school, new SaveSchoolLicenseDto { StudentPlanKey = SubscriptionPlan.StudentEssentialKey });

        await AssignAsync(StudentUser, SubscriptionPlan.StudentBasicKey);
        (await ResolveAsync(StudentUser)).ShouldSatisfyAllConditions(
            p => p.Key.ShouldBe(SubscriptionPlan.StudentEssentialKey), p => p.FromSchoolLicense.ShouldBeTrue());

        await AssignAsync(StudentUser, SubscriptionPlan.StudentProKey);
        (await ResolveAsync(StudentUser)).ShouldSatisfyAllConditions(
            p => p.Key.ShouldBe(SubscriptionPlan.StudentProKey), p => p.FromSchoolLicense.ShouldBeFalse());

        // Deneme her ücretli plandan aşağıdadır.
        await AssignAsync(TeacherUser, SubscriptionPlan.TrialKey);
        (await ResolveAsync(TeacherUser)).Key.ShouldBe(SubscriptionPlan.TeacherProKey);
    }

    [Fact]
    public async Task Ending_the_license_returns_members_to_their_own_plan_or_free()
    {
        var (school, _) = await SeedAsync();
        await SaveAsync(school, new SaveSchoolLicenseDto());
        await AssignAsync(TeacherUser, SubscriptionPlan.TeacherBasicKey);

        await using (var ctx = _db.NewContext())
            (await new SchoolLicenseService(ctx, _clock).EndAsync(school, AdminUser)).ShouldBe(SchoolLicenseError.None);

        (await ResolveAsync(TeacherUser)).Key.ShouldBe(SubscriptionPlan.TeacherBasicKey);
        (await ResolveAsync(StudentUser)).Key.ShouldBe(SubscriptionPlan.FreeKey);
        await using (var ctx = _db.NewContext())
            (await new SchoolLicenseService(ctx, _clock).EndAsync(school, AdminUser)).ShouldBe(SchoolLicenseError.NoLicense);
    }

    [Fact]
    public async Task Admin_user_list_resolves_the_license_like_the_single_lookup()
    {
        var (school, _) = await SeedAsync();
        await SaveAsync(school, new SaveSchoolLicenseDto { StudentPlanKey = SubscriptionPlan.StudentEssentialKey });
        await AssignAsync(TeacherUser, SubscriptionPlan.TeacherBasicKey);

        await using var ctx = _db.NewContext();
        var service = new AdminSubscriptionService(ctx, new EntitlementService(ctx, _clock),
            Substitute.For<ExamApp.Api.Services.Interfaces.IAuthApiClient>(), _clock);
        var plans = await service.CurrentPlansAsync([TeacherUser, StudentUser, PendingStudentUser]);

        plans[TeacherUser].Key.ShouldBe(SubscriptionPlan.TeacherProKey);
        plans[StudentUser].ShouldSatisfyAllConditions(
            p => p.Key.ShouldBe(SubscriptionPlan.StudentEssentialKey), p => p.FromSchoolLicense.ShouldBeTrue());
        plans[PendingStudentUser].Key.ShouldBe(SubscriptionPlan.FreeKey);
    }

    [Fact]
    public async Task A_license_applies_only_inside_its_dates()
    {
        var (school, _) = await SeedAsync();
        await SaveAsync(school, new SaveSchoolLicenseDto { ValidFromUtc = Now.AddDays(2), ValidToUtc = Now.AddDays(10) });
        (await ResolveAsync(StudentUser)).Key.ShouldBe(SubscriptionPlan.FreeKey);

        _clock.Now = Now.AddDays(3);
        (await ResolveAsync(StudentUser)).Key.ShouldBe(SubscriptionPlan.StudentProKey);

        _clock.Now = Now.AddDays(8);
        (await ResolveAsync(StudentUser)).Key.ShouldBe(SubscriptionPlan.FreeKey);
    }

    [Fact]
    public async Task Save_extends_the_same_row_and_every_change_is_audited()
    {
        var (school, _) = await SeedAsync();
        await SaveAsync(school, new SaveSchoolLicenseDto { ValidToUtc = Now.AddMonths(6), Note = "  Sözleşme 42 " });
        await SaveAsync(school, new SaveSchoolLicenseDto { ValidToUtc = Now.AddMonths(12), Note = "Ek protokol" });
        await using (var ctx = _db.NewContext())
            await new SchoolLicenseService(ctx, _clock).EndAsync(school, AdminUser);

        await using var read = _db.NewContext();
        (await read.SchoolLicenses.CountAsync()).ShouldBe(1);
        var license = await new SchoolLicenseService(read, _clock).GetAsync(school);
        license!.ShouldSatisfyAllConditions(
            l => l.Exists.ShouldBeTrue(), l => l.Active.ShouldBeFalse(), l => l.ValidToUtc.ShouldBe(Now),
            l => l.TeacherPlanKey.ShouldBe(SubscriptionPlan.TeacherProKey), l => l.Note.ShouldBe("Ek protokol"));
        license.History.Select(h => h.Action).ShouldBe(["Ended", "Updated", "Created"], ignoreOrder: true);
        license.History.Single(h => h.Action == "Created").Note.ShouldBe("Sözleşme 42");
        license.History.ShouldAllBe(h => h.ActorUserId == AdminUser);
    }

    [Theory]
    [InlineData(SubscriptionPlan.StudentProKey, null, SchoolLicenseError.AudienceMismatch)]
    [InlineData(null, SubscriptionPlan.TeacherProKey, SchoolLicenseError.AudienceMismatch)]
    [InlineData(SubscriptionPlan.TrialKey, null, SchoolLicenseError.UnknownPlan)]
    [InlineData(null, SubscriptionPlan.FreeKey, SchoolLicenseError.UnknownPlan)]
    [InlineData("nope", null, SchoolLicenseError.UnknownPlan)]
    public async Task Save_rejects_plans_that_cannot_be_a_license(string? teacherPlan, string? studentPlan, SchoolLicenseError expected)
    {
        var (school, _) = await SeedAsync();
        (await SaveAsync(school, new SaveSchoolLicenseDto { TeacherPlanKey = teacherPlan, StudentPlanKey = studentPlan })).ShouldBe(expected);
    }

    [Fact]
    public async Task Save_validates_school_dates_and_note()
    {
        var (school, _) = await SeedAsync();
        (await SaveAsync(99_999, new SaveSchoolLicenseDto())).ShouldBe(SchoolLicenseError.SchoolNotFound);
        (await SaveAsync(school, new SaveSchoolLicenseDto { ValidToUtc = Now.AddDays(-1) })).ShouldBe(SchoolLicenseError.InvalidRange);
        (await SaveAsync(school, new SaveSchoolLicenseDto { ValidFromUtc = Now.AddDays(5), ValidToUtc = Now.AddDays(5) }))
            .ShouldBe(SchoolLicenseError.InvalidRange);
        (await SaveAsync(school, new SaveSchoolLicenseDto { Note = new string('x', SchoolLicense.NoteMaxLength + 1) }))
            .ShouldBe(SchoolLicenseError.NoteTooLong);

        await using var ctx = _db.NewContext();
        (await new SchoolLicenseService(ctx, _clock).GetAsync(99_999)).ShouldBeNull();
        (await new SchoolLicenseService(ctx, _clock).GetAsync(school))!.Exists.ShouldBeFalse();
    }

    private sealed class MutableClock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    }
}
