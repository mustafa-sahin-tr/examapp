using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #510: explanation context and recent mistakes over real Postgres and HTTP. Exam integrity: a question in a running
/// test, or in an assigned test the student has not finished, is refused even when it was answered in practice.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class StudentQuestionExplainPostgresTests : IntegrationTestBase
{
    private const int StudentUser = 7201;
    private const int OtherUser = 7202;

    public StudentQuestionExplainPostgresTests(IntegrationApiFactory factory) : base(factory)
    {
    }

    private Task<HttpClient> StudentAsync() => ClientAsAsync(StudentUser, "Student", "kc-ex-student", "Student");
    private Task<HttpClient> OtherAsync() => ClientAsAsync(OtherUser, "Student", "kc-ex-other", "Student");

    private sealed record Seeded(int PracticeWrong, int FinishedTest, int RunningTest, int AssignedTest, int Unanswered, int RunningAndPractised);

    private async Task<Seeded> SeedAsync()
    {
        Seeded seeded = null!;
        await WithDbAsync(async db =>
        {
            var grade = new Grade { Name = "6. Sınıf" };
            var subject = new Subject { Name = "Matematik" };
            db.AddRange(grade, subject);
            await db.SaveChangesAsync();
            var topic = new Topic { Name = "Kesirler", SubjectId = subject.Id, GradeId = grade.Id };
            db.Add(topic);
            var student = new Student { UserId = StudentUser, StudentNumber = "S7201", SchoolName = "s", GradeId = grade.Id };
            var other = new Student { UserId = OtherUser, StudentNumber = "S7202", SchoolName = "s", GradeId = grade.Id };
            db.AddRange(student, other);
            await db.SaveChangesAsync();

            async Task<(Question Q, Answer Right, Answer Wrong)> QuestionAsync(string text)
            {
                var q = new Question { Text = text, Point = 5, DifficultyLevel = 3, SubjectId = subject.Id, TopicId = topic.Id };
                db.Questions.Add(q);
                await db.SaveChangesAsync();
                var right = new Answer { QuestionId = q.Id, Text = "doğru", Order = 1 };
                var wrong = new Answer { QuestionId = q.Id, Text = "yanlış", Order = 2 };
                db.Answers.AddRange(right, wrong);
                await db.SaveChangesAsync();
                q.CorrectAnswerId = right.Id;
                return (q, right, wrong);
            }

            var practiceWrong = await QuestionAsync("pratik");
            var finishedTest = await QuestionAsync("bitmiş test");
            var runningTest = await QuestionAsync("süren test");
            var assignedTest = await QuestionAsync("atanmış test");
            var unanswered = await QuestionAsync("cevapsız");
            var runningAndPractised = await QuestionAsync("pratikte çözülmüş, testte sürüyor");

            var now = DateTime.UtcNow;
            var finishedSheet = new Worksheet { Name = "Biten", Description = "d", GradeId = grade.Id };
            var runningSheet = new Worksheet { Name = "Süren", Description = "d", GradeId = grade.Id };
            var assignedSheet = new Worksheet { Name = "Atanan", Description = "d", GradeId = grade.Id };
            db.Worksheets.AddRange(finishedSheet, runningSheet, assignedSheet);
            await db.SaveChangesAsync();
            var finishedItem = new WorksheetQuestion { TestId = finishedSheet.Id, QuestionId = finishedTest.Q.Id, Order = 1 };
            var runningItem = new WorksheetQuestion { TestId = runningSheet.Id, QuestionId = runningTest.Q.Id, Order = 1 };
            var runningItem2 = new WorksheetQuestion { TestId = runningSheet.Id, QuestionId = runningAndPractised.Q.Id, Order = 2 };
            db.AddRange(finishedItem, runningItem, runningItem2,
                new WorksheetQuestion { TestId = assignedSheet.Id, QuestionId = assignedTest.Q.Id, Order = 1 });
            db.WorksheetAssignments.Add(new WorksheetAssignment { WorksheetId = assignedSheet.Id, StudentId = student.Id, StartAt = now.AddDays(-1) });
            var finished = new WorksheetInstance { StudentId = student.Id, WorksheetId = finishedSheet.Id, StartTime = now.AddDays(-2), EndTime = now.AddDays(-2).AddMinutes(5), Status = WorksheetInstanceStatus.Completed };
            var running = new WorksheetInstance { StudentId = student.Id, WorksheetId = runningSheet.Id, StartTime = now.AddMinutes(-1), Status = WorksheetInstanceStatus.Started };
            db.TestInstances.AddRange(finished, running);
            await db.SaveChangesAsync();
            db.TestInstanceQuestions.AddRange(
                new WorksheetInstanceQuestion { WorksheetInstanceId = finished.Id, WorksheetQuestionId = finishedItem.Id, SelectedAnswerId = finishedTest.Wrong.Id, TimeTaken = 30 },
                new WorksheetInstanceQuestion { WorksheetInstanceId = running.Id, WorksheetQuestionId = runningItem.Id, SelectedAnswerId = runningTest.Wrong.Id, TimeTaken = 5 });

            var session = new PracticeSession { StudentId = student.Id, GradeId = grade.Id, StartTime = now.AddDays(-1) };
            session.Questions.Add(new PracticeSessionQuestion { QuestionId = practiceWrong.Q.Id, ShownAt = now.AddDays(-1), AnsweredAt = now.AddDays(-1), SelectedAnswerId = practiceWrong.Wrong.Id, IsCorrect = false, TimeTaken = 10 });
            session.Questions.Add(new PracticeSessionQuestion { QuestionId = assignedTest.Q.Id, ShownAt = now.AddDays(-1), AnsweredAt = now.AddDays(-1), SelectedAnswerId = assignedTest.Right.Id, IsCorrect = true, TimeTaken = 10 });
            session.Questions.Add(new PracticeSessionQuestion { QuestionId = runningAndPractised.Q.Id, ShownAt = now.AddDays(-1), AnsweredAt = now.AddDays(-1), SelectedAnswerId = runningAndPractised.Right.Id, IsCorrect = true, TimeTaken = 10 });
            // Shown but not answered: no explanation.
            session.Questions.Add(new PracticeSessionQuestion { QuestionId = unanswered.Q.Id, ShownAt = now.AddDays(-1) });
            db.PracticeSessions.Add(session);
            await db.SaveChangesAsync();

            seeded = new Seeded(practiceWrong.Q.Id, finishedTest.Q.Id, runningTest.Q.Id, assignedTest.Q.Id, unanswered.Q.Id, runningAndPractised.Q.Id);
        });
        return seeded;
    }

    private static string Explain(int questionId) => $"/api/student/me/questions/{questionId}/explain-context";

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("errorCode", out var code) ? code.GetString() : null;
    }

    [Fact]
    public async Task Answered_practice_question_returns_both_choices_and_topic()
    {
        var s = await SeedAsync();
        var client = await StudentAsync();

        var response = await client.GetAsync(Explain(s.PracticeWrong));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("source").GetString().ShouldBe("practice");
        root.GetProperty("outcome").GetString().ShouldBe("Wrong");
        root.GetProperty("studentChoice").GetString().ShouldBe("B");
        root.GetProperty("correctChoice").GetString().ShouldBe("A");
        root.GetProperty("choiceCount").GetInt32().ShouldBe(2);
        root.GetProperty("topicName").GetString().ShouldBe("Kesirler");
        root.GetProperty("text").GetString()!.ShouldContain("pratik");
    }

    [Fact]
    public async Task Finished_test_question_is_explained()
    {
        var s = await SeedAsync();
        var client = await StudentAsync();

        var context = await client.GetFromJsonAsync<JsonElement>(Explain(s.FinishedTest), Json);

        context.GetProperty("source").GetString().ShouldBe("test");
        context.GetProperty("studentChoice").GetString().ShouldBe("B");
        context.GetProperty("correctChoice").GetString().ShouldBe("A");
    }

    [Fact]
    public async Task Running_test_question_is_refused_even_if_answered_in_practice()
    {
        var s = await SeedAsync();
        var client = await StudentAsync();

        foreach (var id in new[] { s.RunningTest, s.RunningAndPractised })
        {
            var response = await client.GetAsync(Explain(id));
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await ErrorCodeAsync(response)).ShouldBe("QuestionInOpenTest");
        }
    }

    [Fact]
    public async Task Assigned_unfinished_test_question_is_refused_even_if_answered_in_practice()
    {
        var s = await SeedAsync();
        var client = await StudentAsync();

        var response = await client.GetAsync(Explain(s.AssignedTest));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(response)).ShouldBe("QuestionInOpenTest");
    }

    [Fact]
    public async Task Unanswered_or_someone_elses_question_is_not_found()
    {
        var s = await SeedAsync();
        var client = await StudentAsync();
        var other = await OtherAsync();

        var unanswered = await client.GetAsync(Explain(s.Unanswered));
        unanswered.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ErrorCodeAsync(unanswered)).ShouldBe("QuestionNotAnswered");

        (await other.GetAsync(Explain(s.PracticeWrong))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync(Explain(999_999))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Mistakes_list_practice_and_finished_tests_but_not_the_running_test()
    {
        var s = await SeedAsync();
        var client = await StudentAsync();

        var result = await client.GetFromJsonAsync<JsonElement>("/api/student/me/mistakes?days=30&take=20", Json);

        var ids = result.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("questionId").GetInt32()).ToList();
        ids.ShouldBe([s.PracticeWrong, s.FinishedTest]); // newest first
        result.GetProperty("items")[0].GetProperty("topicName").GetString().ShouldBe("Kesirler");
    }

    [Fact]
    public async Task Mistakes_validate_the_query()
    {
        var client = await StudentAsync();

        (await client.GetAsync("/api/student/me/mistakes?days=0")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.GetAsync("/api/student/me/mistakes?take=21")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Non_students_are_refused()
    {
        var teacher = await ClientAsAsync(7203, "Teacher", "kc-ex-teacher", "Teacher");

        (await teacher.GetAsync("/api/student/me/mistakes")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await teacher.GetAsync(Explain(1))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
