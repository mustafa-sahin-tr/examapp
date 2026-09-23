using System.Net;
using System.Net.Http.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #152: GET api/admin/teachers — gerçek pipeline + Postgres. Test ortamında auth-api/Keycloak yok;
/// bu yüzden liste fail-soft döner (ad/e-posta boş, isEnabled null) — kabul kriterindeki "auth-api erişilemezken
/// liste döner" durumunu da uçtan uca kapsar.
/// </summary>
public class AdminTeacherEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Teachers_list_requires_the_Admin_realm_role()
    {
        (await Anonymous().GetAsync("/api/admin/teachers"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var teacher = await ClientAsAsync(1, "Teacher", "kc-teacher", realmRoles: "Teacher");
        (await teacher.GetAsync("/api/admin/teachers"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var student = await ClientAsAsync(3, "Student", "kc-student", realmRoles: "Student");
        (await student.GetAsync("/api/admin/teachers"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var admin = await ClientAsAsync(2, "Admin", "kc-admin", realmRoles: "Admin");
        (await admin.GetAsync("/api/admin/teachers"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Paginates_and_filters_by_school_and_stays_up_without_auth_api()
    {
        var (schoolA, schoolB) = await WithDbAsync(async db =>
        {
            var a = new School { Name = "Ankara Lisesi" };
            var b = new School { Name = "Bursa Lisesi" };
            db.Schools.AddRange(a, b);
            await db.SaveChangesAsync();
            for (var i = 0; i < 25; i++)
                db.Teachers.Add(new Teacher { UserId = 1000 + i, SchoolId = a.Id });
            for (var i = 0; i < 3; i++)
                db.Teachers.Add(new Teacher { UserId = 2000 + i, SchoolId = b.Id });
            db.Teachers.Add(new Teacher { UserId = 3000, IsIndependentTutor = true, ApprovalStatus = TeacherApprovalStatus.Pending });
            await db.SaveChangesAsync();
            return (a.Id, b.Id);
        });
        var admin = await ClientAsAsync(2, "Admin", "kc-admin", realmRoles: "Admin");

        var page1 = await admin.GetFromJsonAsync<Paged<AdminTeacherListItemDto>>($"/api/admin/teachers?schoolId={schoolA}", Json);
        var page2 = await admin.GetFromJsonAsync<Paged<AdminTeacherListItemDto>>($"/api/admin/teachers?schoolId={schoolA}&page=2&pageSize=20", Json);
        page1!.TotalCount.ShouldBe(25);
        page1.PageSize.ShouldBe(20);
        page1.Items.Count.ShouldBe(20);
        page2!.Items.Count.ShouldBe(5);
        page1.Items.Concat(page2.Items).Select(i => i.UserId).ShouldBe(Enumerable.Range(1000, 25));

        var bursa = await admin.GetFromJsonAsync<Paged<AdminTeacherListItemDto>>($"/api/admin/teachers?schoolId={schoolB}", Json);
        bursa!.TotalCount.ShouldBe(3);
        bursa.Items.ShouldAllBe(i => i.SchoolId == schoolB && i.SchoolName == "Bursa Lisesi" && i.ApprovalStatus == "Approved");
        // auth-api test ortamında yok → fail-soft
        bursa.Items.ShouldAllBe(i => i.IsEnabled == null && i.FullName == string.Empty);

        var unassigned = await admin.GetFromJsonAsync<Paged<AdminTeacherListItemDto>>("/api/admin/teachers?unassigned=true", Json);
        unassigned!.TotalCount.ShouldBe(1);
        unassigned.Items.Single().ApprovalStatus.ShouldBe("Pending");

        var all = await admin.GetFromJsonAsync<Paged<AdminTeacherListItemDto>>("/api/admin/teachers?pageSize=1000", Json);
        all!.TotalCount.ShouldBe(29);
        all.PageSize.ShouldBe(100);
        all.Items.Count.ShouldBe(29);
    }

    [Fact]
    public async Task Teachers_list_is_not_cacheable()
    {
        var admin = await ClientAsAsync(2, "Admin", "kc-admin", realmRoles: "Admin");

        var response = await admin.GetAsync("/api/admin/teachers");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl.ShouldNotBeNull();
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task Huge_page_number_returns_200_with_an_empty_page_not_500()
    {
        await WithDbAsync(async db =>
        {
            db.Teachers.Add(new Teacher { UserId = 4000 });
            await db.SaveChangesAsync();
        });
        var admin = await ClientAsAsync(2, "Admin", "kc-admin", realmRoles: "Admin");

        var response = await admin.GetAsync($"/api/admin/teachers?page={int.MaxValue}&pageSize=100");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<Paged<AdminTeacherListItemDto>>(Json);
        body!.TotalCount.ShouldBe(1);
        body.Items.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Non_positive_schoolId_is_400(int schoolId)
    {
        var admin = await ClientAsAsync(2, "Admin", "kc-admin", realmRoles: "Admin");

        (await admin.GetAsync($"/api/admin/teachers?schoolId={schoolId}"))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SchoolId_with_unassigned_is_400()
    {
        var admin = await ClientAsAsync(2, "Admin", "kc-admin", realmRoles: "Admin");

        (await admin.GetAsync("/api/admin/teachers?schoolId=1&unassigned=true"))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
