using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Services.Worksheets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// issue #583 on real PostgreSQL + HTTP: açık uçlu cevap — öğrenci kontrol eder, atamayı yapan (onaylı, aynı okul) öğretmen
/// <c>GET/PUT api/open-text-reviews</c> ile değerlendirir, öğrenci <c>test-canvas-instance</c> yanıtında görür; öğrenci kuyruğa
/// erişemez (403), atamasız öğretmen 404 alır; metin değişince değerlendirme eskir (ExecuteUpdate), yeniden çözme siler
/// (ExecuteDelete); hiçbir outbox satırı yazılmaz.
/// </summary>
public class OpenTextReviewPostgresTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private static int NewUserId() => 1_000_000 + Random.Shared.Next(0, 900_000_000);

    private sealed record Seed(int StudentUserId, int TeacherUserId, int StrangerUserId, int SchoolId, int InstanceId, int RowId);

    private async Task<Seed> SeedAsync()
    {
        var studentUserId = NewUserId();
        var teacherUserId = NewUserId();
        var strangerUserId = NewUserId();
        var (schoolId, instanceId, rowId) = await WithDbAsync(async db =>
        {
            var grade = new Grade { Name = "5" };
            var school = new School { Name = "Okul" };
            db.AddRange(grade, school);
            await db.SaveChangesAsync();
            var student = new Student
            {
                UserId = studentUserId, StudentNumber = $"S{studentUserId}", SchoolName = "Okul", SchoolId = school.Id,
                SchoolVerifiedAt = DateTime.UtcNow, GradeId = grade.Id,
            };
            var question = new Question
            {
                Text = "Neden?", Point = 10, DifficultyLevel = 1, InteractionType = OpenTextGrader.Type,
                InteractionPlan = """{"version":1,"type":"openText","maxWords":50}""",
                InteractionSolution = """{"sampleAnswer":"Örnek","rubric":"Gizli rubrik"}""",
            };
            db.AddRange(student, question);
            db.SetCurrentUser(teacherUserId);
            var worksheet = new Worksheet { Name = "Açık uçlu", Description = "", GradeId = grade.Id, Kind = WorksheetKind.Activity };
            db.Worksheets.Add(worksheet);
            await db.SaveChangesAsync();
            var wq = new WorksheetQuestion { TestId = worksheet.Id, QuestionId = question.Id, Order = 1 };
            db.TestQuestions.Add(wq);
            db.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = worksheet.Id, StudentId = student.Id, StartAt = DateTime.UtcNow.AddDays(-1),
            });
            var instance = new WorksheetInstance
            {
                StudentId = student.Id, WorksheetId = worksheet.Id, StartTime = DateTime.UtcNow.AddMinutes(-5),
                Status = WorksheetInstanceStatus.Started,
            };
            db.TestInstances.Add(instance);
            await db.SaveChangesAsync();
            var row = new WorksheetInstanceQuestion { WorksheetInstanceId = instance.Id, WorksheetQuestionId = wq.Id };
            db.TestInstanceQuestions.Add(row);
            await db.SaveChangesAsync();
            return (school.Id, instance.Id, row.Id);
        });
        await SeedApprovedTeacherAsync(teacherUserId, schoolId);
        await SeedApprovedTeacherAsync(strangerUserId, schoolId);
        return new Seed(studentUserId, teacherUserId, strangerUserId, schoolId, instanceId, rowId);
    }

    private static string Payload(string text) => JsonSerializer.Serialize(new { version = 1, type = "openText", text });

    private async Task CheckAsync(Seed s, string text)
    {
        using var scope = Factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ITestSessionService>().CheckActivityAsync(s.InstanceId,
            new ActivityCheckRequestDto { Answers = [new() { InstanceQuestionId = s.RowId, AnswerPayload = Payload(text) }] },
            s.StudentUserId);
        result.Success.ShouldBeTrue(result.Message);
    }

    private Task<HttpClient> TeacherAsync(Seed s, int userId) =>
        ClientAsWithSchoolAsync(userId, "Teacher", $"kc-t-{userId}", s.SchoolId, "Teacher");

    [Fact]
    public async Task Teacher_reviews_over_http_and_student_sees_it_without_points()
    {
        var s = await SeedAsync();
        await CheckAsync(s, "Su ısınınca buharlaşır.");
        var teacher = await TeacherAsync(s, s.TeacherUserId);

        var queue = await teacher.GetFromJsonAsync<JsonElement>("/api/open-text-reviews?status=pending");
        queue.GetProperty("totalCount").GetInt32().ShouldBe(1);
        var item = queue.GetProperty("items")[0];
        item.GetProperty("text").GetString().ShouldBe("Su ısınınca buharlaşır.");
        item.GetProperty("rubric").GetString().ShouldBe("Gizli rubrik");
        var hash = item.GetProperty("answerHash").GetString();

        var put = await teacher.PutAsJsonAsync($"/api/open-text-reviews/{s.RowId}",
            new { evaluation = "insufficient", feedback = "<b>Isıyı</b> anlat.", answerHash = hash });
        put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());

        var student = await ClientAsAsync(s.StudentUserId, "Student", $"kc-s-{s.StudentUserId}", "Student");
        var session = await student.GetFromJsonAsync<JsonElement>($"/api/worksheet/test-canvas-instance/{s.InstanceId}");
        var review = session.GetProperty("testInstanceQuestions")[0].GetProperty("openTextReview");
        review.GetProperty("status").GetString().ShouldBe("reviewed");
        review.GetProperty("evaluation").GetString().ShouldBe("insufficient");
        review.GetProperty("feedback").GetString().ShouldBe("<b>Isıyı</b> anlat.");   // düz metin olarak saklanır; istemci kaçışlar

        (await student.GetAsync("/api/open-text-reviews")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await WithDbAsync(db => db.OutboxMessages.CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task Stranger_teacher_gets_404_and_changed_text_makes_review_stale_and_retake_deletes_it()
    {
        var s = await SeedAsync();
        await CheckAsync(s, "Su ısınınca buharlaşır.");
        var teacher = await TeacherAsync(s, s.TeacherUserId);
        var hash = (await teacher.GetFromJsonAsync<JsonElement>("/api/open-text-reviews")).GetProperty("items")[0]
            .GetProperty("answerHash").GetString();

        var stranger = await TeacherAsync(s, s.StrangerUserId);
        (await stranger.PutAsJsonAsync($"/api/open-text-reviews/{s.RowId}", new { evaluation = "partial", answerHash = hash }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await teacher.PutAsJsonAsync($"/api/open-text-reviews/{s.RowId}", new { evaluation = "partial", answerHash = hash }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        await CheckAsync(s, "Güneş suyu ısıtır.");
        (await WithDbAsync(db => db.OpenTextReviews.AsNoTracking().SingleAsync())).Stale.ShouldBeTrue();
        (await teacher.PutAsJsonAsync($"/api/open-text-reviews/{s.RowId}", new { evaluation = "partial", answerHash = hash }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await teacher.GetFromJsonAsync<JsonElement>("/api/open-text-reviews")).GetProperty("items")[0]
            .GetProperty("status").GetString().ShouldBe("outdated");

        using (var scope = Factory.Services.CreateScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<ITestSessionService>();
            (await sessions.EndTest(s.InstanceId, s.StudentUserId)).Success.ShouldBeTrue();
            (await sessions.RetakeActivityAsync(s.InstanceId, s.StudentUserId)).Success.ShouldBeTrue();
        }
        (await WithDbAsync(db => db.OpenTextReviews.CountAsync())).ShouldBe(0);
    }

    /// <summary>
    /// review 11: değerlendirme (PUT) ile öğrencinin metni değiştirmesi (save-answer) aynı satır kilidinde serileşir. Hangisi önce
    /// giderse gitsin sonuç tutarlı: ya PUT 409 alır (AnswerChanged / NotSubmitted) ve kayıt yoktur, ya da kayıt yazılır ve sonra
    /// gelen metin onu eskitir (Stale) — güncel görünen bir değerlendirme ASLA başka bir metne ait olmaz.
    /// </summary>
    [Fact]
    public async Task Concurrent_review_and_answer_change_never_leave_a_current_review_for_other_text()
    {
        for (var round = 0; round < 8; round++)
        {
            var s = await SeedAsync();
            await CheckAsync(s, "Su ısınınca buharlaşır.");
            var hash = OpenTextGrader.AnswerHash(Payload("Su ısınınca buharlaşır."))!;

            using var reviewScope = Factory.Services.CreateScope();
            using var saveScope = Factory.Services.CreateScope();
            var reviews = reviewScope.ServiceProvider.GetRequiredService<IOpenTextReviewService>();
            var sessions = saveScope.ServiceProvider.GetRequiredService<ITestSessionService>();
            var start = new TaskCompletionSource();

            var review = Task.Run(async () =>
            {
                await start.Task;
                return await reviews.ReviewAsync(Services.Tenancy.SchoolScope.For(s.TeacherUserId, s.SchoolId), s.RowId,
                    new Models.Dtos.OpenText.OpenTextReviewRequestDto { Evaluation = "partial", AnswerHash = hash });
            });
            var save = Task.Run(async () =>
            {
                await start.Task;
                return await sessions.SaveAnswer(
                    new SaveAnswerDto { TestInstanceId = s.InstanceId, TestQuestionId = s.RowId, AnswerPayload = Payload($"Yeni metin {round}") },
                    new UserProfileDto { Id = s.StudentUserId, KeycloakId = $"kc-s-{s.StudentUserId}", Role = "Student" });
            });
            start.SetResult();
            var (reviewResult, saveResult) = (await review, await save);

            saveResult.Success.ShouldBeTrue(saveResult.Message);
            var stored = await WithDbAsync(db => db.OpenTextReviews.AsNoTracking()
                .SingleOrDefaultAsync(r => r.WorksheetInstanceQuestionId == s.RowId));
            if (reviewResult.Success)
            {
                stored.ShouldNotBeNull();
                stored!.Stale.ShouldBeTrue();   // değerlendirme önce yazıldı, yeni metin onu eskitti
            }
            else
            {
                reviewResult.Conflict.ShouldBeTrue(reviewResult.ErrorCode);
                reviewResult.ErrorCode.ShouldBeOneOf(
                    Models.Dtos.OpenText.OpenTextReviewErrorCodes.AnswerChanged, Models.Dtos.OpenText.OpenTextReviewErrorCodes.NotSubmitted);
                stored.ShouldBeNull();
            }
        }
    }
}
