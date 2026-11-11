using System.Net;
using System.Net.Http.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #153: GET api/admin/students — gerçek pipeline + Postgres. Test ortamında auth-api/Keycloak yok;
/// bu yüzden liste fail-soft döner (ad/e-posta boş, isEnabled null).
/// </summary>
public class AdminStudentEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    // issue #246: rate limit kovası sub başına — her test kendi admin kimliğiyle.
    private static string NewAdminSub() => $"kc-admin-{Guid.NewGuid():N}";

    [Fact]
    public async Task Students_list_requires_the_Admin_realm_role()
    {
        (await Anonymous().GetAsync("/api/admin/students"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var teacher = await ClientAsAsync(1, "Teacher", "kc-teacher", realmRoles: "Teacher");
        (await teacher.GetAsync("/api/admin/students"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var student = await ClientAsAsync(3, "Student", "kc-student", realmRoles: "Student");
        (await student.GetAsync("/api/admin/students"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var admin = await ClientAsAsync(2, "Admin", NewAdminSub(), realmRoles: "Admin");
        (await admin.GetAsync("/api/admin/students"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Paginates_and_filters_by_school_and_stays_up_without_auth_api()
    {
        var (schoolA, schoolB, gradeId) = await WithDbAsync(async db =>
        {
            var a = new School { Name = "Ankara Lisesi" };
            var b = new School { Name = "Bursa Lisesi" };
            var g = new Grade { Name = "9. Sınıf" };
            db.Schools.AddRange(a, b);
            db.Grades.Add(g);
            await db.SaveChangesAsync();
            for (var i = 0; i < 25; i++)
                db.Students.Add(new Student { UserId = 1000 + i, StudentNumber = $"2026{i:D4}", SchoolId = a.Id, SchoolVerifiedAt = DateTime.UtcNow });
            for (var i = 0; i < 3; i++)
                db.Students.Add(new Student { UserId = 2000 + i, StudentNumber = $"3026{i:D4}", SchoolId = b.Id, SchoolVerifiedAt = DateTime.UtcNow, GradeId = g.Id });
            db.Students.Add(new Student { UserId = 3000, StudentNumber = "20269999" });
            db.Students.Add(new Student { UserId = 3001, StudentNumber = "D0", SchoolId = b.Id, SchoolVerifiedAt = DateTime.UtcNow, IsDeleted = true });
            await db.SaveChangesAsync();
            return (a.Id, b.Id, g.Id);
        });
        var admin = await ClientAsAsync(2, "Admin", NewAdminSub(), realmRoles: "Admin");

        var page1 = await admin.GetFromJsonAsync<Paged<AdminStudentListItemDto>>($"/api/admin/students?schoolId={schoolA}", Json);
        var page2 = await admin.GetFromJsonAsync<Paged<AdminStudentListItemDto>>($"/api/admin/students?schoolId={schoolA}&page=2&pageSize=20", Json);
        page1!.TotalCount.ShouldBe(25);
        page1.PageSize.ShouldBe(20);
        page1.Items.Count.ShouldBe(20);
        page2!.Items.Count.ShouldBe(5);
        page1.Items.Concat(page2.Items).Select(i => i.StudentNumber).ShouldBe(Enumerable.Range(0, 25).Select(i => $"****{i:D4}")); // issue #262: kısmi
        page1.Items.ShouldAllBe(i => i.GradeId == null && i.GradeName == null);

        var bursa = await admin.GetFromJsonAsync<Paged<AdminStudentListItemDto>>($"/api/admin/students?schoolId={schoolB}", Json);
        bursa!.TotalCount.ShouldBe(3); // soft-deleted hariç
        bursa.Items.ShouldAllBe(i => i.SchoolId == schoolB && i.SchoolName == "Bursa Lisesi"
                                     && i.GradeId == gradeId && i.GradeName == "9. Sınıf");
        // auth-api test ortamında yok → fail-soft
        bursa.Items.ShouldAllBe(i => i.IsEnabled == null && i.FullName == string.Empty && i.Email == string.Empty);

        var unassigned = await admin.GetFromJsonAsync<Paged<AdminStudentListItemDto>>("/api/admin/students?unassigned=true", Json);
        unassigned!.TotalCount.ShouldBe(1);
        unassigned.Items.Single().StudentNumber.ShouldBe("****9999");

        var all = await admin.GetFromJsonAsync<Paged<AdminStudentListItemDto>>("/api/admin/students?pageSize=1000", Json);
        all!.TotalCount.ShouldBe(29);
        all.PageSize.ShouldBe(100);
        all.Items.Count.ShouldBe(29);
    }

    [Fact]
    public async Task Response_uses_the_agreed_camelCase_contract()
    {
        await WithDbAsync(async db =>
        {
            db.Students.Add(new Student { UserId = 5000, StudentNumber = "1234" });
            await db.SaveChangesAsync();
        });
        var admin = await ClientAsAsync(2, "Admin", NewAdminSub(), realmRoles: "Admin");

        var response = await admin.GetAsync("/api/admin/students?page=1&pageSize=20");
        response.Headers.CacheControl.ShouldNotBeNull();
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var json = await response.Content.ReadAsStringAsync();

        json.ShouldNotContain("\"userId\""); // reşit olmayan kullanıcının iç kimliği dönülmez
        foreach (var key in new[] { "\"pageNumber\"", "\"pageSize\"", "\"totalCount\"", "\"items\"", "\"id\"",
                     "\"fullName\"", "\"email\"", "\"studentNumber\"", "\"schoolId\"", "\"schoolName\"", "\"gradeId\"",
                     "\"gradeName\"", "\"isEnabled\"" })
            json.ShouldContain(key);
    }

    [Fact]
    public async Task Huge_page_number_returns_200_with_an_empty_page_not_500()
    {
        await WithDbAsync(async db =>
        {
            db.Students.Add(new Student { UserId = 4000, StudentNumber = "X" });
            await db.SaveChangesAsync();
        });
        var admin = await ClientAsAsync(2, "Admin", NewAdminSub(), realmRoles: "Admin");

        var response = await admin.GetAsync($"/api/admin/students?page={int.MaxValue}&pageSize=100");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<Paged<AdminStudentListItemDto>>(Json);
        body!.TotalCount.ShouldBe(1);
        body.Items.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Non_positive_schoolId_is_400(int schoolId)
    {
        var admin = await ClientAsAsync(2, "Admin", NewAdminSub(), realmRoles: "Admin");

        (await admin.GetAsync($"/api/admin/students?schoolId={schoolId}"))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SchoolId_with_unassigned_is_400()
    {
        var admin = await ClientAsAsync(2, "Admin", NewAdminSub(), realmRoles: "Admin");

        (await admin.GetAsync("/api/admin/students?schoolId=1&unassigned=true"))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>Issue #1110: liste geri alınabilir sınıf atlamasını gösterir; geri alma öğrenciyi eski sınıfına döndürür, ikincisi 404.</summary>
    [Fact]
    public async Task Admin_reverts_a_grade_promotion_issue1110()
    {
        var (studentId, fromGrade) = await WithDbAsync(async db =>
        {
            var seventh = new Grade { Name = "7. Sınıf" };
            var eighth = new Grade { Name = "8. Sınıf" };
            db.Grades.AddRange(seventh, eighth);
            await db.SaveChangesAsync();
            var student = new Student { UserId = 4100, StudentNumber = "P4100", GradeId = eighth.Id };
            db.Students.Add(student);
            await db.SaveChangesAsync();
            db.StudentGradePromotions.Add(new StudentGradePromotion
            {
                StudentId = student.Id, Year = 2026, FromGradeId = seventh.Id, ToGradeId = eighth.Id, PromotedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            return (student.Id, seventh.Id);
        });
        var admin = await ClientAsAsync(2, "Admin", NewAdminSub(), realmRoles: "Admin");

        var listed = (await admin.GetFromJsonAsync<Paged<AdminStudentListItemDto>>("/api/admin/students?pageSize=1000", Json))!
            .Items.Single(i => i.Id == studentId);
        (listed.PromotionYear, listed.PromotedFromGradeName).ShouldBe(((int?)2026, "7. Sınıf"));

        (await admin.PostAsync($"/api/admin/students/{studentId}/grade-promotion/revert", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await WithDbAsync(db => Task.FromResult(db.Students.Single(s => s.Id == studentId).GradeId))).ShouldBe(fromGrade);
        (await admin.PostAsync($"/api/admin/students/{studentId}/grade-promotion/revert", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var after = (await admin.GetFromJsonAsync<Paged<AdminStudentListItemDto>>("/api/admin/students?pageSize=1000", Json))!
            .Items.Single(i => i.Id == studentId);
        after.PromotionYear.ShouldBeNull();

        var teacher = await ClientAsAsync(1, "Teacher", "kc-teacher", realmRoles: "Teacher");
        (await teacher.PostAsync($"/api/admin/students/{studentId}/grade-promotion/revert", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
