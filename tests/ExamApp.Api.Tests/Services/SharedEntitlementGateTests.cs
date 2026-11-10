using System.Reflection;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Tests.Support;
using Microsoft.AspNetCore.Mvc;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #1088: iki tarafın planında olması gereken özellikler (mesaj, randevu, canlı ders). Önce çağıran denetlenir; sayılan
/// özellikte aynı işin kayıtlı kullanımı (derse yeniden katılım) dolu sınıra takılmaz, planda olmaması yine engeller.
/// </summary>
public class SharedEntitlementGateTests : IDisposable
{
    private const int Student = 51;
    private const int Teacher = 52;

    private readonly TestDb _db = TestDb.Create();
    private readonly MutableClock _clock = new(new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc));

    private SharedEntitlementGate NewGate(AppDbContext ctx) => new(new EntitlementService(ctx, _clock), ctx);

    [Fact]
    public async Task Both_plans_include_the_feature_so_it_is_allowed()
    {
        await AssignAsync(Student, "t-student", EntitlementFeatures.Messaging, null);
        await AssignAsync(Teacher, "t-teacher", EntitlementFeatures.Messaging, null);
        await using var ctx = _db.NewContext();

        var decision = await NewGate(ctx).CheckAsync(EntitlementFeatures.Messaging, EntitlementSubject.Create(Student), Teacher);

        decision.Allowed.ShouldBeTrue();
        decision.Block.ShouldBe(SharedEntitlementBlock.None);
    }

    [Fact]
    public async Task Caller_is_checked_first_so_they_see_their_own_upgrade()
    {
        // İki tarafın da planında yok: çağıran kendi engelini görür, karşı taraf hiç sorulmaz.
        await AssignAsync(Student, "t-student", EntitlementFeatures.Tutoring, null);
        await AssignAsync(Teacher, "t-teacher", EntitlementFeatures.Tutoring, null);
        await using var ctx = _db.NewContext();

        var decision = await NewGate(ctx).CheckAsync(EntitlementFeatures.Messaging, EntitlementSubject.Create(Student), Teacher);

        decision.Block.ShouldBe(SharedEntitlementBlock.Self);
        decision.CounterpartCheck.ShouldBeNull();
    }

    [Fact]
    public async Task Counterpart_without_the_feature_blocks_the_caller()
    {
        await AssignAsync(Student, "t-student", EntitlementFeatures.Tutoring, null);
        await AssignAsync(Teacher, "t-teacher", EntitlementFeatures.Messaging, null);
        await using var ctx = _db.NewContext();

        var decision = await NewGate(ctx).CheckAsync(EntitlementFeatures.Tutoring, EntitlementSubject.Create(Student), Teacher);

        decision.Block.ShouldBe(SharedEntitlementBlock.Counterpart);
        decision.CounterpartCheck!.Limit.ShouldBe(0);
    }

    [Fact]
    public async Task Role_exemption_applies_only_to_the_caller()
    {
        await AssignAsync(Student, "t-student", EntitlementFeatures.Tutoring, null);
        await using var ctx = _db.NewContext();

        var decision = await NewGate(ctx).CheckAsync(EntitlementFeatures.Messaging,
            EntitlementSubject.Create(Teacher, EntitlementRoles.Admin), Student);

        decision.Block.ShouldBe(SharedEntitlementBlock.Counterpart);
    }

    [Fact]
    public async Task Rejoining_a_counted_lesson_passes_a_full_limit_on_either_side()
    {
        await AssignAsync(Student, "t-student", EntitlementFeatures.LiveLesson, 1);
        await AssignAsync(Teacher, "t-teacher", EntitlementFeatures.LiveLesson, 1);
        await using (var ctx = _db.NewContext())
        {
            var entitlements = new EntitlementService(ctx, _clock);
            await entitlements.RecordUsageAsync(Student, EntitlementFeatures.LiveLesson, "live-lesson:7:51");
            await entitlements.RecordUsageAsync(Teacher, EntitlementFeatures.LiveLesson, "live-lesson:7:52");
        }

        await using var read = _db.NewContext();
        var gate = NewGate(read);
        var self = EntitlementSubject.Create(Student);

        (await gate.CheckAsync(EntitlementFeatures.LiveLesson, self, Teacher, default, "live-lesson:7:51", "live-lesson:7:52"))
            .Allowed.ShouldBeTrue();
        (await gate.CheckAsync(EntitlementFeatures.LiveLesson, self, Teacher, default, "live-lesson:8:51", "live-lesson:8:52"))
            .Block.ShouldBe(SharedEntitlementBlock.Self);
        (await gate.CheckAsync(EntitlementFeatures.LiveLesson, self, Teacher, default, "live-lesson:7:51", "live-lesson:8:52"))
            .Block.ShouldBe(SharedEntitlementBlock.Counterpart);
    }

    [Fact]
    public async Task A_recorded_reference_does_not_open_a_feature_missing_from_the_plan()
    {
        await AssignAsync(Student, "t-student", EntitlementFeatures.Messaging, null);
        await AssignAsync(Teacher, "t-teacher", EntitlementFeatures.LiveLesson, null);
        await using (var ctx = _db.NewContext())
            await new EntitlementService(ctx, _clock).RecordUsageAsync(Student, EntitlementFeatures.LiveLesson, "live-lesson:7:51");

        await using var read = _db.NewContext();
        (await NewGate(read).CheckAsync(EntitlementFeatures.LiveLesson, EntitlementSubject.Create(Student), Teacher, default, "live-lesson:7:51"))
            .Block.ShouldBe(SharedEntitlementBlock.Self);
    }

    [Fact]
    public void Responses_tell_apart_own_plan_own_limit_and_counterpart()
    {
        var notInPlan = new EntitlementCheck(false, 0, 0, null, "student-basic");
        var used = new EntitlementCheck(false, 2, 2, null, "t-student");
        var ok = new EntitlementCheck(true, null, 0, null, "t-student");

        EntitlementLimitResponse.ForShared("live-lesson", new(SharedEntitlementBlock.None, ok, ok), "m").ShouldBeNull();
        Body(EntitlementLimitResponse.ForShared("live-lesson", new(SharedEntitlementBlock.Self, notInPlan, null), "m"), 403)
            .Code.ShouldBe(EntitlementLimitReachedDto.NotInPlanCode);
        Body(EntitlementLimitResponse.ForShared("live-lesson", new(SharedEntitlementBlock.Self, used, null), "m"), 429)
            .Code.ShouldBe(EntitlementLimitReachedDto.LimitReachedCode);
        Body(EntitlementLimitResponse.ForShared("live-lesson", new(SharedEntitlementBlock.Counterpart, ok, notInPlan), "m"), 403)
            .ShouldSatisfyAllConditions(
                b => b.Code.ShouldBe(EntitlementLimitReachedDto.CounterpartNotInPlanCode),
                b => b.Message.ShouldBe("m"),
                b => b.PlanKey.ShouldBeNull()); // karşı tarafın planı sızmaz

        static EntitlementLimitReachedDto Body(ObjectResult? result, int status)
        {
            result.ShouldNotBeNull().StatusCode.ShouldBe(status);
            return result.Value.ShouldBeOfType<EntitlementLimitReachedDto>();
        }
    }

    [Theory]
    [InlineData(true, EntitlementFeatures.Messaging, "entitlements.counterpart.teacher.messaging")]
    [InlineData(false, EntitlementFeatures.LiveLesson, "entitlements.counterpart.student.liveLesson")]
    public void Counterpart_message_key_names_the_other_side(bool counterpartIsTeacher, string feature, string expected) =>
        SharedEntitlementMessages.Key(counterpartIsTeacher, feature).ShouldBe(expected);

    [Theory]
    [InlineData(typeof(TeacherFlashcardsController), "CreateDeck", EntitlementFeatures.FlashcardSets)]
    [InlineData(typeof(TeacherFlashcardsController), "AddCard", EntitlementFeatures.FlashcardSets)]
    [InlineData(typeof(TeacherFlashcardsController), "Assign", EntitlementFeatures.FlashcardSets)]
    [InlineData(typeof(BookingController), "CreateSlot", EntitlementFeatures.Tutoring)]
    [InlineData(typeof(BookingController), "CreateRecurringRule", EntitlementFeatures.Tutoring)]
    public void Teacher_tool_writes_need_the_feature_in_the_plan(Type controller, string action, string feature)
    {
        var attribute = controller.GetMethod(action)!.GetCustomAttribute<RequiresEntitlementAttribute>().ShouldNotBeNull();
        attribute.FeatureKey.ShouldBe(feature);
        attribute.InPlanOnly.ShouldBeTrue();
    }

    [Fact]
    public void Coach_panel_is_gated_for_the_teacher_side_only()
    {
        var attribute = typeof(TeacherCoachController).GetCustomAttribute<RequiresEntitlementAttribute>().ShouldNotBeNull();
        attribute.FeatureKey.ShouldBe(EntitlementFeatures.CoachPanel);
        typeof(TeacherFlashcardsController).GetMethod("DeleteDeck")!.GetCustomAttribute<RequiresEntitlementAttribute>().ShouldBeNull();
    }

    // Free bugün bu özelliklere sınırsız (#1090'a dek); "planda yok" için yalnız verilen özelliği içeren test planı atanır.
    private async Task AssignAsync(int userId, string planKey, string feature, int? limit)
    {
        await using var ctx = _db.NewContext();
        ctx.SubscriptionPlans.Add(new SubscriptionPlan
        {
            Key = planKey,
            Name = planKey,
            IsActive = true,
            FeatureLimits = { new SubscriptionPlanFeatureLimit { FeatureKey = feature, MonthlyLimit = limit } },
        });
        await ctx.SaveChangesAsync();
        (await new EntitlementService(ctx, _clock).AssignPlanAsync(userId, planKey, null, null, 1)).ShouldBe(AssignPlanError.None);
    }

    public void Dispose() => _db.Dispose();

    private sealed class MutableClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
    }
}
