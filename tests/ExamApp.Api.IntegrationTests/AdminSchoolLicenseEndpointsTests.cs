using System.Net;
using System.Net.Http.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.IntegrationTests;

/// <summary>Issue #1092: okul lisansı uçları — yetki, oluşturma ve bitirme, öğrencinin <c>/me</c>'sine yansıma, audit.</summary>
public class AdminSchoolLicenseEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const int StudentUserId = 9201;

    private async Task<int> SeedSchoolWithStudentAsync()
    {
        var schoolId = 0;
        await WithDbAsync(async db =>
        {
            var school = new School { Name = "Lisans Ortaokulu" };
            db.Schools.Add(school);
            await db.SaveChangesAsync();
            db.Students.Add(new Student { UserId = StudentUserId, StudentNumber = "L1", SchoolId = school.Id, SchoolVerifiedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
            schoolId = school.Id;
        });
        return schoolId;
    }

    private static string Url(int schoolId) => $"/api/admin/subscription-plans/schools/{schoolId}/license";

    [Fact]
    public async Task Only_an_admin_can_read_or_change_a_license()
    {
        var schoolId = await SeedSchoolWithStudentAsync();
        var teacher = await ClientAsAsync(51, "Teacher", "kc-lic-teacher", "Teacher");

        (await teacher.GetAsync(Url(schoolId))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await teacher.PutAsJsonAsync(Url(schoolId), new SaveSchoolLicenseDto())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Anonymous().PostAsync(Url(schoolId) + "/end", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_license_gives_members_pro_until_it_is_ended()
    {
        var schoolId = await SeedSchoolWithStudentAsync();
        var admin = await ClientAsAsync(2, "Admin", "kc-lic-admin", "Admin");
        var student = await ClientAsAsync(StudentUserId, "Student", "kc-lic-student", "Student");

        var saved = await admin.PutAsJsonAsync(Url(schoolId), new SaveSchoolLicenseDto { Note = "Sözleşme 42" });
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await saved.Content.ReadFromJsonAsync<SchoolLicenseDto>(Json))!.ShouldSatisfyAllConditions(
            l => l.Active.ShouldBeTrue(), l => l.StudentPlanKey.ShouldBe(SubscriptionPlan.StudentProKey), l => l.History.Count.ShouldBe(1));

        var me = await student.GetFromJsonAsync<CurrentSubscriptionPlanDto>("/api/subscription-plans/me", Json);
        me!.PlanKey.ShouldBe(SubscriptionPlan.StudentProKey);
        me.SchoolLicense.ShouldBeTrue();

        (await admin.PostAsync(Url(schoolId) + "/end", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        me = await student.GetFromJsonAsync<CurrentSubscriptionPlanDto>("/api/subscription-plans/me", Json);
        me!.SchoolLicense.ShouldBeFalse();
        me.PlanKey.ShouldNotBe(SubscriptionPlan.StudentProKey);

        (await admin.PostAsync(Url(schoolId) + "/end", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await WithDbAsync(async db =>
            (await db.SchoolLicenseAudits.Where(a => a.SchoolId == schoolId).Select(a => a.ActorUserId).ToListAsync()).ShouldBe([2, 2]));
    }

    [Fact]
    public async Task Unknown_school_and_bad_input_are_rejected()
    {
        var schoolId = await SeedSchoolWithStudentAsync();
        var admin = await ClientAsAsync(2, "Admin", "kc-lic-admin", "Admin");

        (await admin.GetAsync(Url(999_999))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var bad = await admin.PutAsJsonAsync(Url(schoolId), new SaveSchoolLicenseDto { StudentPlanKey = SubscriptionPlan.TeacherProKey });
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await bad.Content.ReadAsStringAsync()).ShouldContain("subscription.audienceMismatch");
    }
}
