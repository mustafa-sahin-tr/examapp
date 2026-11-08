using System.Security.Claims;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.Tests.Controllers;

/// <summary>
/// Issue #1027: MVP plan kataloğu seed'i, rolüne göre süzülen plan listesi ve oturumdaki kullanıcının planı + kullanımı
/// (ataması olmayan kullanıcı Free, Admin sınırsız). #1028: deneme bir kez, sahte ödeme, atamanın yer değiştirmesi.
/// </summary>
public class SubscriptionPlansControllerTests : IDisposable
{
    private const int UserId = 77;

    private readonly TestDb _db = TestDb.Create();
    private readonly FixedClock _clock = new(new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc));

    [Fact]
    public async Task Catalogue_is_seeded_with_free_trial_teacher_pro_and_student_plus()
    {
        await using var ctx = _db.NewContext();

        var plans = await SubscriptionPlanCatalog.ListAsync(ctx, _ => true, CancellationToken.None);

        plans.Select(p => (p.Key, p.Audience)).ShouldBe([
            (SubscriptionPlan.FreeKey, "Any"), (SubscriptionPlan.TrialKey, "Any"),
            (SubscriptionPlan.TeacherProKey, "Teacher"), (SubscriptionPlan.StudentPlusKey, "Student")]);
        plans.ShouldAllBe(p => p.IsActive && p.Currency == "TRY" && p.DisplayNameKey.StartsWith("subscription.plans."));
        plans.Single(p => p.Key == SubscriptionPlan.TrialKey).TrialDays.ShouldBe(SubscriptionPlanSeed.DefaultTrialDays);
        plans.Where(p => p.Key != SubscriptionPlan.TrialKey).ShouldAllBe(p => p.TrialDays == null);

        Limits(plans, SubscriptionPlan.FreeKey).ShouldBe(new Dictionary<string, int?>
        {
            [EntitlementFeatures.BookImport] = 0, [EntitlementFeatures.AiClassification] = 20, [EntitlementFeatures.MockExam] = 0,
        });
        Limits(plans, SubscriptionPlan.TrialKey)[EntitlementFeatures.BookImport].ShouldBe(1);
        Limits(plans, SubscriptionPlan.TeacherProKey).ShouldBe(new Dictionary<string, int?>
        {
            [EntitlementFeatures.BookImport] = 10, [EntitlementFeatures.AiClassification] = 1000,
        });
        Limits(plans, SubscriptionPlan.StudentPlusKey).ShouldBe(new Dictionary<string, int?> { [EntitlementFeatures.MockExam] = null });
    }

    [Theory]
    [InlineData("Teacher", new[] { "free", "trial", "teacher-pro" })]
    [InlineData("Student", new[] { "free", "trial", "student-plus" })]
    [InlineData("Parent", new[] { "free", "trial" })]
    [InlineData("Admin", new[] { "free", "trial", "teacher-pro", "student-plus" })]
    public async Task List_returns_active_plans_for_the_callers_role(string role, string[] expected)
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.SubscriptionPlans.Add(new SubscriptionPlan
            {
                Key = "retired", Name = "Retired", Audience = SubscriptionPlanAudience.Any, IsActive = false, SortOrder = 9,
            });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var result = await NewController(read, role).List(CancellationToken.None);

        result.Value!.Select(p => p.Key).ShouldBe(expected);
    }

    [Fact]
    public async Task Current_resolves_an_unassigned_user_to_free_with_this_months_usage()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.EntitlementUsages.Add(new EntitlementUsage
            {
                UserId = UserId, FeatureKey = EntitlementFeatures.AiClassification, ReferenceKey = "q-1", UsedAtUtc = _clock.Now.AddDays(-1),
            });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var me = (await NewController(read, "Teacher").Current(CancellationToken.None)).Value!;

        me.PlanKey.ShouldBe(SubscriptionPlan.FreeKey);
        me.DisplayNameKey.ShouldBe("subscription.plans.free");
        me.ValidToUtc.ShouldBeNull();
        me.Features.Select(f => f.FeatureKey).ShouldBe(EntitlementFeatures.All);
        var ai = me.Features.Single(f => f.FeatureKey == EntitlementFeatures.AiClassification);
        (ai.Allowed, ai.Limit, ai.Used, ai.Remaining).ShouldBe((true, 20, 1, 19));
        ai.ResetsAtUtc.ShouldBe(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc));
        me.Features.Single(f => f.FeatureKey == EntitlementFeatures.BookImport).Allowed.ShouldBeFalse();
    }

    [Fact]
    public async Task Current_returns_the_assigned_trial_with_its_end_date()
    {
        var end = _clock.Now.AddDays(SubscriptionPlanSeed.DefaultTrialDays);
        await using (var ctx = _db.NewContext())
            (await new EntitlementService(ctx, _clock).AssignPlanAsync(UserId, SubscriptionPlan.TrialKey, null, end, 1))
                .ShouldBe(AssignPlanError.None);

        await using var read = _db.NewContext();
        var me = (await NewController(read, "Teacher").Current(CancellationToken.None)).Value!;

        me.PlanKey.ShouldBe(SubscriptionPlan.TrialKey);
        me.ValidToUtc.ShouldBe(end);
        me.Features.Single(f => f.FeatureKey == EntitlementFeatures.BookImport).Limit.ShouldBe(1);
    }

    [Fact]
    public async Task Current_is_unlimited_for_admin()
    {
        await using var read = _db.NewContext();
        var me = (await NewController(read, "Admin").Current(CancellationToken.None)).Value!;

        me.PlanKey.ShouldBe(SubscriptionPlan.FreeKey);
        me.Features.ShouldAllBe(f => f.Allowed && f.Limit == null && f.ResetsAtUtc == null);
    }

    [Fact]
    public async Task Trial_starts_once_with_an_end_date_and_is_not_offered_again()
    {
        await using (var ctx = _db.NewContext())
        {
            var controller = NewController(ctx, "Teacher");
            (await controller.Current(CancellationToken.None)).Value!.TrialAvailable.ShouldBeTrue();

            var me = (await controller.StartTrial(CancellationToken.None)).Value!;

            me.PlanKey.ShouldBe(SubscriptionPlan.TrialKey);
            me.ValidToUtc.ShouldBe(_clock.Now.AddDays(SubscriptionPlanSeed.DefaultTrialDays));
            me.TrialAvailable.ShouldBeFalse();
        }

        // Admin başka plana geçirse (deneme ataması silinse) bile ikinci deneme yok.
        await using (var ctx = _db.NewContext())
            await new EntitlementService(ctx, _clock).AssignPlanAsync(UserId, SubscriptionPlan.TeacherProKey, _clock.Now, null, 1);

        await using var again = _db.NewContext();
        var result = await NewController(again, "Teacher").StartTrial(CancellationToken.None);
        result.Result.ShouldBeOfType<ConflictObjectResult>();
        (await again.UserSubscriptionPlans.IgnoreQueryFilters().CountAsync(a => a.UserId == UserId)).ShouldBe(2);
    }

    [Fact]
    public async Task Checkout_assigns_a_paid_plan_for_one_month_and_closes_the_previous_one()
    {
        await using (var ctx = _db.NewContext())
            await NewController(ctx, "Teacher").StartTrial(CancellationToken.None);
        _clock.Now = _clock.Now.AddDays(3);

        await using (var ctx = _db.NewContext())
        {
            var me = (await NewController(ctx, "Teacher")
                .Checkout(new SubscriptionCheckoutDto { PlanKey = SubscriptionPlan.TeacherProKey }, CancellationToken.None)).Value!;

            me.PlanKey.ShouldBe(SubscriptionPlan.TeacherProKey);
            me.ValidToUtc.ShouldBe(_clock.Now.AddMonths(1));
            me.Features.Single(f => f.FeatureKey == EntitlementFeatures.BookImport).Limit.ShouldBe(10);
        }

        await using var read = _db.NewContext();
        var history = await read.UserSubscriptionPlans.Include(a => a.Plan).Where(a => a.UserId == UserId)
            .OrderBy(a => a.ValidFromUtc).ToListAsync();
        history.Select(a => a.Plan.Key).ShouldBe([SubscriptionPlan.TrialKey, SubscriptionPlan.TeacherProKey]);
        history[0].ValidToUtc.ShouldBe(_clock.Now);
        history[1].CreateUserId.ShouldBe(UserId);
    }

    [Theory]
    [InlineData("Student", "teacher-pro")]
    [InlineData("Teacher", "free")]
    [InlineData("Teacher", "trial")]
    public async Task Checkout_rejects_plans_not_sold_to_the_caller(string role, string planKey)
    {
        await using var ctx = _db.NewContext();

        var result = await NewController(ctx, role).Checkout(new SubscriptionCheckoutDto { PlanKey = planKey }, CancellationToken.None);

        result.Result.ShouldBeOfType<BadRequestObjectResult>();
        (await ctx.UserSubscriptionPlans.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Checkout_rejects_unknown_plan()
    {
        await using var ctx = _db.NewContext();

        var result = await NewController(ctx, "Teacher").Checkout(new SubscriptionCheckoutDto { PlanKey = "nope" }, CancellationToken.None);

        result.Result.ShouldBeOfType<BadRequestObjectResult>();
    }

    private static Dictionary<string, int?> Limits(IEnumerable<SubscriptionPlanDto> plans, string key) =>
        plans.Single(p => p.Key == key).MonthlyLimits;

    private SubscriptionPlansController NewController(AppDbContext ctx, string role)
    {
        var profiles = Substitute.For<IUserProfileProvider>();
        profiles.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new UserProfileDto
        {
            Id = UserId, KeycloakId = "kc-1", FullName = "Kullanıcı", Email = string.Empty, Role = role,
        });
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddSingleton(profiles)
            .BuildServiceProvider();
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "kc-1"), new(ClaimTypes.Role, role) };
        return new SubscriptionPlansController(ctx, new EntitlementService(ctx, _clock))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "TestAuth")),
                    RequestServices = services,
                },
            },
        };
    }

    public void Dispose() => _db.Dispose();

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    }
}
