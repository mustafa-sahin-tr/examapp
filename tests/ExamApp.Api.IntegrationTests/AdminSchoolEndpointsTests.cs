using System.Net;
using System.Net.Http.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.IntegrationTests;

public class AdminSchoolEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Admin_school_endpoints_require_the_Admin_realm_role()
    {
        (await Anonymous().GetAsync("/api/admin/schools"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var teacher = await ClientAsAsync(1, "Teacher", realmRoles: "Teacher");
        (await teacher.GetAsync("/api/admin/schools"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var student = await ClientAsAsync(3, "Student", realmRoles: "Student");
        (await student.PostAsJsonAsync("/api/admin/schools", new UpsertSchoolDto { Name = "X" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var admin = await ClientAsAsync(2, "Admin", realmRoles: "Admin");
        (await admin.GetAsync("/api/admin/schools"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_admin_can_create_a_school_and_read_it_back()
    {
        var admin = await ClientAsAsync(2, "Admin", realmRoles: "Admin");

        var create = await admin.PostAsJsonAsync("/api/admin/schools", new UpsertSchoolDto { Name = "Konya Lisesi", AddressLine = "Meram Cad. No:5" });
        create.StatusCode.ShouldBe(HttpStatusCode.OK);

        var schools = await admin.GetFromJsonAsync<Paged<SchoolDto>>("/api/admin/schools?q=konya", Json);
        schools!.Items.ShouldContain(s => s.Name == "Konya Lisesi" && s.AddressLine == "Meram Cad. No:5");

        await WithDbAsync(async db =>
            (await db.Schools.AnyAsync(s => s.Name == "Konya Lisesi")).ShouldBeTrue());
    }

    [Fact]
    public async Task Creating_a_school_with_a_duplicate_name_case_insensitively_is_rejected()
    {
        var admin = await ClientAsAsync(2, "Admin", realmRoles: "Admin");
        (await admin.PostAsJsonAsync("/api/admin/schools", new UpsertSchoolDto { Name = "Ankara Lisesi" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var dup = await admin.PostAsJsonAsync("/api/admin/schools", new UpsertSchoolDto { Name = "ankara lisesi" });

        dup.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await WithDbAsync(async db =>
            (await db.Schools.CountAsync(s => s.Name.ToLower() == "ankara lisesi")).ShouldBe(1));
    }

    [Fact]
    public async Task Deleting_a_school_with_a_matching_teacher_schoolname_is_rejected()
    {
        var admin = await ClientAsAsync(2, "Admin", realmRoles: "Admin");

        var schoolId = await WithDbAsync(async db =>
        {
            var school = new School { Name = "Bursa Lisesi" };
            db.Schools.Add(school);
            db.Teachers.Add(new Teacher { UserId = 42, SchoolName = "bursa lisesi" });
            await db.SaveChangesAsync();
            return school.Id;
        });

        var delete = await admin.DeleteAsync($"/api/admin/schools/{schoolId}");

        delete.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await WithDbAsync(async db =>
            (await db.Schools.AnyAsync(s => s.Id == schoolId)).ShouldBeTrue());
    }

    [Fact]
    public async Task Deleting_an_unreferenced_school_succeeds()
    {
        var admin = await ClientAsAsync(2, "Admin", realmRoles: "Admin");

        var schoolId = await WithDbAsync(async db =>
        {
            var school = new School { Name = "Eskişehir Lisesi" };
            db.Schools.Add(school);
            await db.SaveChangesAsync();
            return school.Id;
        });

        var delete = await admin.DeleteAsync($"/api/admin/schools/{schoolId}");

        delete.StatusCode.ShouldBe(HttpStatusCode.OK);
        await WithDbAsync(async db =>
            (await db.Schools.AnyAsync(s => s.Id == schoolId)).ShouldBeFalse());
    }

    [Fact]
    public async Task The_public_school_list_is_reachable_without_authentication()
    {
        await WithDbAsync(async db =>
        {
            db.Schools.Add(new School { Name = "Public Lise" });
            await db.SaveChangesAsync();
        });

        var res = await Anonymous().GetAsync("/api/school");

        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await res.Content.ReadAsStringAsync();
        body.ShouldContain("Public Lise");
        body.ShouldNotContain("teacherCount", Case.Insensitive);
        body.ShouldNotContain("studentCount", Case.Insensitive);

        var schoolId = await WithDbAsync(db => db.Schools.Where(s => s.Name == "Public Lise").Select(s => s.Id).SingleAsync());
        (await Anonymous().GetAsync($"/api/school/{schoolId}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Anonymous().GetAsync($"/api/school/{schoolId + 1000}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_public_school_list_filters_by_province_and_lists_locations_issue1106()
    {
        var (provinceId, districtId) = await WithDbAsync(async db =>
        {
            var province = new Province { Name = "Filtre İli" };
            var district = new District { Name = "Filtre İlçesi", Province = province };
            db.Schools.Add(new School { Name = "İçerideki Lise", Province = province, District = district });
            db.Schools.Add(new School { Name = "Dışarıdaki Lise" });
            await db.SaveChangesAsync();
            return (province.Id, district.Id);
        });

        var inside = await (await Anonymous().GetAsync($"/api/school?provinceId={provinceId}&districtId={districtId}")).Content.ReadAsStringAsync();
        inside.ShouldContain("İçerideki Lise");
        inside.ShouldNotContain("Dışarıdaki Lise");

        (await (await Anonymous().GetAsync("/api/school/provinces")).Content.ReadAsStringAsync()).ShouldContain("Filtre İli");
        (await (await Anonymous().GetAsync($"/api/school/districts?provinceId={provinceId}")).Content.ReadAsStringAsync())
            .ShouldContain("Filtre İlçesi");
    }
}
