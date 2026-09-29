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
    public async Task Taxonomy_returns_400_when_gradeId_and_unassigned_are_both_supplied()
    {
        var admin = await ClientAsAsync(2, "Admin", realmRoles: "Admin");

        var res = await admin.GetAsync("/api/admin/taxonomy?gradeId=1&unassigned=true");

        res.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_admin_can_create_a_subject_and_read_it_back_in_the_tree()
    {
        var admin = await ClientAsAsync(2, "Admin", realmRoles: "Admin");
        var gradeId = await AddGradeAsync("9");

        var create = await admin.PostAsJsonAsync("/api/admin/subjects",
            new UpsertSubjectDto { Name = "Coğrafya", GradeIds = new List<int> { gradeId } });
        create.StatusCode.ShouldBe(HttpStatusCode.OK);

        var tree = await admin.GetFromJsonAsync<TaxonomyTreeDto>("/api/admin/taxonomy", Json);
        tree!.Subjects.ShouldContain(s => s.Name == "Coğrafya");

        await WithDbAsync(async db =>
            (await db.Subjects.AnyAsync(s => s.Name == "Coğrafya")).ShouldBeTrue());
    }

    [Fact]
    public async Task Creating_a_subject_without_a_grade_is_a_400_with_an_error_code()
    {
        var admin = await ClientAsAsync(2, "Admin", realmRoles: "Admin");

        var res = await admin.PostAsJsonAsync("/api/admin/subjects", new UpsertSubjectDto { Name = "Sahipsiz Ders" });

        res.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await res.Content.ReadFromJsonAsync<TaxonomyResponseDto>(Json);
        body!.ErrorCode.ShouldBe(TaxonomyErrorCodes.GradeRequired);
        body.Message.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Removing_a_grade_link_that_still_has_topics_is_a_400_with_an_error_code()
    {
        var admin = await ClientAsAsync(2, "Admin", realmRoles: "Admin");
        var (gradeId, subjectId) = await WithDbAsync(async db =>
        {
            var g = new Grade { Name = "6" };
            var other = new Grade { Name = "6b" };
            var s = new Subject { Name = "Bağlı Ders" };
            db.Grades.AddRange(g, other);
            db.Subjects.Add(s);
            await db.SaveChangesAsync();
            db.GradeSubjects.Add(new GradeSubject { GradeId = g.Id, SubjectId = s.Id });
            db.GradeSubjects.Add(new GradeSubject { GradeId = other.Id, SubjectId = s.Id }); // not the last link
            db.Topics.Add(new Topic { Name = "Konu", GradeId = g.Id, SubjectId = s.Id });
            await db.SaveChangesAsync();
            return (g.Id, s.Id);
        });

        var res = await admin.DeleteAsync($"/api/admin/subjects/{subjectId}/grades/{gradeId}");

        res.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await res.Content.ReadFromJsonAsync<TaxonomyResponseDto>(Json);
        body!.ErrorCode.ShouldBe(TaxonomyErrorCodes.SubjectGradeHasTopics);
        await WithDbAsync(async db =>
            (await db.GradeSubjects.AnyAsync(gs => gs.GradeId == gradeId && gs.SubjectId == subjectId)).ShouldBeTrue());
    }

    [Fact]
    public async Task Creating_a_topic_for_a_subject_not_linked_to_the_grade_is_a_400_with_an_error_code()
    {
        var admin = await ClientAsAsync(2, "Admin", realmRoles: "Admin");
        var (gradeId, subjectId) = await WithDbAsync(async db =>
        {
            var g = new Grade { Name = "5" };
            var s = new Subject { Name = "Bağsız Ders" };
            db.Grades.Add(g);
            db.Subjects.Add(s);
            await db.SaveChangesAsync(); // deliberately no GradeSubject link
            return (g.Id, s.Id);
        });

        var res = await admin.PostAsJsonAsync("/api/admin/topics",
            new UpsertTopicDto { Name = "Sahipsiz Konu", SubjectId = subjectId, GradeId = gradeId });

        res.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await res.Content.ReadFromJsonAsync<TaxonomyResponseDto>(Json);
        body!.ErrorCode.ShouldBe(TaxonomyErrorCodes.SubjectGradeNotLinked);
        await WithDbAsync(async db =>
            (await db.Topics.AnyAsync(t => t.Name == "Sahipsiz Konu")).ShouldBeFalse());
    }

    private Task<int> AddGradeAsync(string name) => WithDbAsync(async db =>
    {
        var g = new Grade { Name = name };
        db.Grades.Add(g);
        await db.SaveChangesAsync();
        return g.Id;
    });

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
