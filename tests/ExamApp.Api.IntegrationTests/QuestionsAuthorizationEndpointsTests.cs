using System.Net;
using System.Net.Http.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// issue #287 security review H1: <c>api/questions</c> gerçek pipeline + Postgres. Öğrenci hiçbir yazma ucuna erişemez;
/// öğretmen yalnızca kendi testinden soru çıkarabilir; BadgeService servis hesabı sınıflandırma ucunu çağırabilir.
/// </summary>
public class QuestionsAuthorizationEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const int OwnerId = 8101;
    private const int OtherTeacherId = 8102;

    private async Task<(int WorksheetId, int QuestionId)> SeedAsync()
    {
        await SeedApprovedTeacherAsync(OwnerId);
        await SeedApprovedTeacherAsync(OtherTeacherId);
        return await WithDbAsync(async db =>
        {
            var grade = new Grade { Name = "6" };
            db.Grades.Add(grade);
            await db.SaveChangesAsync();

            db.SetCurrentUser(OwnerId);
            var worksheet = new Worksheet { Name = "Sahibin testi", Description = "", GradeId = grade.Id };
            var question = new Question { Text = "Soru" };
            db.AddRange(worksheet, question);
            await db.SaveChangesAsync();
            db.TestQuestions.Add(new WorksheetQuestion { TestId = worksheet.Id, QuestionId = question.Id, Order = 1 });
            await db.SaveChangesAsync();
            return (worksheet.Id, question.Id);
        });
    }

    [Fact]
    public async Task Student_is_forbidden_on_every_question_write_endpoint()
    {
        var (worksheetId, questionId) = await SeedAsync();
        var student = await ClientAsAsync(8103, "Student", "kc-q-student", "Student");

        (await student.PostAsJsonAsync("/api/questions", new { id = questionId, text = "x", categoryName = "c" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await student.PostAsJsonAsync("/api/questions/save", new { imageData = "x", header = new { testId = worksheetId } }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await student.PostAsJsonAsync("/api/questions/attach-study-page", new { imageData = "x" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await student.PutAsJsonAsync($"/api/questions/{questionId}/correct-answer", new { correctAnswerId = 1, scale = 1 }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await student.PutAsJsonAsync($"/api/questions/{questionId}/classification", new { subjectId = 1 }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await student.DeleteAsync($"/api/questions/test/{worksheetId}/question/{questionId}"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await student.GetAsync($"/api/questions/bytest/{worksheetId}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Only_the_worksheet_owner_can_remove_a_question_from_it()
    {
        var (worksheetId, questionId) = await SeedAsync();

        var other = await ClientAsAsync(OtherTeacherId, "Teacher", "kc-q-other", "Teacher");
        (await other.DeleteAsync($"/api/questions/test/{worksheetId}/question/{questionId}"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await other.PutAsJsonAsync($"/api/questions/{questionId}/correct-answer", new { correctAnswerId = 1, scale = 1 }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var owner = await ClientAsAsync(OwnerId, "Teacher", "kc-q-owner", "Teacher");
        (await owner.DeleteAsync($"/api/questions/test/{worksheetId}/question/{questionId}"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Service_account_can_classify_any_question()
    {
        var (_, questionId) = await SeedAsync();

        var response = await ServiceClient().PutAsJsonAsync($"/api/questions/{questionId}/classification", new { difficulty = 3 });

        response.StatusCode.ShouldNotBe(HttpStatusCode.Forbidden);
        response.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized);
    }
}
