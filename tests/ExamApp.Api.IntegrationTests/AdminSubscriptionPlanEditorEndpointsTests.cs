using System.Net;
using System.Net.Http.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Entitlements;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.IntegrationTests;

/// <summary>Issue #1086: admin plan düzenleyicisi — yetki, doğrulama, audit ve sonraki hak denetimine yansıma.</summary>
public class AdminSubscriptionPlanEditorEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const string PlanKey = "it-teacher";
    private const string Url = "/api/admin/subscription-plans/" + PlanKey;
    private const int TeacherId = 51;

    // Respawn plan tablolarını da boşaltır: test kendi planını ve öğretmen atamasını ekler.
    private async Task SeedPlanAsync() => await WithDbAsync(async db =>
    {
        var plan = new SubscriptionPlan
        {
            Key = PlanKey, Name = "IT Teacher", DisplayNameKey = "subscription.plans.it", Audience = SubscriptionPlanAudience.Teacher,
            MonthlyPrice = 100m,
        };
        plan.FeatureLimits.Add(new SubscriptionPlanFeatureLimit { FeatureKey = EntitlementFeatures.BookImport, MonthlyLimit = 1 });
        plan.FeatureLimits.Add(new SubscriptionPlanFeatureLimit { FeatureKey = EntitlementFeatures.ManualLabeling, MonthlyLimit = null });
        db.SubscriptionPlans.Add(plan);
        await db.SaveChangesAsync();
        db.UserSubscriptionPlans.Add(new UserSubscriptionPlan { UserId = TeacherId, PlanId = plan.Id, ValidFromUtc = DateTime.UtcNow.AddDays(-1) });
        await db.SaveChangesAsync();
    });

    private static UpdateSubscriptionPlanDto Request(Dictionary<string, int?> limits) => new()
    {
        Name = "IT Teacher v2", MonthlyPrice = 249.90m, IsActive = true, MonthlyLimits = limits,
    };

    [Fact]
    public async Task Only_an_admin_can_edit_a_plan()
    {
        await SeedPlanAsync();
        var teacher = await ClientAsAsync(TeacherId, "Teacher", "kc-plan-teacher", "Teacher");

        (await teacher.PutAsJsonAsync(Url, Request(new()))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Anonymous().PutAsJsonAsync(Url, Request(new()))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_saved_plan_applies_to_the_next_check_and_writes_an_audit_row()
    {
        await SeedPlanAsync();
        var admin = await ClientAsAsync(2, "Admin", "kc-plan-admin", "Admin");
        var teacher = await ClientAsAsync(TeacherId, "Teacher", "kc-plan-teacher", "Teacher");

        var res = await admin.PutAsJsonAsync(Url, Request(new()
        {
            [EntitlementFeatures.BookImport] = 4,
            [EntitlementFeatures.CoachPanel] = 7, // açık / kapalı özellikte sayı yok sayılır
        }));

        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        var plan = await res.Content.ReadFromJsonAsync<SubscriptionPlanDto>(Json);
        plan!.Name.ShouldBe("IT Teacher v2");
        plan.MonthlyPrice.ShouldBe(249.90m);
        plan.MonthlyLimits[EntitlementFeatures.BookImport].ShouldBe(4);
        plan.MonthlyLimits[EntitlementFeatures.CoachPanel].ShouldBeNull();
        plan.MonthlyLimits[EntitlementFeatures.ManualLabeling].ShouldBe(0); // listede yok = planda yok

        var current = await teacher.GetFromJsonAsync<CurrentSubscriptionPlanDto>("/api/subscription-plans/me", Json);
        var features = current!.Features.ToDictionary(f => f.FeatureKey);
        features[EntitlementFeatures.BookImport].Limit.ShouldBe(4);
        features[EntitlementFeatures.CoachPanel].Allowed.ShouldBeTrue();
        features[EntitlementFeatures.ManualLabeling].Allowed.ShouldBeFalse();

        await WithDbAsync(async db =>
        {
            var audit = await db.SubscriptionPlanChangeAudits.SingleAsync();
            audit.PlanKey.ShouldBe(PlanKey);
            audit.ActorUserId.ShouldBe(2);
            audit.BeforeJson.ShouldContain("\"book-import\":1");
            audit.AfterJson.ShouldContain("\"book-import\":4");
            audit.AfterJson.ShouldNotContain(EntitlementFeatures.ManualLabeling);
        });
    }

    [Fact]
    public async Task Unknown_features_negative_limits_and_wrong_audience_are_rejected()
    {
        await SeedPlanAsync();
        var admin = await ClientAsAsync(2, "Admin", "kc-plan-admin", "Admin");

        async Task<string?> CodeAsync(UpdateSubscriptionPlanDto request)
        {
            var res = await admin.PutAsJsonAsync(Url, request);
            res.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            return (await res.Content.ReadFromJsonAsync<Dictionary<string, string>>(Json))!["code"];
        }

        (await CodeAsync(Request(new() { ["no-such-feature"] = 1 }))).ShouldBe("subscription.unknownFeature");
        (await CodeAsync(Request(new() { [EntitlementFeatures.BookImport] = -1 }))).ShouldBe("subscription.invalidLimit");
        (await CodeAsync(Request(new() { [EntitlementFeatures.MockExam] = 3 }))).ShouldBe("subscription.featureAudienceMismatch");
        (await CodeAsync(new UpdateSubscriptionPlanDto { Name = "x", MonthlyPrice = -5 })).ShouldBe("subscription.invalidPrice");
        (await CodeAsync(new UpdateSubscriptionPlanDto { Name = "x", TrialDays = 14 })).ShouldBe("subscription.invalidTrialDays");

        (await admin.PutAsJsonAsync("/api/admin/subscription-plans/missing", Request(new()))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await WithDbAsync(async db => (await db.SubscriptionPlanChangeAudits.CountAsync()).ShouldBe(0));
    }
}
