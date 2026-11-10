using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Entitlements;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #1091: öğrencinin planını velisi alır. Yalnız Active bağlantının velisi; başka velinin çocuğu ve bekleyen bağlantı 404.
/// Öğrenci kendine öğrenci planı alamaz (403), öğretmenin kendi ödemesi değişmez. Plan kaydında aktör velidir.
/// </summary>
public class ParentChildPlanEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private async Task<int> SeedStudentAsync(int studentUserId, params int[] parentUserIds)
    {
        var directory = Factory.Services.GetRequiredService<FakeUserDirectory>();
        directory.Add(new() { Id = studentUserId, KeycloakId = $"kc-{studentUserId}", FullName = "Ayşe Kaya" });
        foreach (var p in parentUserIds)
            directory.Add(new() { Id = p, KeycloakId = $"kc-{p}", FullName = $"Veli {p}" });

        return await WithDbAsync(async db =>
        {
            var student = new Student { UserId = studentUserId, StudentNumber = $"P{studentUserId}" };
            db.Students.Add(student);
            db.Parents.AddRange(parentUserIds.Select(u => new Parent { UserId = u }));
            await db.SaveChangesAsync();
            return student.Id;
        });
    }

    private Task<HttpClient> ParentAsync(int userId) => ClientAsAsync(userId, "Parent", $"kc-{userId}", "Parent");

    private static string PlanUrl(int studentId) => $"/api/parent/children/{studentId}/plan";

    private static Task<HttpResponseMessage> BuyAsync(HttpClient client, int studentId, string planKey) =>
        client.PostAsJsonAsync($"{PlanUrl(studentId)}/checkout", new SubscriptionCheckoutDto { PlanKey = planKey });

    [Fact]
    public async Task Linked_parent_sees_the_child_plan_and_buys_one_as_the_audited_actor()
    {
        const int studentUser = 47101, parentUser = 47102;
        var studentId = await SeedStudentAsync(studentUser, parentUser);
        await SeedParentLinkAsync(parentUser, studentUser);
        var parent = await ParentAsync(parentUser);

        var before = await parent.GetAsync(PlanUrl(studentId));
        before.StatusCode.ShouldBe(HttpStatusCode.OK);
        before.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var plan = (await before.Content.ReadFromJsonAsync<ChildSubscriptionPlanDto>(Json))!;
        plan.PlanKey.ShouldBe(SubscriptionPlan.FreeKey);
        // Yalnız ücretli öğrenci planları: Free, deneme, öğretmen planları ve pasif plan yok.
        plan.Plans.Select(p => p.Key).ShouldBe(
            [SubscriptionPlan.StudentBasicKey, SubscriptionPlan.StudentEssentialKey, SubscriptionPlan.StudentProKey]);
        plan.Features.ShouldContain(f => f.Key == EntitlementFeatures.Analytics);
        plan.Features.ShouldNotContain(f => f.Key == EntitlementFeatures.BookImport);

        var bought = await BuyAsync(parent, studentId, SubscriptionPlan.StudentEssentialKey);
        bought.StatusCode.ShouldBe(HttpStatusCode.OK);
        var after = (await bought.Content.ReadFromJsonAsync<ChildSubscriptionPlanDto>(Json))!;
        after.PlanKey.ShouldBe(SubscriptionPlan.StudentEssentialKey);
        after.IsTrial.ShouldBeFalse();
        after.ValidToUtc.ShouldNotBeNull();
        after.ValidToUtc!.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddDays(27));

        var audit = await WithDbAsync(db => db.SubscriptionPlanAssignmentAudits.AsNoTracking().SingleAsync(a => a.UserId == studentUser));
        audit.ActorUserId.ShouldBe(parentUser);
        audit.ToPlanKey.ShouldBe(SubscriptionPlan.StudentEssentialKey);
        (await WithDbAsync(db => db.ParentAccessAudits.CountAsync(a => a.Endpoint == ParentAccessEndpoints.ChildPlanCheckout)))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Parent_cannot_buy_a_teacher_plan_or_the_trial_for_the_child()
    {
        const int studentUser = 47111, parentUser = 47112;
        var studentId = await SeedStudentAsync(studentUser, parentUser);
        await SeedParentLinkAsync(parentUser, studentUser);
        var parent = await ParentAsync(parentUser);

        foreach (var key in new[] { SubscriptionPlan.TeacherProKey, SubscriptionPlan.TrialKey, SubscriptionPlan.FreeKey })
        {
            var res = await BuyAsync(parent, studentId, key);
            res.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await CodeAsync(res)).ShouldBe(SubscriptionPlanErrorCodes.PlanNotAvailable);
        }
        (await BuyAsync(parent, studentId, "no-such-plan")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await WithDbAsync(db => db.UserSubscriptionPlans.CountAsync(a => a.UserId == studentUser))).ShouldBe(0);
    }

    [Fact]
    public async Task Another_parents_child_and_a_pending_link_are_404()
    {
        const int studentUser = 47121, owner = 47122, stranger = 47123, pending = 47124;
        var studentId = await SeedStudentAsync(studentUser, owner, stranger, pending);
        await SeedParentLinkAsync(owner, studentUser);
        await SeedParentLinkAsync(pending, studentUser, active: false);

        foreach (var parentUser in new[] { stranger, pending })
        {
            var parent = await ParentAsync(parentUser);
            (await parent.GetAsync(PlanUrl(studentId))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await BuyAsync(parent, studentId, SubscriptionPlan.StudentBasicKey)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
        (await WithDbAsync(db => db.UserSubscriptionPlans.CountAsync(a => a.UserId == studentUser))).ShouldBe(0);
    }

    [Fact]
    public async Task Student_cannot_self_checkout_but_a_teacher_still_can()
    {
        var student = await ClientAsAsync(47131, "Student", "kc-47131", "Student");
        var res = await student.PostAsJsonAsync("/api/subscription-plans/me/checkout",
            new SubscriptionCheckoutDto { PlanKey = SubscriptionPlan.StudentBasicKey });
        res.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(res)).ShouldBe(SubscriptionPlanErrorCodes.ParentPays);
        (await WithDbAsync(db => db.UserSubscriptionPlans.CountAsync(a => a.UserId == 47131))).ShouldBe(0);

        var teacher = await ClientAsAsync(47132, "Teacher", "kc-47132", "Teacher");
        var bought = await teacher.PostAsJsonAsync("/api/subscription-plans/me/checkout",
            new SubscriptionCheckoutDto { PlanKey = SubscriptionPlan.TeacherBasicKey });
        bought.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await bought.Content.ReadFromJsonAsync<CurrentSubscriptionPlanDto>(Json))!.PlanKey.ShouldBe(SubscriptionPlan.TeacherBasicKey);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString();
}
