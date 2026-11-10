using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #1091: bitişine 3 gün kalan öğrenci planı / denemesi için her Active veliye bir hatırlatma event'i; atama başına bir kez,
/// velisi olmayan öğrenci işaretlenmez, yenilenmiş plan ve öğretmen planı hatırlatılmaz. SQLite (gerçek ilişkisel davranış).
/// </summary>
public class ParentPlanEndingReminderJobTests : IDisposable
{
    private const int StudentUser = 8301;
    private const int TeacherUser = 8302;
    private const int ParentUserA = 8311;
    private const int ParentUserB = 8312;

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();
    private readonly FixedTimeProvider _time = new(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));

    public ParentPlanEndingReminderJobTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IEnumerable<int>>().Select(id => new UserLookupResultDto
            {
                Id = id, KeycloakId = $"kc-{id}", FullName = id == StudentUser ? "Ayşe Kaya" : $"Veli {id}", Email = $"u{id}@example.com"
            }).ToList());
    }

    public void Dispose() => _db.Dispose();

    private DateTime Now => _time.Now.UtcDateTime;

    private async Task<(int StudentId, int ParentA, int ParentB)> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var student = new Student { UserId = StudentUser, StudentNumber = "s1" };
        var a = new Parent { UserId = ParentUserA };
        var b = new Parent { UserId = ParentUserB };
        ctx.AddRange(student, a, b);
        await ctx.SaveChangesAsync();
        return (student.Id, a.Id, b.Id);
    }

    private async Task LinkAsync(int parentId, int studentId, ParentStudentLinkStatus status = ParentStudentLinkStatus.Active)
    {
        await using var ctx = _db.NewContext();
        ctx.ParentStudentLinks.Add(new ParentStudentLink
        {
            Origin = ParentStudentLinkOrigin.ParentCreated, ParentId = parentId, StudentId = studentId, Status = status,
            CreatedAt = Now.AddDays(-30), ActivatedAt = status == ParentStudentLinkStatus.Pending ? null : Now.AddDays(-30),
        });
        await ctx.SaveChangesAsync();
    }

    private async Task<int> AssignAsync(int userId, string planKey, DateTime from, DateTime? to)
    {
        await using var ctx = _db.NewContext();
        var planId = await ctx.SubscriptionPlans.Where(p => p.Key == planKey).Select(p => p.Id).SingleAsync();
        var a = new UserSubscriptionPlan { UserId = userId, PlanId = planId, ValidFromUtc = from, ValidToUtc = to };
        ctx.UserSubscriptionPlans.Add(a);
        await ctx.SaveChangesAsync();
        return a.Id;
    }

    private async Task<int> RunAsync()
    {
        await using var ctx = _db.NewContext();
        return await new ParentPlanEndingReminderJob(ctx, _authApi,
            new StaticOptionsMonitor<ParentPlanEndingReminderOptions>(new ParentPlanEndingReminderOptions()), _time).RunAsync();
    }

    private async Task<List<ParentChildPlanEndingEvent>> EventsAsync()
    {
        await using var ctx = _db.NewContext();
        var type = OutboxEventRegistry.NameFor<ParentChildPlanEndingEvent>();
        return (await ctx.OutboxMessages.Where(o => o.Type == type).ToListAsync())
            .Select(o => JsonSerializer.Deserialize<ParentChildPlanEndingEvent>(o.Content)!).ToList();
    }

    [Fact]
    public async Task Each_active_parent_is_reminded_once_three_days_before_the_trial_ends()
    {
        var w = await SeedAsync();
        await LinkAsync(w.ParentA, w.StudentId);
        await LinkAsync(w.ParentB, w.StudentId, ParentStudentLinkStatus.Pending);
        var assignmentId = await AssignAsync(StudentUser, SubscriptionPlan.TrialKey, Now.AddDays(-11), Now.AddDays(2).AddHours(5));

        (await RunAsync()).ShouldBe(1);
        (await RunAsync()).ShouldBe(0);

        var e = (await EventsAsync()).ShouldHaveSingleItem();
        e.ParentUserId.ShouldBe(ParentUserA);
        e.ParentKeycloakId.ShouldBe($"kc-{ParentUserA}");
        e.StudentId.ShouldBe(w.StudentId);
        e.StudentDisplayName.ShouldBe("Ayşe K.");
        e.AssignmentId.ShouldBe(assignmentId);
        e.IsTrial.ShouldBeTrue();
        await using var ctx = _db.NewContext();
        (await ctx.UserSubscriptionPlans.SingleAsync(a => a.Id == assignmentId)).EndingReminderSentAtUtc.ShouldBe(Now);
    }

    [Fact]
    public async Task Paid_plan_is_reminded_but_not_before_the_window()
    {
        var w = await SeedAsync();
        await LinkAsync(w.ParentA, w.StudentId);
        var assignmentId = await AssignAsync(StudentUser, SubscriptionPlan.StudentProKey, Now.AddDays(-20), Now.AddDays(4));

        (await RunAsync()).ShouldBe(0);
        _time.Now = _time.Now.AddDays(1.5);
        (await RunAsync()).ShouldBe(1);

        var e = (await EventsAsync()).ShouldHaveSingleItem();
        e.IsTrial.ShouldBeFalse();
        e.AssignmentId.ShouldBe(assignmentId);
    }

    [Fact]
    public async Task A_student_without_a_parent_is_not_marked_so_a_later_parent_still_gets_it()
    {
        var w = await SeedAsync();
        var assignmentId = await AssignAsync(StudentUser, SubscriptionPlan.StudentBasicKey, Now.AddDays(-28), Now.AddDays(2));

        (await RunAsync()).ShouldBe(0);
        await using (var ctx = _db.NewContext())
            (await ctx.UserSubscriptionPlans.SingleAsync(a => a.Id == assignmentId)).EndingReminderSentAtUtc.ShouldBeNull();

        await LinkAsync(w.ParentA, w.StudentId);
        (await RunAsync()).ShouldBe(1);
        (await EventsAsync()).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Renewed_ended_teacher_and_open_ended_plans_are_not_reminded()
    {
        var w = await SeedAsync();
        await LinkAsync(w.ParentA, w.StudentId);
        // Önceden yenilenmiş: biten planın ardından başlayan atama var.
        var ends = Now.AddDays(2);
        await AssignAsync(StudentUser, SubscriptionPlan.StudentBasicKey, Now.AddDays(-28), ends);
        await AssignAsync(StudentUser, SubscriptionPlan.StudentProKey, ends, ends.AddMonths(1));
        // Bitmiş, süresiz ve öğretmen planları.
        await AssignAsync(TeacherUser, SubscriptionPlan.TeacherProKey, Now.AddDays(-28), Now.AddDays(2));

        (await RunAsync()).ShouldBe(0);
        (await EventsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_student_covered_by_the_school_license_is_marked_without_a_reminder_issue1092()
    {
        var w = await SeedAsync();
        await LinkAsync(w.ParentA, w.StudentId);
        var assignment = await AssignAsync(StudentUser, SubscriptionPlan.TrialKey, Now.AddDays(-12), Now.AddDays(2));
        await using (var ctx = _db.NewContext())
        {
            var school = new School { Name = "Lisanslı" };
            ctx.Schools.Add(school);
            await ctx.SaveChangesAsync();
            var student = await ctx.Students.SingleAsync(s => s.Id == w.StudentId);
            student.SchoolId = school.Id;
            student.SchoolVerifiedAt = Now;
            await ctx.SaveChangesAsync();
            await new SchoolLicenseService(ctx, _time).SaveAsync(school.Id, new SaveSchoolLicenseDto(), adminUserId: 1);
        }

        (await RunAsync()).ShouldBe(1);
        (await EventsAsync()).ShouldBeEmpty();
        await using var read = _db.NewContext();
        (await read.UserSubscriptionPlans.SingleAsync(a => a.Id == assignment)).EndingReminderSentAtUtc.ShouldBe(Now);
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
