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
/// Issue #1116: satın alma iptali. Aylık plan dönem sonuna kadar sürer; 6 / 12 aylık plan ilk 14 günde iptal edilirse hemen biter ve
/// tam iade kaydı yazılır, sonrasında dönem sonuna kadar sürer. Öğrencinin planını velisi iptal eder; deneme / admin ataması iptal edilmez.
/// </summary>
public class PlanCancellationEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private async Task<int> SeedStudentAsync(int studentUserId, int parentUserId)
    {
        var directory = Factory.Services.GetRequiredService<FakeUserDirectory>();
        directory.Add(new() { Id = studentUserId, KeycloakId = $"kc-{studentUserId}", FullName = "Ayşe Kaya" });
        directory.Add(new() { Id = parentUserId, KeycloakId = $"kc-{parentUserId}", FullName = "Veli" });

        var studentId = await WithDbAsync(async db =>
        {
            var student = new Student { UserId = studentUserId, StudentNumber = $"C{studentUserId}" };
            db.Students.Add(student);
            db.Parents.Add(new Parent { UserId = parentUserId });
            await db.SaveChangesAsync();
            return student.Id;
        });
        await SeedParentLinkAsync(parentUserId, studentUserId);
        return studentId;
    }

    // Satın alma (PricePaid) olarak doğrudan yazılır: uzun süre seçenekleri sınıf / fiyat tablosu ister.
    private Task SeedPurchaseAsync(int userId, string planKey, PlanDuration duration, DateTime validFrom, decimal price) =>
        WithDbAsync(async db =>
        {
            var plan = await db.SubscriptionPlans.SingleAsync(p => p.Key == planKey);
            db.UserSubscriptionPlans.Add(new UserSubscriptionPlan
            {
                UserId = userId, PlanId = plan.Id, ValidFromUtc = validFrom, ValidToUtc = StudentPlanPricing.ValidTo(validFrom, duration),
                Duration = duration, PricePaid = price,
            });
            await db.SaveChangesAsync();
        });

    private static string PlanUrl(int studentId) => $"/api/parent/children/{studentId}/plan";

    [Fact]
    public async Task Monthly_plan_cancelled_by_the_parent_keeps_access_until_the_end_of_the_month()
    {
        const int studentUser = 48101, parentUser = 48102;
        var studentId = await SeedStudentAsync(studentUser, parentUser);
        var parent = await ClientAsAsync(parentUser, "Parent", $"kc-{parentUser}", "Parent");
        (await parent.PostAsJsonAsync($"{PlanUrl(studentId)}/checkout",
            new SubscriptionCheckoutDto { PlanKey = SubscriptionPlan.StudentBasicKey })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var before = (await parent.GetFromJsonAsync<ChildSubscriptionPlanDto>(PlanUrl(studentId), Json))!;
        before.Purchase.ShouldNotBeNull();
        before.Purchase!.Duration.ShouldBe(nameof(PlanDuration.Monthly));
        before.Purchase.FullRefundUntilUtc.ShouldBeNull();
        before.Purchase.CancelledAtUtc.ShouldBeNull();

        var res = await parent.PostAsync($"{PlanUrl(studentId)}/cancel", null);
        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        var cancelled = (await res.Content.ReadFromJsonAsync<PlanCancellationDto>(Json))!;
        cancelled.RefundAmount.ShouldBeNull();
        cancelled.AccessUntilUtc!.Value.ShouldBe(before.ValidToUtc!.Value, TimeSpan.FromSeconds(1));

        var after = (await parent.GetFromJsonAsync<ChildSubscriptionPlanDto>(PlanUrl(studentId), Json))!;
        after.PlanKey.ShouldBe(SubscriptionPlan.StudentBasicKey);
        after.Purchase!.CancelledAtUtc.ShouldNotBeNull();

        var again = await parent.PostAsync($"{PlanUrl(studentId)}/cancel", null);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(again)).ShouldBe(SubscriptionPlanErrorCodes.AlreadyCancelled);

        var audit = await WithDbAsync(db => db.SubscriptionPlanAssignmentAudits.AsNoTracking()
            .SingleAsync(a => a.UserId == studentUser && a.Action == SubscriptionPlanAuditAction.Cancelled));
        audit.ActorUserId.ShouldBe(parentUser);
        (await WithDbAsync(db => db.ParentAccessAudits.CountAsync(a => a.Endpoint == ParentAccessEndpoints.ChildPlanCancel))).ShouldBe(1);
    }

    [Fact]
    public async Task Long_plan_within_14_days_ends_now_with_a_full_refund()
    {
        const int studentUser = 48111, parentUser = 48112;
        var studentId = await SeedStudentAsync(studentUser, parentUser);
        await SeedPurchaseAsync(studentUser, SubscriptionPlan.StudentProKey, PlanDuration.SixMonths, DateTime.UtcNow.AddDays(-3), 1500m);
        var parent = await ClientAsAsync(parentUser, "Parent", $"kc-{parentUser}", "Parent");

        var before = (await parent.GetFromJsonAsync<ChildSubscriptionPlanDto>(PlanUrl(studentId), Json))!;
        before.Purchase!.FullRefundUntilUtc!.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddDays(10));

        var res = await parent.PostAsync($"{PlanUrl(studentId)}/cancel", null);
        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        var cancelled = (await res.Content.ReadFromJsonAsync<PlanCancellationDto>(Json))!;
        cancelled.RefundAmount.ShouldBe(1500m);
        cancelled.AccessUntilUtc!.Value.ShouldBeLessThanOrEqualTo(DateTime.UtcNow);

        var after = (await parent.GetFromJsonAsync<ChildSubscriptionPlanDto>(PlanUrl(studentId), Json))!;
        after.PlanKey.ShouldBe(SubscriptionPlan.FreeKey);
        after.Purchase.ShouldBeNull();
        var row = await WithDbAsync(db => db.UserSubscriptionPlans.AsNoTracking().SingleAsync(a => a.UserId == studentUser));
        row.RefundAmount.ShouldBe(1500m);
        row.CancelledByUserId.ShouldBe(parentUser);
    }

    [Fact]
    public async Task Long_plan_after_14_days_keeps_access_until_the_end_without_a_refund()
    {
        const int teacherUser = 48121;
        var validFrom = DateTime.UtcNow.AddDays(-20);
        await SeedPurchaseAsync(teacherUser, SubscriptionPlan.TeacherProKey, PlanDuration.TwelveMonths, validFrom, 3000m);
        var teacher = await ClientAsAsync(teacherUser, "Teacher", $"kc-{teacherUser}", "Teacher");

        var res = await teacher.PostAsync("/api/subscription-plans/me/cancel", null);
        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        var cancelled = (await res.Content.ReadFromJsonAsync<PlanCancellationDto>(Json))!;
        cancelled.RefundAmount.ShouldBeNull();
        cancelled.AccessUntilUtc!.Value.ShouldBe(validFrom.AddMonths(12), TimeSpan.FromSeconds(1));

        var me = (await teacher.GetFromJsonAsync<CurrentSubscriptionPlanDto>("/api/subscription-plans/me", Json))!;
        me.PlanKey.ShouldBe(SubscriptionPlan.TeacherProKey);
        me.Purchase!.CancelledAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task Trial_admin_assignment_and_a_student_cannot_cancel()
    {
        const int teacherUser = 48131, studentUser = 48132;
        var teacher = await ClientAsAsync(teacherUser, "Teacher", $"kc-{teacherUser}", "Teacher");
        (await teacher.PostAsync("/api/subscription-plans/me/trial", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var res = await teacher.PostAsync("/api/subscription-plans/me/cancel", null);
        res.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(res)).ShouldBe(SubscriptionPlanErrorCodes.NothingToCancel);
        (await teacher.GetFromJsonAsync<CurrentSubscriptionPlanDto>("/api/subscription-plans/me", Json))!.Purchase.ShouldBeNull();

        await SeedPurchaseAsync(studentUser, SubscriptionPlan.StudentBasicKey, PlanDuration.Monthly, DateTime.UtcNow.AddDays(-1), 200m);
        var student = await ClientAsAsync(studentUser, "Student", $"kc-{studentUser}", "Student");
        var denied = await student.PostAsync("/api/subscription-plans/me/cancel", null);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(denied)).ShouldBe(SubscriptionPlanErrorCodes.ParentPays);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString();
}
