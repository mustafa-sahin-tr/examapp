using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// issue #289: gerçek pipeline + Postgres — admin öğretmen hesap onayını askıya alır / askıyı kaldırır. Askıdaki öğretmen
/// öğretmen uçlarında 403 TeacherNotApproved alır ve kendi durumunu (neden olmadan) görür; admin listesi askı alanlarını
/// taşır; aksiyonlar AdminUserActionLogs'a yazılır; admin olmayan çağıran 403 alır.
/// </summary>
public class TeacherSuspensionEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private static string NewSub(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    // FakeUserDirectory Respawn ile sıfırlanmaz → benzersiz, büyük UserId.
    private static int NewUserId() => 1_000_000 + Random.Shared.Next(0, 900_000_000);

    private async Task<(int TeacherId, int UserId)> SeedApprovedTeacherAsync()
    {
        var userId = NewUserId();
        Factory.Services.GetRequiredService<FakeUserDirectory>()
            .Add(new() { Id = userId, KeycloakId = NewSub("kc-target"), FullName = "Hedef Öğretmen" });
        var teacherId = await WithDbAsync(async db =>
        {
            var teacher = new Teacher
            {
                UserId = userId, ApprovalStatus = TeacherApprovalStatus.Approved, AccountApprovedAt = DateTime.UtcNow.AddDays(-3)
            };
            db.Teachers.Add(teacher);
            await db.SaveChangesAsync();
            return teacher.Id;
        });
        return (teacherId, userId);
    }

    private Task<HttpClient> AdminAsync() => ClientAsAsync(2, "Admin", NewSub("kc-admin"), "Admin");

    private static JsonContent Reason(string? reason) => JsonContent.Create(new { reason });

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Json);

    [Fact]
    public async Task Suspend_gates_the_teacher_and_unsuspend_restores_access()
    {
        var (teacherId, userId) = await SeedApprovedTeacherAsync();
        var teacher = await ClientAsAsync(userId, "Teacher", NewSub("kc-teacher"), "Teacher");
        (await teacher.GetAsync("/api/teacher/dashboard-summary")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var admin = await AdminAsync();
        var suspend = await admin.PostAsync($"/api/admin/teachers/{teacherId}/suspend", Reason("  Şikayet incelemesi  "));
        suspend.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await JsonOf(suspend);
        body.GetProperty("teacherId").GetInt32().ShouldBe(teacherId);
        body.GetProperty("accountSuspended").GetBoolean().ShouldBeTrue();
        body.GetProperty("accountApproved").GetBoolean().ShouldBeFalse();
        body.GetProperty("accountSuspendedAt").ValueKind.ShouldBe(JsonValueKind.String);
        body.GetProperty("accountApprovedAt").ValueKind.ShouldBe(JsonValueKind.Null);

        // Öğretmen: 403 TeacherNotApproved (askı ayrı bir kod olarak sızmaz).
        var denied = await teacher.GetAsync("/api/teacher/dashboard-summary");
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await JsonOf(denied)).GetProperty("errorCode").GetString().ShouldBe("TeacherNotApproved");

        // Öğretmenin kendi durumu: askıda, neden dönülmez.
        var checkResponse = await teacher.GetAsync("/api/teacher/check-teacher");
        var checkRaw = await checkResponse.Content.ReadAsStringAsync();
        var check = JsonDocument.Parse(checkRaw).RootElement;
        check.GetProperty("teacherAccountApproved").GetBoolean().ShouldBeFalse();
        check.GetProperty("teacherAccountSuspended").GetBoolean().ShouldBeTrue();
        checkRaw.ShouldNotContain("Şikayet");

        // Admin listesi askı alanlarını taşır.
        var list = await JsonOf(await admin.GetAsync("/api/admin/teachers?pageSize=100"));
        var row = list.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetInt32() == teacherId);
        row.GetProperty("accountApproved").GetBoolean().ShouldBeFalse();
        row.GetProperty("accountSuspended").GetBoolean().ShouldBeTrue();
        row.GetProperty("accountSuspendedAt").ValueKind.ShouldBe(JsonValueKind.String);
        row.GetProperty("accountSuspensionReason").GetString().ShouldBe("Şikayet incelemesi");

        var unsuspend = await admin.PostAsync($"/api/admin/teachers/{teacherId}/unsuspend", null);
        unsuspend.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonOf(unsuspend)).GetProperty("accountApproved").GetBoolean().ShouldBeTrue();

        (await teacher.GetAsync("/api/teacher/dashboard-summary")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var after = await JsonOf(await teacher.GetAsync("/api/teacher/check-teacher"));
        after.GetProperty("teacherAccountApproved").GetBoolean().ShouldBeTrue();
        after.GetProperty("teacherAccountSuspended").GetBoolean().ShouldBeFalse();

        var audits = await WithDbAsync(db => db.AdminUserActionLogs.AsNoTracking()
            .Where(a => a.TargetType == AdminUserTargetType.Teacher && a.TargetId == teacherId)
            .OrderBy(a => a.Id)
            .Select(a => new { a.Action, a.Outcome })
            .ToListAsync());
        audits.Select(a => (a.Action, a.Outcome)).ShouldBe([
            (AdminUserAction.TeacherSuspended, AdminUserActionOutcome.Succeeded),
            (AdminUserAction.TeacherUnsuspended, AdminUserActionOutcome.Succeeded)
        ]);
    }

    [Theory]
    [InlineData("Teacher")]
    [InlineData("Student")]
    public async Task Non_admin_gets_403_and_nothing_changes(string role)
    {
        var (teacherId, _) = await SeedApprovedTeacherAsync();
        var caller = await ClientAsAsync(NewUserId(), role, NewSub("kc-nonadmin"), role);

        (await caller.PostAsync($"/api/admin/teachers/{teacherId}/suspend", Reason("neden")))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await caller.PostAsync($"/api/admin/teachers/{teacherId}/unsuspend", null))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var row = await WithDbAsync(db => db.Teachers.AsNoTracking().SingleAsync(t => t.Id == teacherId));
        row.AccountApprovedAt.ShouldNotBeNull();
        row.AccountSuspendedAt.ShouldBeNull();
        (await WithDbAsync(db => db.AdminUserActionLogs.CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task Validation_not_found_and_state_errors_carry_errorCodes()
    {
        var (teacherId, _) = await SeedApprovedTeacherAsync();

        // Hesap durumu rate limit kovası testte admin başına 5 istek (IntegrationApiFactory) → her çağrı ayrı admin sub'ı.
        async Task<HttpResponseMessage> Post(string url, HttpContent? content) =>
            await (await AdminAsync()).PostAsync(url, content);

        async Task Expect(HttpResponseMessage response, HttpStatusCode code, string errorCode)
        {
            response.StatusCode.ShouldBe(code);
            var body = await JsonOf(response);
            body.GetProperty("errorCode").GetString().ShouldBe(errorCode);
            body.GetProperty("message").GetString().ShouldNotBeNullOrWhiteSpace();
        }

        await Expect(await Post($"/api/admin/teachers/{teacherId}/suspend", Reason("   ")),
            HttpStatusCode.BadRequest, "SuspensionReasonRequired");
        await Expect(await Post($"/api/admin/teachers/{teacherId}/suspend", Reason(new string('x', 501))),
            HttpStatusCode.BadRequest, "SuspensionReasonTooLong");
        await Expect(await Post("/api/admin/teachers/987654321/suspend", Reason("neden")),
            HttpStatusCode.NotFound, "TeacherNotFound");
        await Expect(await Post($"/api/admin/teachers/{teacherId}/unsuspend", null),
            HttpStatusCode.Conflict, "TeacherNotSuspended");

        (await Post($"/api/admin/teachers/{teacherId}/suspend", Reason("neden"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        await Expect(await Post($"/api/admin/teachers/{teacherId}/suspend", Reason("tekrar")),
            HttpStatusCode.Conflict, "TeacherAlreadySuspended");

        var pendingId = await WithDbAsync(async db =>
        {
            var pending = new Teacher { UserId = NewUserId(), ApprovalStatus = TeacherApprovalStatus.Pending };
            db.Teachers.Add(pending);
            await db.SaveChangesAsync();
            return pending.Id;
        });
        await Expect(await Post($"/api/admin/teachers/{pendingId}/suspend", Reason("neden")),
            HttpStatusCode.Conflict, "TeacherAccountNotApproved");
    }
}
