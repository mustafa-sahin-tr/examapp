using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// issue #287: gerçek pipeline + Postgres — onaysız öğretmen öğretmen uçlarında 403 + TeacherNotApproved alır,
/// kendi kaydını/public veriyi okumaya devam eder; admin onayından sonra erişim açılır. Admin ve öğrenci karma
/// uçlarda etkilenmez; onaylı okul öğretmeni bağımsız tutor başvurusu beklerken erişimini kaybetmez.
/// </summary>
public class TeacherAccountApprovalGateEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private static string NewSub(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private async Task<int> SeedTeacherAsync(int userId, TeacherApprovalStatus status, DateTime? accountApprovedAt,
        bool independent = false)
        => await WithDbAsync(async db =>
        {
            var teacher = new Teacher
            {
                UserId = userId, IsIndependentTutor = independent, ApprovalStatus = status,
                AccountApprovedAt = accountApprovedAt
            };
            db.Teachers.Add(teacher);
            await db.SaveChangesAsync();
            return teacher.Id;
        });

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Json);

    [Fact]
    public async Task Pending_teacher_is_gated_until_an_admin_approves_the_account()
    {
        const int userId = 6001;
        var teacherId = await SeedTeacherAsync(userId, TeacherApprovalStatus.Pending, accountApprovedAt: null);
        var teacher = await ClientAsAsync(userId, "Teacher", NewSub("kc-pending"), "Teacher");

        var denied = await teacher.GetAsync("/api/teacher/dashboard-summary");
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var body = await JsonOf(denied);
        body.GetProperty("success").GetBoolean().ShouldBeFalse();
        body.GetProperty("errorCode").GetString().ShouldBe("TeacherNotApproved");
        body.GetProperty("message").GetString().ShouldNotBeNullOrWhiteSpace();

        (await teacher.GetAsync("/api/worksheet/list")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Kendi kaydı + public veri açık kalır.
        var check = await JsonOf(await teacher.GetAsync("/api/teacher/check-teacher"));
        check.GetProperty("teacherAccountApproved").GetBoolean().ShouldBeFalse();
        check.GetProperty("teacherApplicationStatus").GetString().ShouldBe("Pending");
        (await teacher.GetAsync("/api/worksheet/grades")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var admin = await ClientAsAsync(7001, "Admin", NewSub("kc-admin"), "Admin");
        (await admin.PostAsync($"/api/admin/teacher-applications/{teacherId}/approve", null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await teacher.GetAsync("/api/teacher/dashboard-summary")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var after = await JsonOf(await teacher.GetAsync("/api/teacher/check-teacher"));
        after.GetProperty("teacherAccountApproved").GetBoolean().ShouldBeTrue();
        after.GetProperty("teacherApplicationStatus").GetString().ShouldBe("Approved");
    }

    [Fact]
    public async Task Rejected_first_time_applicant_stays_gated_and_sees_the_reason()
    {
        const int userId = 6002;
        await WithDbAsync(async db =>
        {
            db.Teachers.Add(new Teacher
            {
                UserId = userId, ApprovalStatus = TeacherApprovalStatus.Rejected, RejectionReason = "Belge eksik"
            });
            await db.SaveChangesAsync();
        });
        var teacher = await ClientAsAsync(userId, "Teacher", NewSub("kc-rejected"), "Teacher");

        (await teacher.GetAsync("/api/question-transfer/jobs")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var check = await JsonOf(await teacher.GetAsync("/api/teacher/check-teacher"));
        check.GetProperty("teacherAccountApproved").GetBoolean().ShouldBeFalse();
        check.GetProperty("teacherApplicationStatus").GetString().ShouldBe("Rejected");
        check.GetProperty("rejectionReason").GetString().ShouldBe("Belge eksik");
    }

    [Fact]
    public async Task Approved_school_teacher_with_a_pending_tutor_application_keeps_teacher_access()
    {
        const int userId = 6003;
        await SeedTeacherAsync(userId, TeacherApprovalStatus.Pending, accountApprovedAt: DateTime.UtcNow, independent: true);
        var teacher = await ClientAsAsync(userId, "Teacher", NewSub("kc-switch"), "Teacher");

        (await teacher.GetAsync("/api/teacher/dashboard-summary")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var check = await JsonOf(await teacher.GetAsync("/api/teacher/check-teacher"));
        check.GetProperty("teacherAccountApproved").GetBoolean().ShouldBeTrue();
        check.GetProperty("teacherApplicationStatus").GetString().ShouldBe("Pending");
    }

    [Fact]
    public async Task Admin_and_student_are_unaffected_on_mixed_role_endpoints()
    {
        var admin = await ClientAsAsync(7002, "Admin", NewSub("kc-admin"), "Admin");
        (await admin.GetAsync("/api/question-transfer/jobs")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var student = await ClientAsAsync(7003, "Student", NewSub("kc-student"), "Student");
        (await student.GetAsync("/api/study-pages?pageNumber=1&pageSize=10")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
