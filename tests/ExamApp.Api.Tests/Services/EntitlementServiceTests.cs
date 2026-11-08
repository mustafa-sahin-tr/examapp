using ExamApp.Api.Data;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #1019: plan / hak katmanı — varsayılan Free, rol muafiyetleri, ay sınırı, atama geçerliliği, kullanım defteri.</summary>
public class EntitlementServiceTests : IDisposable
{
    private const string Feature = EntitlementFeatures.BookImport;
    private const int TeacherId = 41;

    private readonly TestDb _db = TestDb.Create();
    private readonly MutableClock _clock = new(new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc));

    private EntitlementService NewService(AppDbContext ctx) => new(ctx, _clock);

    private static EntitlementSubject Teacher(int id = TeacherId) => EntitlementSubject.Create(id);

    [Fact]
    public async Task User_without_assignment_resolves_to_free_with_zero_book_imports()
    {
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        var plan = await service.ResolvePlanAsync(TeacherId);
        var check = await service.CheckAsync(Teacher(), Feature);

        plan.Key.ShouldBe(SubscriptionPlan.FreeKey);
        check.ShouldBe(new EntitlementCheck(false, 0, 0, new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), SubscriptionPlan.FreeKey));
        check.Remaining.ShouldBe(0);
    }

    [Fact]
    public async Task Admin_is_unlimited_for_every_feature_and_question_trainer_for_book_import()
    {
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        (await service.CheckAsync(EntitlementSubject.Create(1, EntitlementRoles.Admin), Feature)).ShouldSatisfyAllConditions(
            c => c.Allowed.ShouldBeTrue(), c => c.Limit.ShouldBeNull(), c => c.ResetsAtUtc.ShouldBeNull());
        (await service.CheckAsync(EntitlementSubject.Create(1, EntitlementRoles.Admin), "some-future-feature")).Allowed.ShouldBeTrue();

        (await service.CheckAsync(EntitlementSubject.Create(2, EntitlementRoles.QuestionTrainer), Feature)).Allowed.ShouldBeTrue();
        // Muafiyet özellik bazında: QuestionTrainer başka özellikte plana tabidir (Free'de satır yok = 0).
        (await service.CheckAsync(EntitlementSubject.Create(2, EntitlementRoles.QuestionTrainer), "some-future-feature")).Allowed.ShouldBeFalse();
    }

    [Fact]
    public async Task Limit_is_reached_after_recorded_usages_and_resets_at_the_month_boundary()
    {
        await AddPlanAsync("pro", 2);
        await using (var ctx = _db.NewContext())
            (await NewService(ctx).AssignPlanAsync(TeacherId, "pro", null, null, assignedByUserId: 1)).ShouldBe(AssignPlanError.None);

        _clock.Now = new DateTime(2026, 11, 30, 23, 0, 0, DateTimeKind.Utc);
        await using (var ctx = _db.NewContext())
        {
            var service = NewService(ctx);
            (await service.CheckAsync(Teacher(), Feature)).ShouldSatisfyAllConditions(
                c => c.Allowed.ShouldBeTrue(), c => c.Limit.ShouldBe(2), c => c.Remaining.ShouldBe(2), c => c.PlanKey.ShouldBe("pro"));
            (await service.RecordUsageAsync(TeacherId, Feature, "job-1")).ShouldBeTrue();
            (await service.RecordUsageAsync(TeacherId, Feature, "job-2")).ShouldBeTrue();

            var check = await service.CheckAsync(Teacher(), Feature);
            check.Allowed.ShouldBeFalse();
            check.Used.ShouldBe(2);
            check.ResetsAtUtc.ShouldBe(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc));
        }

        _clock.Now = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);
        await using (var ctx = _db.NewContext())
        {
            var check = await NewService(ctx).CheckAsync(Teacher(), Feature);
            check.Allowed.ShouldBeTrue();
            check.Used.ShouldBe(0);
            check.ResetsAtUtc.ShouldBe(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        }
    }

    [Fact]
    public async Task Null_limit_is_unlimited()
    {
        await AddPlanAsync("school", null);
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);
        await service.AssignPlanAsync(TeacherId, "school", null, null, 1);
        for (var i = 0; i < 30; i++)
            await service.RecordUsageAsync(TeacherId, Feature, $"job-{i}");

        var check = await service.CheckAsync(Teacher(), Feature);

        check.ShouldBe(new EntitlementCheck(true, null, 30, null, "school"));
    }

    [Fact]
    public async Task Usage_is_counted_once_per_reference_and_only_for_its_owner()
    {
        await AddPlanAsync("pro", 5);
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);
        await service.AssignPlanAsync(TeacherId, "pro", null, null, 1);

        (await service.RecordUsageAsync(TeacherId, Feature, "job-1")).ShouldBeTrue();
        (await service.RecordUsageAsync(TeacherId, Feature, "job-1")).ShouldBeFalse();
        await service.RecordUsageAsync(99, Feature, "job-2");

        (await service.CheckAsync(Teacher(), Feature)).Used.ShouldBe(1);
    }

    [Fact]
    public async Task Assignment_applies_only_inside_its_validity_window()
    {
        await AddPlanAsync("pro", 10);
        await using (var ctx = _db.NewContext())
        {
            var result = await NewService(ctx).AssignPlanAsync(TeacherId, "pro",
                new DateTime(2026, 11, 25, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 12, 10, 0, 0, 0, DateTimeKind.Utc), 1);
            result.ShouldBe(AssignPlanError.None);
        }

        async Task<string?> PlanAt(DateTime now)
        {
            _clock.Now = now;
            await using var ctx = _db.NewContext();
            return (await NewService(ctx).ResolvePlanAsync(TeacherId)).Key;
        }

        (await PlanAt(new DateTime(2026, 11, 24, 23, 59, 0, DateTimeKind.Utc))).ShouldBe(SubscriptionPlan.FreeKey);
        (await PlanAt(new DateTime(2026, 11, 25, 0, 0, 0, DateTimeKind.Utc))).ShouldBe("pro");
        (await PlanAt(new DateTime(2026, 12, 9, 23, 59, 0, DateTimeKind.Utc))).ShouldBe("pro");
        (await PlanAt(new DateTime(2026, 12, 10, 0, 0, 0, DateTimeKind.Utc))).ShouldBe(SubscriptionPlan.FreeKey);
    }

    [Fact]
    public async Task New_assignment_closes_the_open_one_so_an_account_has_one_plan()
    {
        await AddPlanAsync("pro", 10);
        await AddPlanAsync("basic", 3);
        await using (var ctx = _db.NewContext())
        {
            var service = NewService(ctx);
            await service.AssignPlanAsync(TeacherId, "pro", null, null, 1);
            _clock.Now = _clock.Now.AddDays(2);
            await service.AssignPlanAsync(TeacherId, "basic", null, null, 1);
        }

        await using var check = _db.NewContext();
        var rows = await check.UserSubscriptionPlans.Include(a => a.Plan).OrderBy(a => a.ValidFromUtc).ToListAsync();
        rows.Select(r => r.Plan.Key).ShouldBe(["pro", "basic"]);
        rows[0].ValidToUtc.ShouldBe(rows[1].ValidFromUtc);
        rows[1].CreateUserId.ShouldBe(1);
        (await NewService(check).ResolvePlanAsync(TeacherId)).Key.ShouldBe("basic");
    }

    [Fact]
    public async Task Assignment_rejects_unknown_or_inactive_plan_and_inverted_range()
    {
        await AddPlanAsync("retired", 1, isActive: false);
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        (await service.AssignPlanAsync(TeacherId, "nope", null, null, 1)).ShouldBe(AssignPlanError.UnknownPlan);
        (await service.AssignPlanAsync(TeacherId, "retired", null, null, 1)).ShouldBe(AssignPlanError.UnknownPlan);
        (await service.AssignPlanAsync(TeacherId, SubscriptionPlan.FreeKey, _clock.Now, _clock.Now.AddDays(-1), 1))
            .ShouldBe(AssignPlanError.InvalidRange);
        (await ctx.UserSubscriptionPlans.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Inactive_assigned_plan_falls_back_to_free()
    {
        await AddPlanAsync("pro", 10);
        await using (var ctx = _db.NewContext())
            await NewService(ctx).AssignPlanAsync(TeacherId, "pro", null, null, 1);
        await using (var ctx = _db.NewContext())
        {
            (await ctx.SubscriptionPlans.SingleAsync(p => p.Key == "pro")).IsActive = false;
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        (await NewService(read).ResolvePlanAsync(TeacherId)).Key.ShouldBe(SubscriptionPlan.FreeKey);
    }

    private async Task AddPlanAsync(string key, int? bookImportLimit, bool isActive = true)
    {
        await using var ctx = _db.NewContext();
        ctx.SubscriptionPlans.Add(new SubscriptionPlan
        {
            Key = key,
            Name = key,
            IsActive = isActive,
            FeatureLimits = { new SubscriptionPlanFeatureLimit { FeatureKey = Feature, MonthlyLimit = bookImportLimit } },
        });
        await ctx.SaveChangesAsync();
    }

    public void Dispose() => _db.Dispose();

    private sealed class MutableClock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    }
}
