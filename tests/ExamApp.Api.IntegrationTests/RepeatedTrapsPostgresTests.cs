using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Services.Practice;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #528: repeated traps over real Postgres and HTTP. The popular distractor comes from the nightly choice stats (20 other
/// students); a pattern needs 3 wrong questions in a subtopic with the popular distractor picked in most of them. A question of an
/// assigned test the student has not finished never counts. No correct choice and no other student's choice leaves.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class RepeatedTrapsPostgresTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const int StudentUser = 7501;
    private const string Url = "/api/student/me/repeated-traps";

    private sealed record Seeded(int AcuteSubTopic, int[] Traps, int Scattered);

    private Task<HttpClient> StudentAsync() => ClientAsAsync(StudentUser, "Student", "kc-rt-student", "Student");

    /// <summary>
    /// Every question: A correct, B the popular distractor (10 of 20 others), C 5, A 5. Subtopic "Dar açı": the student picked
    /// B, B, C (pattern) and B in an open assigned test (out). Subtopic "Geniş açı": C, D, C (scattered, no pattern).
    /// </summary>
    private async Task<Seeded> SeedAsync() => await WithDbAsync(async db =>
    {
        var grade = new Grade { Name = "6. Sınıf" };
        var subject = new Subject { Name = "Matematik" };
        db.AddRange(grade, subject);
        await db.SaveChangesAsync();
        var topic = new Topic { Name = "Açılar", SubjectId = subject.Id, GradeId = grade.Id };
        db.Add(topic);
        await db.SaveChangesAsync();
        var acute = new SubTopic { Name = "Dar açı", TopicId = topic.Id };
        var obtuse = new SubTopic { Name = "Geniş açı", TopicId = topic.Id };
        var pool = new Worksheet { Name = "Havuz", Description = "d", GradeId = grade.Id, StudentVisibility = WorksheetStudentVisibility.Normal };
        var assigned = new Worksheet { Name = "Atanan", Description = "d", GradeId = grade.Id, StudentVisibility = WorksheetStudentVisibility.Normal };
        var me = new Student { UserId = StudentUser, StudentNumber = "S7501", SchoolName = "s", GradeId = grade.Id };
        var others = Enumerable.Range(0, 20)
            .Select(i => new Student { UserId = 7510 + i, StudentNumber = $"S{7510 + i}", SchoolName = "s", GradeId = grade.Id }).ToList();
        db.AddRange(acute, obtuse, pool, assigned, me);
        db.AddRange(others);
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        var otherSessions = others.Select(st => new PracticeSession { StudentId = st.Id, GradeId = grade.Id, StartTime = now.AddDays(-5) }).ToList();
        var mine = new PracticeSession { StudentId = me.Id, GradeId = grade.Id, StartTime = now.AddDays(-2) };
        var order = 0;
        async Task<int> QuestionAsync(Worksheet sheet, SubTopic subTopic, char myChoice)
        {
            var q = new Question { Text = $"T{order}", ImageUrl = $"/img/test/questions/rt{order}/question.jpg", Point = 5, DifficultyLevel = 3, SubjectId = subject.Id, TopicId = topic.Id };
            db.Questions.Add(q);
            await db.SaveChangesAsync();
            var choices = "ABCD".Select((c, i) => new Answer { QuestionId = q.Id, Text = $"şık {c}", Order = i + 1 }).ToList();
            db.Answers.AddRange(choices);
            await db.SaveChangesAsync();
            q.CorrectAnswerId = choices[0].Id;
            db.TestQuestions.Add(new WorksheetQuestion { TestId = sheet.Id, QuestionId = q.Id, Order = ++order });
            db.QuestionSubTopics.Add(new QuestionSubTopic { QuestionId = q.Id, SubTopicId = subTopic.Id });
            for (var i = 0; i < 20; i++)
            {
                var pick = i < 5 ? choices[0] : i < 15 ? choices[1] : choices[2];
                otherSessions[i].Questions.Add(new PracticeSessionQuestion
                {
                    QuestionId = q.Id, ShownAt = now.AddDays(-4), AnsweredAt = now.AddDays(-4),
                    SelectedAnswerId = pick.Id, IsCorrect = pick == choices[0], TimeTaken = 10,
                });
            }
            mine.Questions.Add(new PracticeSessionQuestion
            {
                QuestionId = q.Id, ShownAt = now.AddDays(-1), AnsweredAt = now.AddDays(-1),
                SelectedAnswerId = choices[myChoice - 'A'].Id, IsCorrect = false, TimeTaken = 10,
            });
            return q.Id;
        }

        var trap1 = await QuestionAsync(pool, acute, 'B');
        var trap2 = await QuestionAsync(pool, acute, 'B');
        await QuestionAsync(pool, acute, 'C');
        await QuestionAsync(assigned, acute, 'B');
        var scattered = await QuestionAsync(pool, obtuse, 'C');
        await QuestionAsync(pool, obtuse, 'D');
        await QuestionAsync(pool, obtuse, 'C');
        db.WorksheetAssignments.Add(new WorksheetAssignment { WorksheetId = assigned.Id, StudentId = me.Id, StartAt = now.AddDays(-1) });
        db.PracticeSessions.AddRange(otherSessions);
        db.PracticeSessions.Add(mine);
        await db.SaveChangesAsync();
        return new Seeded(acute.Id, [trap1, trap2], scattered);
    });

    private async Task RunStatsJobAsync()
    {
        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IQuestionAnswerStatsJob>().RunAsync();
    }

    [Fact]
    public async Task A_subtopic_where_most_mistakes_are_the_popular_distractor_is_a_pattern_and_scattered_mistakes_are_not()
    {
        var s = await SeedAsync();
        await RunStatsJobAsync();
        var client = await StudentAsync();

        var raw = await client.GetStringAsync(Url);
        var result = JsonDocument.Parse(raw).RootElement;

        var item = result.GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
        item.GetProperty("subTopicId").GetInt32().ShouldBe(s.AcuteSubTopic);
        item.GetProperty("subTopicName").GetString().ShouldBe("Dar açı");
        (item.GetProperty("mistakeCount").GetInt32(), item.GetProperty("trapCount").GetInt32(), item.GetProperty("strengthPct").GetInt32())
            .ShouldBe((3, 2, 67));
        var examples = item.GetProperty("examples").EnumerateArray().ToList();
        examples.Select(e => e.GetProperty("questionId").GetInt32()).OrderBy(id => id).ShouldBe(s.Traps);
        examples.ShouldAllBe(e => e.GetProperty("chosenLabel").GetString() == "B" && e.GetProperty("chosenText").GetString() == "şık B");
        // The student's own choice only: never the correct one, never another student's count.
        raw.ShouldNotContain("correct", Case.Insensitive);
        raw.ShouldNotContain("studentCount", Case.Insensitive);
    }

    [Fact]
    public async Task The_stats_job_counts_each_students_first_choice()
    {
        var s = await SeedAsync();
        await RunStatsJobAsync();

        var stats = await WithDbAsync(db => Task.FromResult(db.QuestionChoiceStats.Where(c => c.QuestionId == s.Scattered).ToList()));

        stats.Sum(c => c.StudentCount).ShouldBe(21);
        // B: 10 others; C: 5 others and the student; A: 5.
        stats.Select(c => c.StudentCount).OrderBy(c => c).ShouldBe([5, 6, 10]);
    }

    [Fact]
    public async Task Nothing_before_the_stats_job_has_run()
    {
        await SeedAsync();
        var client = await StudentAsync();

        var result = await client.GetFromJsonAsync<JsonElement>(Url, Json);

        result.GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task The_query_is_validated_and_teachers_are_refused()
    {
        var client = await StudentAsync();
        foreach (var query in new[] { "?days=0", "?days=91", "?take=0", "?take=11" })
            (await client.GetAsync(Url + query)).StatusCode.ShouldBe(HttpStatusCode.BadRequest, query);

        var teacher = await ClientAsAsync(7502, "Teacher", "kc-rt-teacher", "Teacher");
        (await teacher.GetAsync(Url)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
