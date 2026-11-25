using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #527: slow and rushed questions over real Postgres and HTTP. Ten other students answer each question in 60 s (the
/// average); thresholds are the defaults (slow ≥ 2×, rushed ≤ 25 % and ≤ 15 s, 10 answers for a question's own average).
/// </summary>
[Collection(IntegrationCollection.Name)]
public class TimeStrugglesPostgresTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const int StudentUser = 7601;
    private const string Url = "/api/student/me/time-struggles";

    private sealed record Seeded(int SlowRight, int Normal, int RushedWrong, int FastRight, int ZeroTime, int TooLong, int FewAnswers, int OpenAssigned);

    private Task<HttpClient> StudentAsync() => ClientAsAsync(StudentUser, "Student", "kc-ts-student", "Student");

    private async Task<Seeded> SeedAsync() => await WithDbAsync(async db =>
    {
        var grade = new Grade { Name = "6. Sınıf" };
        var subject = new Subject { Name = "Matematik" };
        db.AddRange(grade, subject);
        await db.SaveChangesAsync();
        var topic = new Topic { Name = "Kesirler", SubjectId = subject.Id, GradeId = grade.Id };
        db.Add(topic);
        await db.SaveChangesAsync();
        var subTopic = new SubTopic { Name = "Denk kesirler", TopicId = topic.Id };
        var pool = new Worksheet { Name = "Havuz", Description = "d", GradeId = grade.Id, StudentVisibility = WorksheetStudentVisibility.Normal };
        var assigned = new Worksheet { Name = "Atanan", Description = "d", GradeId = grade.Id, StudentVisibility = WorksheetStudentVisibility.Normal };
        var me = new Student { UserId = StudentUser, StudentNumber = "S7601", SchoolName = "s", GradeId = grade.Id };
        var others = Enumerable.Range(0, 10)
            .Select(i => new Student { UserId = 7610 + i, StudentNumber = $"S{7610 + i}", SchoolName = "s", GradeId = grade.Id }).ToList();
        db.AddRange(subTopic, pool, assigned, me);
        db.AddRange(others);
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        var otherSessions = others.Select(st => new PracticeSession { StudentId = st.Id, GradeId = grade.Id, StartTime = now.AddDays(-5) }).ToList();
        var mine = new PracticeSession { StudentId = me.Id, GradeId = grade.Id, StartTime = now.AddDays(-2) };
        var order = 0;
        async Task<int> QuestionAsync(Worksheet sheet, bool correct, int seconds, int otherAnswers = 10)
        {
            var q = new Question { Text = $"S{order}", ImageUrl = $"/img/test/questions/ts{order}/question.jpg", Point = 5, DifficultyLevel = 3, SubjectId = subject.Id, TopicId = topic.Id };
            db.Questions.Add(q);
            await db.SaveChangesAsync();
            var right = new Answer { QuestionId = q.Id, Text = "doğru", Order = 1 };
            var wrong = new Answer { QuestionId = q.Id, Text = "yanlış", Order = 2 };
            db.Answers.AddRange(right, wrong);
            await db.SaveChangesAsync();
            q.CorrectAnswerId = right.Id;
            db.TestQuestions.Add(new WorksheetQuestion { TestId = sheet.Id, QuestionId = q.Id, Order = ++order });
            db.QuestionSubTopics.Add(new QuestionSubTopic { QuestionId = q.Id, SubTopicId = subTopic.Id });
            for (var i = 0; i < otherAnswers; i++)
                otherSessions[i].Questions.Add(new PracticeSessionQuestion
                {
                    QuestionId = q.Id, ShownAt = now.AddDays(-4), AnsweredAt = now.AddDays(-4),
                    SelectedAnswerId = right.Id, IsCorrect = true, TimeTaken = 60,
                });
            mine.Questions.Add(new PracticeSessionQuestion
            {
                QuestionId = q.Id, ShownAt = now.AddDays(-1), AnsweredAt = now.AddDays(-1),
                SelectedAnswerId = correct ? right.Id : wrong.Id, IsCorrect = correct, TimeTaken = seconds,
            });
            return q.Id;
        }

        var slowRight = await QuestionAsync(pool, true, 180);
        var normal = await QuestionAsync(pool, true, 60);
        var rushedWrong = await QuestionAsync(pool, false, 5);
        var fastRight = await QuestionAsync(pool, true, 5);
        var zeroTime = await QuestionAsync(pool, false, 0);
        var tooLong = await QuestionAsync(pool, false, 4000);
        var fewAnswers = await QuestionAsync(pool, false, 200, otherAnswers: 3);
        var openAssigned = await QuestionAsync(assigned, true, 300);
        db.WorksheetAssignments.Add(new WorksheetAssignment { WorksheetId = assigned.Id, StudentId = me.Id, StartAt = now.AddDays(-1) });
        db.PracticeSessions.AddRange(otherSessions);
        db.PracticeSessions.Add(mine);
        await db.SaveChangesAsync();
        return new Seeded(slowRight, normal, rushedWrong, fastRight, zeroTime, tooLong, fewAnswers, openAssigned);
    });

    private static List<JsonElement> Items(JsonElement result) => result.GetProperty("items").EnumerateArray().ToList();

    [Fact]
    public async Task Slow_returns_answers_at_twice_the_average_right_or_wrong_and_uses_the_subtopic_average_with_few_answers()
    {
        var s = await SeedAsync();
        var client = await StudentAsync();

        var raw = await client.GetStringAsync(Url + "?kind=slow");
        var items = Items(JsonDocument.Parse(raw).RootElement);

        // FewAnswers: 200 s against the subtopic average; SlowRight: 3× its own 60 s. Normal, a 0 s / 4000 s time and an open
        // assigned test's question are out.
        items.Select(i => i.GetProperty("questionId").GetInt32()).ShouldBe([s.FewAnswers, s.SlowRight]);
        var slow = items[1];
        (slow.GetProperty("timeTakenSeconds").GetInt32(), slow.GetProperty("averageSeconds").GetInt32(), slow.GetProperty("averageScope").GetString(), slow.GetProperty("correct").GetBoolean())
            .ShouldBe((180, 60, "question", true));
        items[0].GetProperty("averageScope").GetString().ShouldBe("subTopic");
        raw.ShouldNotContain("correctAnswer", Case.Insensitive);
    }

    [Fact]
    public async Task Rushed_returns_only_fast_wrong_answers_and_a_session_opens_on_them()
    {
        var s = await SeedAsync();
        var client = await StudentAsync();

        var items = Items(await client.GetFromJsonAsync<JsonElement>(Url + "?kind=rushed", Json));

        items.Select(i => i.GetProperty("questionId").GetInt32()).ShouldBe([s.RushedWrong]);
        items[0].GetProperty("correct").GetBoolean().ShouldBeFalse();

        var created = await client.PostAsJsonAsync("/api/practice/sessions", new { questionIds = new[] { s.RushedWrong, s.SlowRight } });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        foreach (var refused in new[] { s.Normal, s.FastRight, s.OpenAssigned })
            (await client.PostAsJsonAsync("/api/practice/sessions", new { questionIds = new[] { refused } }))
                .StatusCode.ShouldBe(HttpStatusCode.BadRequest, refused.ToString());
    }

    [Fact]
    public async Task The_query_is_validated_and_teachers_are_refused()
    {
        var client = await StudentAsync();
        foreach (var query in new[] { "?kind=other", "?days=0", "?days=91", "?take=0", "?take=31" })
            (await client.GetAsync(Url + query)).StatusCode.ShouldBe(HttpStatusCode.BadRequest, query);

        var teacher = await ClientAsAsync(7602, "Teacher", "kc-ts-teacher", "Teacher");
        (await teacher.GetAsync(Url)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
