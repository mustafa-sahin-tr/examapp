using System.Security.Claims;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #1090: planı olmayan öğretmen / öğrenci kilitlenir; denemeyi hiç kullanmamış olan kilitlenmez, denemesi başlar. Admin,
/// içerik ekibi ve veli muaf; anahtar kapalıyken kimse kilitlenmez.
/// </summary>
public class PlanLockServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    private static ClaimsPrincipal As(params string[] roles) =>
        new(new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)), "test"));

    private PlanLockService NewService(AppDbContext ctx, bool requirePlan = true)
    {
        var options = Substitute.For<IOptionsSnapshot<SubscriptionOptions>>();
        options.Value.Returns(new SubscriptionOptions { RequirePlan = requirePlan });
        return new PlanLockService(ctx, new EntitlementService(ctx, TimeProvider.System), options, TimeProvider.System);
    }

    private async Task UseUpTrialAsync(int userId)
    {
        await using var ctx = _db.NewContext();
        var entitlements = new EntitlementService(ctx, TimeProvider.System);
        (await entitlements.StartTrialAsync(userId, new HashSet<SubscriptionPlanAudience> { SubscriptionPlanAudience.Any }))
            .ShouldBe(SelfServicePlanError.None);
        (await entitlements.EndPlanAsync(userId, 1)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("Teacher")]
    [InlineData("Student")]
    public async Task A_user_whose_trial_ended_without_a_plan_is_locked(string role)
    {
        await UseUpTrialAsync(10);
        await using var ctx = _db.NewContext();

        (await NewService(ctx).IsLockedAsync(10, As(role))).ShouldBeTrue();
    }

    [Fact]
    public async Task A_new_user_is_not_locked_and_gets_the_trial()
    {
        await using (var ctx = _db.NewContext())
            (await NewService(ctx).IsLockedAsync(11, As("Student"))).ShouldBeFalse();

        await using var read = _db.NewContext();
        (await new EntitlementService(read, TimeProvider.System).ResolvePlanAsync(11)).Key.ShouldBe(SubscriptionPlan.TrialKey);
    }

    [Fact]
    public async Task A_paid_plan_unlocks()
    {
        await UseUpTrialAsync(12);
        await using var ctx = _db.NewContext();
        (await new EntitlementService(ctx, TimeProvider.System).AssignPlanAsync(12, SubscriptionPlan.TeacherBasicKey, null, null, 1))
            .ShouldBe(AssignPlanError.None);

        (await NewService(ctx).IsLockedAsync(12, As("Teacher"))).ShouldBeFalse();
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Parent")]
    [InlineData("Teacher", "QuestionTrainer")]
    [InlineData("Teacher", "Admin")]
    public async Task Exempt_roles_are_never_locked(params string[] roles)
    {
        await UseUpTrialAsync(13);
        await using var ctx = _db.NewContext();

        (await NewService(ctx).IsLockedAsync(13, As(roles))).ShouldBeFalse();
    }

    [Fact]
    public async Task With_the_switch_off_nobody_is_locked_and_no_trial_starts()
    {
        await UseUpTrialAsync(14);
        await using var ctx = _db.NewContext();

        (await NewService(ctx, requirePlan: false).IsLockedAsync(14, As("Teacher"))).ShouldBeFalse();
        (await NewService(ctx, requirePlan: false).IsLockedAsync(15, As("Teacher"))).ShouldBeFalse();
        (await ctx.UserSubscriptionPlans.AnyAsync(a => a.UserId == 15)).ShouldBeFalse();
        (await NewService(ctx, requirePlan: false).LockedAmongAsync([14])).ShouldBeEmpty();
    }

    [Fact]
    public async Task Locked_among_lists_only_students_whose_trial_ended_without_a_plan()
    {
        await UseUpTrialAsync(20); // kilitli
        await UseUpTrialAsync(21); // sonra plan aldı
        await using var ctx = _db.NewContext();
        await new EntitlementService(ctx, TimeProvider.System).AssignPlanAsync(21, SubscriptionPlan.StudentBasicKey, null, null, 1);
        // 22: hiç girmedi (ilk girişte deneme alacak)

        (await NewService(ctx).LockedAmongAsync([20, 21, 22])).ShouldBe(new HashSet<int> { 20 });
    }

    public void Dispose() => _db.Dispose();
}
