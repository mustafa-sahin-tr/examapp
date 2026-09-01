using System.Net;
using System.Net.Http.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos.Admin;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.IntegrationTests;

public class AdminTaxonomyEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Taxonomy_requires_the_Admin_realm_role()
    {
        (await Anonymous().GetAsync("/api/admin/taxonomy"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var teacher = await ClientAsAsync(1, "Teacher", realmRoles: "Teacher");
        (await teacher.GetAsync("/api/admin/taxonomy"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var admin = await ClientAsAsync(2, "Admin", realmRoles: "Admin");
        (await admin.GetAsync("/api/admin/taxonomy"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_admin_can_create_a_subject_and_read_it_back_in_the_tree()
    {
        var admin = await ClientAsAsync(2, "Admin", realmRoles: "Admin");

        var create = await admin.PostAsJsonAsync("/api/admin/subjects", new UpsertSubjectDto { Name = "Coğrafya" });
        create.StatusCode.ShouldBe(HttpStatusCode.OK);

        var tree = await admin.GetFromJsonAsync<TaxonomyTreeDto>("/api/admin/taxonomy", Json);
        tree!.Subjects.ShouldContain(s => s.Name == "Coğrafya");

        await WithDbAsync(async db =>
            (await db.Subjects.AnyAsync(s => s.Name == "Coğrafya")).ShouldBeTrue());
    }

    [Fact]
    public async Task Creating_a_topic_under_a_missing_subject_is_a_400()
    {
        var admin = await ClientAsAsync(2, "Admin", realmRoles: "Admin");

        var gradeId = await WithDbAsync(async db =>
        {
            var g = new Grade { Name = "7" };
            db.Grades.Add(g);
            await db.SaveChangesAsync();
            return g.Id;
        });

        var res = await admin.PostAsJsonAsync("/api/admin/topics",
            new UpsertTopicDto { Name = "X", SubjectId = 99999, GradeId = gradeId });

        res.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Classifier_cache_status_is_reachable_and_reports_missing_key()
    {
        var admin = await ClientAsAsync(2, "Admin", realmRoles: "Admin");

        var status = await admin.GetFromJsonAsync<ClassifierCacheStatusDto>("/api/admin/classifier-cache", Json);
        status!.ConfiguredInSettings.ShouldBeFalse();
        status.Stale.ShouldBeTrue();
    }

    [Fact]
    public async Task The_classifier_cache_pointer_endpoint_is_service_only()
    {
        (await Anonymous().GetAsync("/api/questions/classifier-cache"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var svc = ServiceClient();
        var res = await svc.GetAsync("/api/questions/classifier-cache");
        res.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
