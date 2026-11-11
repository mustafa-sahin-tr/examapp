using ExamApp.Api.Data;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #1115: öğrencilerinden biri ücretli planlıysa öğretmen Teacher Essential'ı ücretsiz kullanır — okul / randevu / doğrudan atama
/// yolları, deneme sayılmaz, 14 gün grace ve yeniden uygunluk, ücretli Pro kişisel planın önceliği, okul lisansıyla birlikte. SQLite.
/// </summary>
public class TeacherSponsorshipJobTests : IDisposable
{
    private const int AdminUser = 1;
    private const int SchoolTeacherUser = 9301;
    private const int BookedTeacherUser = 9302;
    private const int AssigningTeacherUser = 9303;
    private const int StrangerTeacherUser = 9304;
    private const int StudentUser = 9311;

    private readonly TestDb _db = TestDb.Create();
    private readonly MutableClock _clock = new(new DateTime(2027, 2, 1, 3, 0, 0, DateTimeKind.Utc));
    private int _studentId, _schoolId, _bookedTeacherId;

    public TeacherSponsorshipJobTests()
    {
        using var ctx = _db.NewContext();
        var school = new School { Name = "Okul" };
        var other = new School { Name = "Başka" };
        ctx.AddRange(school, other);
        ctx.SaveChanges();
        _schoolId = school.Id;
        var booked = new Teacher { UserId = BookedTeacherUser, AccountApprovedAt = Now };
        ctx.AddRange(
            new Teacher { UserId = SchoolTeacherUser, SchoolId = school.Id, AccountApprovedAt = Now },
            booked,
            new Teacher { UserId = AssigningTeacherUser, AccountApprovedAt = Now },
            new Teacher { UserId = StrangerTeacherUser, SchoolId = other.Id, AccountApprovedAt = Now });
        var student = new Student { UserId = StudentUser, StudentNumber = "s1", SchoolId = school.Id, SchoolVerifiedAt = Now };
        ctx.Students.Add(student);
        ctx.SaveChanges();
        _studentId = student.Id;
        _bookedTeacherId = booked.Id;
    }

    public void Dispose() => _db.Dispose();

    private DateTime Now => _clock.Now;

    private async Task<TeacherSponsorshipRunResult> RunAsync()
    {
        await using var ctx = _db.NewContext();
        return await new TeacherSponsorshipJob(ctx, _clock, Options.Create(new TeacherSponsorshipOptions()),
            NullLogger<TeacherSponsorshipJob>.Instance).RunAsync();
    }

    private async Task<ResolvedPlan> ResolveAsync(int userId)
    {
        await using var ctx = _db.NewContext();
        return await new EntitlementService(ctx, _clock).ResolvePlanAsync(userId);
    }

    private async Task AssignAsync(int userId, string planKey, DateTime? validTo = null)
    {
        await using var ctx = _db.NewContext();
        (await new EntitlementService(ctx, _clock).AssignPlanAsync(userId, planKey, null, validTo, AdminUser)).ShouldBe(AssignPlanError.None);
    }

    private async Task<List<TeacherSponsorship>> RowsAsync()
    {
        await using var ctx = _db.NewContext();
        return await ctx.TeacherSponsorships.OrderBy(x => x.Id).ToListAsync();
    }

    [Fact]
    public async Task A_trial_student_sponsors_nobody_and_a_paid_one_sponsors_the_school_teacher()
    {
        await AssignAsync(StudentUser, SubscriptionPlan.TrialKey);
        (await RunAsync()).Started.ShouldBe(0);

        await AssignAsync(StudentUser, SubscriptionPlan.StudentEssentialKey);
        (await RunAsync()).ShouldBe(new TeacherSponsorshipRunResult(1, 0, 0, 0));

        (await ResolveAsync(SchoolTeacherUser)).ShouldSatisfyAllConditions(
            p => p.Key.ShouldBe(SubscriptionPlan.TeacherEssentialKey),
            p => p.FromStudentSponsorship.ShouldBeTrue(),
            p => p.FromSchoolLicense.ShouldBeFalse(),
            p => p.ValidToUtc.ShouldBeNull());
        (await ResolveAsync(StrangerTeacherUser)).Key.ShouldBe(SubscriptionPlan.FreeKey);

        // Tekrar koşmak yeni satır açmaz.
        (await RunAsync()).ShouldBe(new TeacherSponsorshipRunResult(0, 0, 0, 0));
        (await RowsAsync()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Approved_booking_and_a_direct_assignment_also_make_the_student_theirs()
    {
        await using (var ctx = _db.NewContext())
        {
            BookingSeed.Add(ctx, _bookedTeacherId, _studentId, BookingStatus.Approved, 10);
            ctx.SetCurrentUser(AssigningTeacherUser);
            var grade = new Grade { Name = "5" };
            ctx.Add(grade);
            await ctx.SaveChangesAsync();
            var worksheet = new Worksheet { Name = "Ödev", Description = "", GradeId = grade.Id };
            ctx.Worksheets.Add(worksheet);
            await ctx.SaveChangesAsync();
            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = worksheet.Id, StudentId = _studentId, StartAt = Now.AddDays(-1), EndAt = Now.AddDays(7)
            });
            await ctx.SaveChangesAsync();
        }
        await AssignAsync(StudentUser, SubscriptionPlan.StudentProKey);

        (await RunAsync()).Started.ShouldBe(3);
        (await RowsAsync()).Select(r => r.TeacherUserId).Order()
            .ShouldBe([SchoolTeacherUser, BookedTeacherUser, AssigningTeacherUser]);
    }

    [Fact]
    public async Task Losing_the_last_paid_student_starts_a_grace_that_ends_after_14_days_unless_they_requalify()
    {
        await AssignAsync(StudentUser, SubscriptionPlan.StudentEssentialKey, validTo: Now.AddDays(10));
        await RunAsync();

        _clock.Now = Now.AddDays(11);
        (await RunAsync()).GraceStarted.ShouldBe(1);
        (await ResolveAsync(SchoolTeacherUser)).ShouldSatisfyAllConditions(
            p => p.Key.ShouldBe(SubscriptionPlan.TeacherEssentialKey), p => p.ValidToUtc.ShouldBe(Now.AddDays(14)));

        // Grace içinde yeniden uygun: grace temizlenir, aynı satır sürer.
        await AssignAsync(StudentUser, SubscriptionPlan.StudentEssentialKey, validTo: Now.AddDays(3));
        (await RunAsync()).Renewed.ShouldBe(1);
        (await ResolveAsync(SchoolTeacherUser)).ValidToUtc.ShouldBeNull();

        _clock.Now = Now.AddDays(4);
        (await RunAsync()).GraceStarted.ShouldBe(1);
        _clock.Now = Now.AddDays(14);
        // Grace bitti: iş kapatmadan önce de plan düşer.
        (await ResolveAsync(SchoolTeacherUser)).Key.ShouldBe(SubscriptionPlan.FreeKey);
        (await RunAsync()).Ended.ShouldBe(1);

        var rows = await RowsAsync();
        rows.Single().EndedAtUtc.ShouldBe(Now);
        rows.Single().StartedAtUtc.ShouldBe(new DateTime(2027, 2, 1, 3, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task A_paid_pro_plan_wins_and_a_lower_paid_plan_is_lifted_to_essential()
    {
        await AssignAsync(StudentUser, SubscriptionPlan.StudentEssentialKey);
        await RunAsync();

        await AssignAsync(SchoolTeacherUser, SubscriptionPlan.TeacherBasicKey);
        (await ResolveAsync(SchoolTeacherUser)).ShouldSatisfyAllConditions(
            p => p.Key.ShouldBe(SubscriptionPlan.TeacherEssentialKey), p => p.FromStudentSponsorship.ShouldBeTrue());

        await AssignAsync(SchoolTeacherUser, SubscriptionPlan.TeacherProKey);
        (await ResolveAsync(SchoolTeacherUser)).ShouldSatisfyAllConditions(
            p => p.Key.ShouldBe(SubscriptionPlan.TeacherProKey), p => p.FromStudentSponsorship.ShouldBeFalse());
    }

    [Fact]
    public async Task A_school_license_above_essential_wins_over_the_sponsorship()
    {
        await AssignAsync(StudentUser, SubscriptionPlan.StudentEssentialKey);
        await RunAsync();
        await using (var ctx = _db.NewContext())
        {
            (await new SchoolLicenseService(ctx, _clock).SaveAsync(_schoolId, new ExamApp.Api.Models.Dtos.SaveSchoolLicenseDto(), AdminUser))
                .ShouldBe(SchoolLicenseError.None);
        }

        (await ResolveAsync(SchoolTeacherUser)).ShouldSatisfyAllConditions(
            p => p.Key.ShouldBe(SubscriptionPlan.TeacherProKey), p => p.FromSchoolLicense.ShouldBeTrue());
    }

    private sealed class MutableClock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    }
}
