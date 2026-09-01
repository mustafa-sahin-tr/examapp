using System.Net;
using System.Net.Http.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.IntegrationTests;

public class ExamEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const string Kc = "kc-student";
    private const int UserId = 4200;

    private async Task<(int studentId, int worksheetId)> SeedStudentAndWorksheetAsync()
    {
        return await WithDbAsync(async db =>
        {
            var grade = new Grade { Name = "5" };
            db.Grades.Add(grade);
            await db.SaveChangesAsync();

            var student = new Student { UserId = UserId, StudentNumber = "S1", SchoolName = "Okul", GradeId = grade.Id };
            var worksheet = new Worksheet { Name = "Deneme", Description = "", GradeId = grade.Id, MaxDurationSeconds = 300 };
            var q = new Question { Text = "1+1?", Point = 10 };
            db.AddRange(student, worksheet, q);
            await db.SaveChangesAsync();

            db.TestQuestions.Add(new WorksheetQuestion { TestId = worksheet.Id, QuestionId = q.Id, Order = 1 });
            await db.SaveChangesAsync();
            return (student.Id, worksheet.Id);
        });
    }

    private Task<HttpClient> StudentClient() => ClientAsAsync(UserId, "Student", Kc, "Student");

    [Fact]
    public async Task Grades_endpoint_is_open_to_any_authenticated_user()
    {
        await WithDbAsync(async db => { db.Grades.Add(new Grade { Name = "1" }); await db.SaveChangesAsync(); });

        (await Anonymous().GetAsync("/api/worksheet/grades")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var client = await ClientAsAsync(1, "Teacher", "kc-t", "Teacher");
        var grades = await client.GetFromJsonAsync<List<Grade>>("/api/worksheet/grades", Json);
        grades!.ShouldContain(g => g.Name == "1");
    }

    [Fact]
    public async Task Latest_worksheets_lists_newest_first()
    {
        await WithDbAsync(async db =>
        {
            var g = new Grade { Name = "3" };
            db.Grades.Add(g);
            await db.SaveChangesAsync();
            db.Worksheets.Add(new Worksheet { Name = "Old", Description = "", GradeId = g.Id, CreateTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
            db.Worksheets.Add(new Worksheet { Name = "New", Description = "", GradeId = g.Id, CreateTime = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc) });
            await db.SaveChangesAsync();
        });

        var client = await ClientAsAsync(1, "Teacher", "kc-t", "Teacher");
        var page = await client.GetFromJsonAsync<List<WorksheetDto>>("/api/worksheet/latest?pageNumber=1&pageSize=1", Json);
        page!.Single().Name.ShouldBe("New");
    }

    [Fact]
    public async Task CompletedTests_requires_the_Student_role()
    {
        var teacher = await ClientAsAsync(1, "Teacher", "kc-t", "Teacher");
        (await teacher.GetAsync("/api/worksheet/CompletedTests")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_student_can_start_a_test_then_end_it()
    {
        var (_, worksheetId) = await SeedStudentAndWorksheetAsync();
        var student = await StudentClient();

        var start = await student.PostAsync($"/api/worksheet/start-test/{worksheetId}", null);
        start.StatusCode.ShouldBe(HttpStatusCode.OK);
        var startResult = await start.Content.ReadFromJsonAsync<TestStartResultDto>(Json);
        startResult!.InstanceId.ShouldBeGreaterThan(0);

        // the instance + one question row exist
        await WithDbAsync(async db =>
        {
            var inst = await db.TestInstances.Include(i => i.WorksheetInstanceQuestions)
                .FirstAsync(i => i.Id == startResult.InstanceId);
            inst.Status.ShouldBe(WorksheetInstanceStatus.Started);
            inst.WorksheetInstanceQuestions.Count.ShouldBe(1);
        });

        var end = await student.PutAsync($"/api/worksheet/end-test/{startResult.InstanceId}", null);
        end.StatusCode.ShouldBe(HttpStatusCode.OK);

        await WithDbAsync(async db =>
            (await db.TestInstances.FirstAsync(i => i.Id == startResult.InstanceId)).Status
                .ShouldBe(WorksheetInstanceStatus.Completed));
    }

    [Fact]
    public async Task Starting_the_same_test_twice_returns_the_same_open_instance()
    {
        var (_, worksheetId) = await SeedStudentAndWorksheetAsync();
        var student = await StudentClient();

        var first = await (await student.PostAsync($"/api/worksheet/start-test/{worksheetId}", null))
            .Content.ReadFromJsonAsync<TestStartResultDto>(Json);
        var second = await (await student.PostAsync($"/api/worksheet/start-test/{worksheetId}", null))
            .Content.ReadFromJsonAsync<TestStartResultDto>(Json);

        second!.InstanceId.ShouldBe(first!.InstanceId);
        await WithDbAsync(async db => (await db.TestInstances.CountAsync()).ShouldBe(1));
    }
}
