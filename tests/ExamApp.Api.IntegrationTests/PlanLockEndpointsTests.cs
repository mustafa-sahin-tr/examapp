using System.Net;
using System.Net.Http.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Entitlements;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #1090: deneme bitip planı olmayan öğretmen / öğrenci yalnız plan sayfasını kullanır (402 <c>subscription.required</c>);
/// muaf roller ve kapalı anahtar bugünkü davranışı korur.
/// </summary>
public class PlanLockEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const string StudentEndpoint = "/api/flashcards/decks";
    private const string TeacherEndpoint = "/api/teacher/flashcards/decks";

    private void RequirePlan(bool on) => Factory.Services.GetRequiredService<PlanRequirementSwitch>().RequirePlan = on;

    /// <summary>Deneme kullanılmış ve bitmiş: plan yok.</summary>
    private Task EndTrialAsync(int userId) => WithDbAsync(async db =>
    {
        var trialId = await db.SubscriptionPlans.Where(p => p.Key == SubscriptionPlan.TrialKey).Select(p => p.Id).SingleAsync();
        db.UserSubscriptionPlans.Add(new UserSubscriptionPlan
        {
            UserId = userId, PlanId = trialId, ValidFromUtc = DateTime.UtcNow.AddDays(-20), ValidToUtc = DateTime.UtcNow.AddDays(-6),
        });
        await db.SaveChangesAsync();
    });

    private Task AssignAsync(int userId, string planKey) => WithDbAsync(async db =>
    {
        var planId = await db.SubscriptionPlans.Where(p => p.Key == planKey).Select(p => p.Id).SingleAsync();
        db.UserSubscriptionPlans.Add(new UserSubscriptionPlan { UserId = userId, PlanId = planId, ValidFromUtc = DateTime.UtcNow.AddDays(-1) });
        await db.SaveChangesAsync();
    });

    [Fact]
    public async Task A_locked_student_gets_402_but_can_still_see_plans_profile_and_parents()
    {
        RequirePlan(true);
        await EndTrialAsync(61);
        var student = await ClientAsAsync(61, "Student", "kc-lock-student", "Student");

        var res = await student.GetAsync(StudentEndpoint);
        res.StatusCode.ShouldBe(HttpStatusCode.PaymentRequired);
        var body = (await res.Content.ReadFromJsonAsync<SubscriptionRequiredDto>())!;
        body.Code.ShouldBe(SubscriptionRequiredDto.RequiredCode);
        body.Audience.ShouldBe("student");
        body.Message.ShouldNotBeNullOrWhiteSpace();

        var me = (await student.GetFromJsonAsync<CurrentSubscriptionPlanDto>("/api/subscription-plans/me"))!;
        me.Locked.ShouldBeTrue();
        me.TrialAvailable.ShouldBeFalse();
        // Kilit açıkken Free seçilebilir bir plan değil.
        (await student.GetFromJsonAsync<List<SubscriptionPlanDto>>("/api/subscription-plans"))!.ShouldNotContain(p => p.Key == SubscriptionPlan.FreeKey);
        (await student.GetAsync("/api/auth/culture")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await student.GetAsync("/api/student-parent/status")).StatusCode.ShouldNotBe(HttpStatusCode.PaymentRequired);
    }

    [Fact]
    public async Task A_locked_teacher_is_unlocked_by_a_plan()
    {
        RequirePlan(true);
        await SeedApprovedTeacherAsync(62);
        await EndTrialAsync(62);
        var teacher = await ClientAsAsync(62, "Teacher", "kc-lock-teacher", "Teacher");

        var res = await teacher.GetAsync(TeacherEndpoint);
        res.StatusCode.ShouldBe(HttpStatusCode.PaymentRequired);
        (await res.Content.ReadFromJsonAsync<SubscriptionRequiredDto>())!.Audience.ShouldBe("teacher");

        (await teacher.PostAsJsonAsync("/api/subscription-plans/me/checkout", new SubscriptionCheckoutDto { PlanKey = SubscriptionPlan.TeacherProKey }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await teacher.GetAsync(TeacherEndpoint)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_new_user_starts_the_trial_instead_of_being_locked()
    {
        RequirePlan(true);
        await SeedApprovedTeacherAsync(63);
        var teacher = await ClientAsAsync(63, "Teacher", "kc-lock-new", "Teacher");

        (await teacher.GetAsync(TeacherEndpoint)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var me = (await teacher.GetFromJsonAsync<CurrentSubscriptionPlanDto>("/api/subscription-plans/me"))!;
        me.PlanKey.ShouldBe(SubscriptionPlan.TrialKey);
        me.Locked.ShouldBeFalse();
    }

    [Fact]
    public async Task The_first_pages_parallel_requests_start_a_single_trial_and_none_is_locked()
    {
        RequirePlan(true);
        await SeedApprovedTeacherAsync(68);
        var teacher = await ClientAsAsync(68, "Teacher", "kc-lock-parallel", "Teacher");

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => teacher.GetAsync(TeacherEndpoint)));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);
        (await WithDbAsync(db => db.UserSubscriptionPlans.CountAsync(a => a.UserId == 68))).ShouldBe(1);
    }

    [Fact]
    public async Task Exempt_roles_and_paying_users_are_not_locked()
    {
        RequirePlan(true);
        await SeedApprovedTeacherAsync(64);
        await EndTrialAsync(64);
        await EndTrialAsync(65);
        await AssignAsync(65, SubscriptionPlan.StudentBasicKey);
        await EndTrialAsync(66);

        var trainer = await ClientAsAsync(64, "Teacher", "kc-lock-trainer", "Teacher", "QuestionTrainer");
        var paying = await ClientAsAsync(65, "Student", "kc-lock-paying", "Student");
        var parent = await ClientAsAsync(66, "Parent", "kc-lock-parent", "Parent");

        (await trainer.GetAsync(TeacherEndpoint)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await paying.GetAsync(StudentEndpoint)).StatusCode.ShouldNotBe(HttpStatusCode.PaymentRequired);
        (await parent.GetAsync("/api/parent/check-parent")).StatusCode.ShouldNotBe(HttpStatusCode.PaymentRequired);
    }

    [Fact]
    public async Task With_the_switch_off_nothing_is_locked()
    {
        await SeedApprovedTeacherAsync(67);
        await EndTrialAsync(67);
        var teacher = await ClientAsAsync(67, "Teacher", "kc-lock-off", "Teacher");

        (await teacher.GetAsync(TeacherEndpoint)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await teacher.GetFromJsonAsync<CurrentSubscriptionPlanDto>("/api/subscription-plans/me"))!.Locked.ShouldBeFalse();
    }
}
