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
    public async Task Catalogue_is_seeded_with_three_tiers_per_audience_issue1093()
    {
        await using var ctx = _db.NewContext();

        var plans = await SubscriptionPlanCatalog.ListAsync(ctx, _ => true, CancellationToken.None);

        plans.Select(p => (p.Key, p.Audience, p.MonthlyPrice)).ShouldBe([
            (SubscriptionPlan.FreeKey, "Any", 0m), (SubscriptionPlan.TrialKey, "Any", 0m),
            (SubscriptionPlan.TeacherBasicKey, "Teacher", 149m), (SubscriptionPlan.TeacherEssentialKey, "Teacher", 299m),
            (SubscriptionPlan.TeacherProKey, "Teacher", 599m),
            (SubscriptionPlan.StudentBasicKey, "Student", 99m), (SubscriptionPlan.StudentEssentialKey, "Student", 199m),
            (SubscriptionPlan.StudentProKey, "Student", 349m), (SubscriptionPlan.StudentPlusKey, "Student", 99m)]);
        plans.Single(p => p.Key == SubscriptionPlan.StudentPlusKey).IsActive.ShouldBeFalse();
        plans.Where(p => p.Key != SubscriptionPlan.StudentPlusKey).ShouldAllBe(p => p.IsActive);
        plans.ShouldAllBe(p => p.Currency == "TRY" && p.DisplayNameKey.StartsWith("subscription.plans."));
        plans.Single(p => p.Key == SubscriptionPlan.TrialKey).TrialDays.ShouldBe(SubscriptionPlanSeed.DefaultTrialDays);
        plans.Where(p => p.Key != SubscriptionPlan.TrialKey).ShouldAllBe(p => p.TrialDays == null);

        // Free kilitleme işine (#1090) kadar eski sınırlarını korur; #1087 bugünkü elle soru ve otomatik tespiti, #1088 öğretmen
        // araçlarını ve iki taraflı özellikleri açık tutar.
        Limits(plans, SubscriptionPlan.FreeKey).ShouldBe(new Dictionary<string, int?>
        {
            [EntitlementFeatures.BookImport] = 0, [EntitlementFeatures.AiClassification] = 20, [EntitlementFeatures.MockExam] = 0,
            [EntitlementFeatures.QuickAdd] = 5, [EntitlementFeatures.ManualLabeling] = null, [EntitlementFeatures.AutoDetect] = null,
            [EntitlementFeatures.ActivityEditor] = null, [EntitlementFeatures.FlashcardSets] = null, [EntitlementFeatures.CoachPanel] = null,
            [EntitlementFeatures.PrintTest] = null, [EntitlementFeatures.Messaging] = null, [EntitlementFeatures.Tutoring] = null,
            [EntitlementFeatures.LiveLesson] = null,
        });
        Limits(plans, SubscriptionPlan.TeacherBasicKey).ShouldBe(new Dictionary<string, int?> { [EntitlementFeatures.ManualLabeling] = null });
        var essential = Limits(plans, SubscriptionPlan.TeacherEssentialKey);
        essential.ShouldNotContainKey(EntitlementFeatures.BookImport);
        essential.ShouldNotContainKey(EntitlementFeatures.QuestionGeneration);
        (essential[EntitlementFeatures.CanvasAi], essential[EntitlementFeatures.QuickAdd], essential[EntitlementFeatures.LiveLesson])
            .ShouldBe((300, 100, 10));
        var pro = Limits(plans, SubscriptionPlan.TeacherProKey);
        (pro[EntitlementFeatures.BookImport], pro[EntitlementFeatures.QuickAdd], pro[EntitlementFeatures.AiClassification],
            pro[EntitlementFeatures.QuestionGeneration]).ShouldBe((10, 200, 1000, 200));
        essential.Keys.Except(pro.Keys).ShouldBeEmpty();
        Limits(plans, SubscriptionPlan.StudentBasicKey).ShouldBe(new Dictionary<string, int?>
        {
            [EntitlementFeatures.MockExam] = 3, [EntitlementFeatures.MixedMini] = 3, [EntitlementFeatures.Practice] = 10,
            [EntitlementFeatures.SelfActivity] = 10,
        });
        Limits(plans, SubscriptionPlan.StudentEssentialKey).ShouldNotContainKey(EntitlementFeatures.Analytics);
        Limits(plans, SubscriptionPlan.StudentProKey).Values.ShouldAllBe(v => v == null);
        Limits(plans, SubscriptionPlan.TrialKey)[EntitlementFeatures.BookImport].ShouldBe(1);
        pro.Keys.Except(Limits(plans, SubscriptionPlan.TrialKey).Keys).ShouldBeEmpty();
    }

    [Fact]
    public void Every_seeded_limit_uses_a_registered_feature_issue1093()
    {
        using var ctx = _db.NewContext();

        ctx.SubscriptionPlanFeatureLimits.Select(l => l.FeatureKey).Distinct().ToList()
            .ShouldAllBe(key => EntitlementFeatures.Find(key) != null);
    }

    [Theory]
    [InlineData("Teacher", "Teacher")]
    [InlineData("Student", "Student")]
    public void Features_lists_the_callers_audience_and_shared_features_issue1093(string role, string audience)
    {
        using var ctx = _db.NewContext();

        var features = NewController(ctx, role).Features().Value!;

        features.ShouldAllBe(f => f.Audience == audience || f.Audience == "Both");
        features.ShouldContain(f => f.Key == EntitlementFeatures.Messaging && f.Kind == "Toggle" && f.Unit == null);
        features.Where(f => f.Kind == "Metered").ShouldAllBe(f => f.Unit != null);
    }

    [Theory]
    [InlineData("Teacher", new[] { "free", "trial", "teacher-basic", "teacher-essential", "teacher-pro" })]
    [InlineData("Student", new[] { "free", "trial", "student-basic", "student-essential", "student-pro" })]
    [InlineData("Parent", new[] { "free", "trial" })]
    [InlineData("Admin", new[] { "free", "trial", "teacher-basic", "teacher-essential", "teacher-pro", "student-basic", "student-essential", "student-pro" })]
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
        // #1093: öğretmen yalnız öğretmen ve ortak özellikleri görür.
        me.Features.Select(f => f.FeatureKey).ShouldBe(EntitlementFeatures.Registry
            .Where(f => f.Audience != EntitlementAudience.Student).Select(f => f.Key));
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

    [Fact]
    public async Task Limit_response_carries_feature_usage_and_reset_date_for_a_blocked_user()
    {
        await using var ctx = _db.NewContext();
        var check = await new EntitlementService(ctx, _clock).CheckAsync(
            EntitlementSubject.Create(UserId, "Teacher"), EntitlementFeatures.BookImport, CancellationToken.None);

        var result = EntitlementLimitResponse.Create(EntitlementFeatures.BookImport, check, "limit");

        check.Allowed.ShouldBeFalse();
        result.StatusCode.ShouldBe(StatusCodes.Status429TooManyRequests);
        var body = result.Value.ShouldBeOfType<EntitlementLimitReachedDto>();
        body.Code.ShouldBe("entitlement.limitReached");
        body.Feature.ShouldBe(EntitlementFeatures.BookImport);
        body.Limit.ShouldBe(0);
        body.Used.ShouldBe(0);
        body.ResetsAtUtc.ShouldBe(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc));
        body.PlanKey.ShouldBe(SubscriptionPlan.FreeKey);
        body.Message.ShouldBe("limit");
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
