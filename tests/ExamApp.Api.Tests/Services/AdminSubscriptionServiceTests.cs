using System.Reflection;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #1030: admin plan ataması — audit (eski → yeni), atamayı bitirme, okula toplu atama, arama ve geçmiş.</summary>
public class AdminSubscriptionServiceTests : IDisposable
{
    private const int AdminId = 1;
    private const int UserId = 41;

    private readonly TestDb _db = TestDb.Create();
    private readonly MutableClock _clock = new(new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc));
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    private EntitlementService Entitlements(AppDbContext ctx) => new(ctx, _clock);

    private AdminSubscriptionService NewService(AppDbContext ctx) => new(ctx, Entitlements(ctx), _authApi, _clock);

    private async Task<List<SubscriptionPlanAssignmentAudit>> AuditsAsync()
    {
        await using var ctx = _db.NewContext();
        return await ctx.SubscriptionPlanAssignmentAudits.AsNoTracking().OrderBy(a => a.Id).ToListAsync();
    }

    [Fact]
    public async Task Every_assignment_writes_an_audit_row_with_old_and_new_plan()
    {
        await using (var ctx = _db.NewContext())
            (await Entitlements(ctx).AssignPlanAsync(UserId, SubscriptionPlan.TeacherProKey, null, null, AdminId)).ShouldBe(AssignPlanError.None);
        _clock.Now = _clock.Now.AddDays(1);
        var end = _clock.Now.AddMonths(1);
        await using (var ctx = _db.NewContext())
            (await Entitlements(ctx).AssignPlanAsync(UserId, SubscriptionPlan.StudentProKey, null, end, AdminId)).ShouldBe(AssignPlanError.None);

        var audits = await AuditsAsync();
        audits.Count.ShouldBe(2);
        audits[0].ShouldSatisfyAllConditions(
            a => a.Action.ShouldBe(SubscriptionPlanAuditAction.Assigned),
            a => a.FromPlanKey.ShouldBeNull(),
            a => a.ToPlanKey.ShouldBe(SubscriptionPlan.TeacherProKey),
            a => a.ActorUserId.ShouldBe(AdminId),
            a => a.SchoolId.ShouldBeNull());
        audits[1].ShouldSatisfyAllConditions(
            a => a.FromPlanKey.ShouldBe(SubscriptionPlan.TeacherProKey),
            a => a.ToPlanKey.ShouldBe(SubscriptionPlan.StudentProKey),
            a => a.ValidFromUtc.ShouldBe(_clock.Now),
            a => a.ValidToUtc.ShouldBe(end),
            a => a.OccurredAtUtc.ShouldBe(_clock.Now));
    }

    [Fact]
    public async Task A_rejected_assignment_writes_no_audit()
    {
        await using (var ctx = _db.NewContext())
        {
            (await Entitlements(ctx).AssignPlanAsync(UserId, "nope", null, null, AdminId)).ShouldBe(AssignPlanError.UnknownPlan);
            (await Entitlements(ctx).AssignPlanAsync(UserId, SubscriptionPlan.TeacherProKey, _clock.Now, _clock.Now, AdminId))
                .ShouldBe(AssignPlanError.InvalidRange);
        }

        (await AuditsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Ending_closes_the_active_plan_cancels_scheduled_ones_and_is_audited()
    {
        await using (var ctx = _db.NewContext())
        {
            await Entitlements(ctx).AssignPlanAsync(UserId, SubscriptionPlan.TeacherProKey, _clock.Now.AddDays(-3), null, AdminId);
        }
        await using (var ctx = _db.NewContext())
            await Entitlements(ctx).AssignPlanAsync(UserId, SubscriptionPlan.StudentProKey, _clock.Now.AddDays(10), null, AdminId);

        await using (var ctx = _db.NewContext())
            (await Entitlements(ctx).EndPlanAsync(UserId, AdminId)).ShouldBeTrue();

        await using (var ctx = _db.NewContext())
        {
            (await Entitlements(ctx).ResolvePlanAsync(UserId)).Key.ShouldBe(SubscriptionPlan.FreeKey);
            _clock.Now = _clock.Now.AddDays(11);
            (await Entitlements(ctx).ResolvePlanAsync(UserId)).Key.ShouldBe(SubscriptionPlan.FreeKey); // scheduled one cancelled
            (await Entitlements(ctx).EndPlanAsync(UserId, AdminId)).ShouldBeFalse();
        }

        var ended = (await AuditsAsync()).Last();
        ended.Action.ShouldBe(SubscriptionPlanAuditAction.Ended);
        ended.FromPlanKey.ShouldBe(SubscriptionPlan.TeacherProKey);
        ended.ToPlanKey.ShouldBeNull();
    }

    private async Task<int> SeedSchoolAsync()
    {
        await using var ctx = _db.NewContext();
        var school = new School { Name = "A" };
        var other = new School { Name = "B" };
        var grade = new Grade { Name = "5" };
        ctx.AddRange(school, other, grade);
        await ctx.SaveChangesAsync();
        ctx.Teachers.AddRange(
            new Teacher { UserId = 101, SchoolId = school.Id },
            new Teacher { UserId = 102, RequestedSchoolId = school.Id }, // başvurusu bekliyor
            new Teacher { UserId = 103, SchoolId = other.Id });
        ctx.Students.AddRange(
            new Student { UserId = 201, StudentNumber = "s1", SchoolId = school.Id, SchoolVerifiedAt = _clock.Now, GradeId = grade.Id },
            new Student { UserId = 202, StudentNumber = "s2", SchoolId = school.Id, GradeId = grade.Id }); // doğrulanmamış
        await ctx.SaveChangesAsync();
        return school.Id;
    }

    private static AssignSchoolSubscriptionPlanDto SchoolRequest(string planKey, SchoolPlanAudience audience) =>
        new() { PlanKey = planKey, Audience = audience };

    [Fact]
    public async Task School_members_are_linked_teachers_and_verified_students()
    {
        var schoolId = await SeedSchoolAsync();
        await using var ctx = _db.NewContext();

        var members = (await NewService(ctx).GetSchoolMembersAsync(schoolId)).ShouldNotBeNull();

        members.Teachers.ShouldBe(1);
        members.Students.ShouldBe(1);
        (await NewService(ctx).GetSchoolMembersAsync(9999)).ShouldBeNull();
    }

    [Fact]
    public async Task Bulk_assign_gives_every_member_the_plan_with_one_audit_row_each()
    {
        var schoolId = await SeedSchoolAsync();
        var end = _clock.Now.AddMonths(12);

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).AssignSchoolAsync(schoolId,
                new AssignSchoolSubscriptionPlanDto { PlanKey = SubscriptionPlan.TrialKey, Audience = SchoolPlanAudience.All, ValidToUtc = end }, AdminId))
                .ShouldBe((AdminSchoolAssignError.None, 2));

        await using (var ctx = _db.NewContext())
        {
            foreach (var userId in new[] { 101, 201 })
                (await Entitlements(ctx).ResolvePlanAsync(userId)).ShouldSatisfyAllConditions(
                    p => p.Key.ShouldBe(SubscriptionPlan.TrialKey), p => p.ValidToUtc.ShouldBe(end));
            foreach (var userId in new[] { 102, 103, 202 })
                (await Entitlements(ctx).ResolvePlanAsync(userId)).Key.ShouldBe(SubscriptionPlan.FreeKey);
        }

        var audits = await AuditsAsync();
        audits.Select(a => a.UserId).OrderBy(id => id).ShouldBe(new[] { 101, 201 });
        audits.ShouldAllBe(a => a.SchoolId == schoolId && a.ActorUserId == AdminId && a.ToPlanKey == SubscriptionPlan.TrialKey);
    }

    [Fact]
    public async Task Bulk_assign_to_one_audience_leaves_the_other_alone()
    {
        var schoolId = await SeedSchoolAsync();

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).AssignSchoolAsync(schoolId, SchoolRequest(SubscriptionPlan.TeacherProKey, SchoolPlanAudience.Teachers), AdminId))
                .ShouldBe((AdminSchoolAssignError.None, 1));

        (await AuditsAsync()).ShouldHaveSingleItem().UserId.ShouldBe(101);
    }

    [Theory]
    [InlineData(SubscriptionPlan.TeacherProKey, SchoolPlanAudience.Students)]
    [InlineData(SubscriptionPlan.TeacherProKey, SchoolPlanAudience.All)]
    [InlineData(SubscriptionPlan.StudentProKey, SchoolPlanAudience.Teachers)]
    public async Task Bulk_assign_rejects_a_plan_for_the_other_audience(string planKey, SchoolPlanAudience audience)
    {
        var schoolId = await SeedSchoolAsync();
        await using var ctx = _db.NewContext();

        (await NewService(ctx).AssignSchoolAsync(schoolId, SchoolRequest(planKey, audience), AdminId))
            .Error.ShouldBe(AdminSchoolAssignError.AudienceMismatch);
        (await AuditsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Bulk_assign_reports_unknown_school_plan_and_empty_school()
    {
        await using var ctx = _db.NewContext();
        var empty = new School { Name = "Empty" };
        ctx.Schools.Add(empty);
        await ctx.SaveChangesAsync();
        var service = NewService(ctx);

        (await service.AssignSchoolAsync(9999, SchoolRequest(SubscriptionPlan.TrialKey, SchoolPlanAudience.All), AdminId))
            .Error.ShouldBe(AdminSchoolAssignError.SchoolNotFound);
        (await service.AssignSchoolAsync(empty.Id, SchoolRequest("nope", SchoolPlanAudience.All), AdminId))
            .Error.ShouldBe(AdminSchoolAssignError.UnknownPlan);
        (await service.AssignSchoolAsync(empty.Id, SchoolRequest(SubscriptionPlan.TrialKey, SchoolPlanAudience.All), AdminId))
            .Error.ShouldBe(AdminSchoolAssignError.NoMembers);
    }

    [Fact]
    public async Task Search_masks_emails_and_shows_the_current_plan_or_free()
    {
        await using (var ctx = _db.NewContext())
            await Entitlements(ctx).AssignPlanAsync(UserId, SubscriptionPlan.TeacherProKey, null, null, AdminId);
        _authApi.SearchUsersAsync("ay", "Teacher", Arg.Any<CancellationToken>()).Returns(new List<UserLookupResultDto>
        {
            new() { Id = UserId, FullName = "Ayşe", Email = "ayse@okul.k12.tr", Role = "Teacher" },
            new() { Id = 42, FullName = "Ayhan", Email = "ayhan@okul.k12.tr", Role = "Teacher" },
        });

        await using var check = _db.NewContext();
        var users = await NewService(check).SearchAsync("ay", "Teacher");

        users.Select(u => u.Email).ShouldBe(new[] { EmailMask.Apply("ayse@okul.k12.tr"), EmailMask.Apply("ayhan@okul.k12.tr") });
        users.ShouldNotContain(u => u.Email.Contains("ayse@"));
        users[0].PlanKey.ShouldBe(SubscriptionPlan.TeacherProKey);
        users[1].PlanKey.ShouldBe(SubscriptionPlan.FreeKey);
    }

    [Fact]
    public async Task Detail_lists_assignments_and_changes_with_actor_names()
    {
        await using (var ctx = _db.NewContext())
            await Entitlements(ctx).AssignPlanAsync(UserId, SubscriptionPlan.TrialKey, null, _clock.Now.AddDays(14), assignedByUserId: UserId); // self (trial)
        _clock.Now = _clock.Now.AddDays(1);
        await using (var ctx = _db.NewContext())
            await Entitlements(ctx).AssignPlanAsync(UserId, SubscriptionPlan.TeacherProKey, null, null, AdminId);
        _authApi.GetUsersByIdsOrThrowAsync(Arg.Is<IEnumerable<int>>(ids => ids.Single() == UserId), Arg.Any<CancellationToken>())
            .Returns(new List<UserLookupResultDto> { new() { Id = UserId, FullName = "Ayşe", Email = "ayse@okul.k12.tr", Role = "Teacher" } });
        _authApi.GetUsersByIdsAsync(Arg.Is<IEnumerable<int>>(ids => ids.Single() == AdminId), Arg.Any<CancellationToken>())
            .Returns(new List<UserLookupResultDto> { new() { Id = AdminId, FullName = "Yönetici" } });

        await using var check = _db.NewContext();
        var detail = (await NewService(check).GetDetailAsync(UserId)).ShouldNotBeNull();

        detail.PlanKey.ShouldBe(SubscriptionPlan.TeacherProKey);
        detail.Email.ShouldBe(EmailMask.Apply("ayse@okul.k12.tr"));
        detail.Assignments.Select(a => (a.PlanKey, a.Status)).ShouldBe(new[]
        {
            (SubscriptionPlan.TeacherProKey, "Active"), (SubscriptionPlan.TrialKey, "Ended"),
        });
        detail.Changes.Select(c => (c.ToPlanKey, c.FromPlanKey, c.ActorName, c.BySelf)).ShouldBe(new[]
        {
            ((string?)SubscriptionPlan.TeacherProKey, (string?)SubscriptionPlan.TrialKey, "Yönetici", false),
            (SubscriptionPlan.TrialKey, null, "Ayşe", true),
        });
    }

    [Fact]
    public async Task Detail_of_an_unknown_user_is_null()
    {
        _authApi.GetUsersByIdsOrThrowAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>()).Returns(new List<UserLookupResultDto>());
        await using var ctx = _db.NewContext();

        (await NewService(ctx).GetDetailAsync(777)).ShouldBeNull();
    }

    [Fact]
    public void Controller_is_admin_only_and_audits_personal_data_endpoints()
    {
        var type = typeof(AdminSubscriptionPlansController);
        var roles = type.GetCustomAttributes<AuthorizeAttribute>(inherit: false).Single().Roles!.Split(',');
        roles.ShouldBe(new[] { "Admin", "SuperAdmin" }, ignoreOrder: true);
        type.GetMethods().ShouldAllBe(m => m.GetCustomAttribute<AllowAnonymousAttribute>() == null);

        type.GetMethod(nameof(AdminSubscriptionPlansController.SearchUsers))!.GetCustomAttribute<AdminDataAccessAttribute>()!
            .Resource.ShouldBe(AdminDataAccessResource.SubscriptionUserSearch);
        type.GetMethod(nameof(AdminSubscriptionPlansController.GetUserPlan))!.GetCustomAttribute<AdminDataAccessAttribute>()!
            .Resource.ShouldBe(AdminDataAccessResource.SubscriptionUserDetail);
    }

    public void Dispose() => _db.Dispose();

    private sealed class MutableClock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    }
}
