using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #526: the student's hard questions (<c>mine</c>) and a practice session on them, over real Postgres and HTTP.
/// Exam integrity: a question of an assigned test the student has not finished never shows and cannot be practised.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class HardQuestionsPostgresTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const int StudentUser = 7401;
    private const string Url = "/api/student/me/hard-questions";

    private sealed record Seeded(int TwiceWrong, int Relapsed, int OnceWrong, int Fixed, int NeverWrong, int OpenAssigned);

    private Task<HttpClient> StudentAsync() => ClientAsAsync(StudentUser, "Student", "kc-hq-student", "Student");

    private async Task<Seeded> SeedAsync() => await WithDbAsync(async db =>
    {
        var grade = new Grade { Name = "6. Sınıf" };
        var subject = new Subject { Name = "Matematik" };
        db.AddRange(grade, subject);
        await db.SaveChangesAsync();
        var topic = new Topic { Name = "Açılar", SubjectId = subject.Id, GradeId = grade.Id };
        var pool = new Worksheet { Name = "Havuz", Description = "d", GradeId = grade.Id, StudentVisibility = WorksheetStudentVisibility.Normal };
        var assigned = new Worksheet { Name = "Atanan", Description = "d", GradeId = grade.Id, StudentVisibility = WorksheetStudentVisibility.Normal };
        var student = new Student { UserId = StudentUser, StudentNumber = "S7401", SchoolName = "s", GradeId = grade.Id };
        db.AddRange(topic, pool, assigned, student);
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        // One session per attempt: a question appears once per session.
        var sessions = new List<PracticeSession>();
        var order = 0;
        async Task<int> QuestionAsync(Worksheet sheet, params (int DaysAgo, bool Correct)[] attempts)
        {
            var q = new Question { Text = $"Q{order}", ImageUrl = $"/img/test/questions/hq{order}/question.jpg", Point = 5, DifficultyLevel = 3, SubjectId = subject.Id, TopicId = topic.Id };
            db.Questions.Add(q);
            await db.SaveChangesAsync();
            var right = new Answer { QuestionId = q.Id, Text = "doğru", Order = 1 };
            var wrong = new Answer { QuestionId = q.Id, Text = "yanlış", Order = 2 };
            db.Answers.AddRange(right, wrong);
            await db.SaveChangesAsync();
            q.CorrectAnswerId = right.Id;
            db.TestQuestions.Add(new WorksheetQuestion { TestId = sheet.Id, QuestionId = q.Id, Order = ++order });
            for (var a = 0; a < attempts.Length; a++)
            {
                var (daysAgo, correct) = attempts[a];
                if (sessions.Count <= a)
                    sessions.Add(new PracticeSession { StudentId = student.Id, GradeId = grade.Id, StartTime = now.AddDays(-10) });
                sessions[a].Questions.Add(new PracticeSessionQuestion
                {
                    QuestionId = q.Id, ShownAt = now.AddDays(-daysAgo), AnsweredAt = now.AddDays(-daysAgo),
                    SelectedAnswerId = correct ? right.Id : wrong.Id, IsCorrect = correct, TimeTaken = 10,
                });
            }
            return q.Id;
        }

        var twiceWrong = await QuestionAsync(pool, (6, false), (3, false));
        var relapsed = await QuestionAsync(pool, (6, true), (5, false));
        var onceWrong = await QuestionAsync(pool, (1, false));
        var fixedLater = await QuestionAsync(pool, (4, false), (2, false), (1, true));
        var neverWrong = await QuestionAsync(pool, (2, true));
        var openAssigned = await QuestionAsync(assigned, (3, false), (2, false), (1, false));
        db.WorksheetAssignments.Add(new WorksheetAssignment { WorksheetId = assigned.Id, StudentId = student.Id, StartAt = now.AddDays(-1) });
        db.PracticeSessions.AddRange(sessions);
        await db.SaveChangesAsync();
        return new Seeded(twiceWrong, relapsed, onceWrong, fixedLater, neverWrong, openAssigned);
    });

    [Fact]
    public async Task Most_missed_first_a_relapse_before_a_single_miss_and_a_fixed_one_last()
    {
        var s = await SeedAsync();
        var client = await StudentAsync();

        var result = await client.GetFromJsonAsync<JsonElement>(Url, Json);

        result.GetProperty("mode").GetString().ShouldBe("mine");
        var items = result.GetProperty("items").EnumerateArray().ToList();
        // Fixed: 2 misses but answered correctly last → after the others. Open assigned test and never-missed: absent.
        items.Select(i => i.GetProperty("questionId").GetInt32()).ShouldBe([s.TwiceWrong, s.Relapsed, s.OnceWrong, s.Fixed]);
        items[0].GetProperty("missCount").GetInt32().ShouldBe(2);
        items[1].GetProperty("missedAfterCorrect").GetBoolean().ShouldBeTrue();
        items[3].GetProperty("lastAttemptCorrect").GetBoolean().ShouldBeTrue();
        items[0].GetProperty("topicName").GetString().ShouldBe("Açılar");
        // The panel shows the image: a short-lived signed /img URL like every question image (#365).
        items[0].GetProperty("imageUrl").GetString()!.ShouldContain("X-Amz-");
        // Never the correct choice: the assistant explains through explain-context (#510).
        foreach (var item in items)
            item.EnumerateObject().Select(p => p.Name).ShouldNotContain(n => n.StartsWith("correct"));
    }

    [Fact]
    public async Task A_session_on_hard_questions_asks_only_them_in_order_and_targets_their_count()
    {
        var s = await SeedAsync();
        var client = await StudentAsync();

        var created = await client.PostAsJsonAsync("/api/practice/sessions",
            new { questionIds = new[] { s.Relapsed, s.TwiceWrong }, startedVia = "Assistant" });

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var session = (await created.Content.ReadFromJsonAsync<PracticeSessionDto>(Json))!;
        session.TargetCount.ShouldBe(2);
        foreach (var expected in new[] { s.Relapsed, s.TwiceWrong })
        {
            var next = await client.GetFromJsonAsync<PracticeNextQuestionDto>($"/api/practice/sessions/{session.Id}/next", Json);
            next!.Question!.Id.ShouldBe(expected);
            (await client.PostAsJsonAsync($"/api/practice/sessions/{session.Id}/answer",
                new { questionId = expected, skipped = true, timeTaken = 1 })).EnsureSuccessStatusCode();
        }
        var end = await client.GetFromJsonAsync<PracticeNextQuestionDto>($"/api/practice/sessions/{session.Id}/next", Json);
        end!.Question.ShouldBeNull();
    }

    [Fact]
    public async Task Ids_outside_the_hard_list_are_refused_not_skipped()
    {
        var s = await SeedAsync();
        var client = await StudentAsync();

        foreach (var ids in new[] { new[] { s.TwiceWrong, s.OpenAssigned }, [s.NeverWrong], [999_999] })
        {
            var refused = await client.PostAsJsonAsync("/api/practice/sessions", new { questionIds = ids });
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, string.Join(",", ids));
        }
        var mixed = await client.PostAsJsonAsync("/api/practice/sessions", new { questionIds = new[] { s.TwiceWrong }, mistakes = true });
        mixed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var tooMany = await client.PostAsJsonAsync("/api/practice/sessions", new { questionIds = Enumerable.Range(1, 31).ToArray() });
        tooMany.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_query_is_validated_and_global_mode_is_not_served_yet()
    {
        var client = await StudentAsync();

        foreach (var query in new[] { "?mode=global", "?days=0", "?days=91", "?take=0", "?take=31" })
            (await client.GetAsync(Url + query)).StatusCode.ShouldBe(HttpStatusCode.BadRequest, query);
    }

    [Fact]
    public async Task Teachers_are_refused()
    {
        var teacher = await ClientAsAsync(7402, "Teacher", "kc-hq-teacher", "Teacher");

        (await teacher.GetAsync(Url)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
