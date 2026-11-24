using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Practice;
using Microsoft.Extensions.DependencyInjection;

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
            item.EnumerateObject().Select(p => p.Name).ShouldNotContain(n => n.StartsWith("correct") && n != "correctPercent");
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

    private sealed record GlobalSeeded(int Hard, int Medium, int TooFew, int Restricted, int OtherGrade, int OpenAssigned, int SolvedByMe);

    /// <summary>
    /// 20 other students answer in practice (one session each); <c>correct</c> of them get the question right. The student 7401
    /// answers <c>SolvedByMe</c> correctly.
    /// </summary>
    private async Task<GlobalSeeded> SeedGlobalAsync() => await WithDbAsync(async db =>
    {
        var grade = new Grade { Name = "6. Sınıf" };
        var otherGrade = new Grade { Name = "7. Sınıf" };
        var subject = new Subject { Name = "Matematik" };
        db.AddRange(grade, otherGrade, subject);
        await db.SaveChangesAsync();
        Worksheet Sheet(string name, int gradeId, WorksheetStudentVisibility visibility) =>
            new() { Name = name, Description = "d", GradeId = gradeId, StudentVisibility = visibility };
        var pool = Sheet("Havuz", grade.Id, WorksheetStudentVisibility.Normal);
        var restricted = Sheet("Gizli", grade.Id, WorksheetStudentVisibility.Restricted);
        var other = Sheet("Diğer sınıf", otherGrade.Id, WorksheetStudentVisibility.Normal);
        var assigned = Sheet("Atanan", grade.Id, WorksheetStudentVisibility.Normal);
        var me = new Student { UserId = StudentUser, StudentNumber = "S7401", SchoolName = "s", GradeId = grade.Id };
        var others = Enumerable.Range(0, 20)
            .Select(i => new Student { UserId = 7410 + i, StudentNumber = $"S{7410 + i}", SchoolName = "s", GradeId = grade.Id }).ToList();
        db.AddRange(pool, restricted, other, assigned, me);
        db.AddRange(others);
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        var sessions = others.Select(st => new PracticeSession { StudentId = st.Id, GradeId = grade.Id, StartTime = now.AddDays(-3) }).ToList();
        var mine = new PracticeSession { StudentId = me.Id, GradeId = grade.Id, StartTime = now.AddDays(-2) };
        var order = 0;
        async Task<int> QuestionAsync(Worksheet sheet, int students, int correct, bool solvedByMe = false)
        {
            var q = new Question { Text = $"G{order}", Point = 5, DifficultyLevel = 3, SubjectId = subject.Id };
            db.Questions.Add(q);
            await db.SaveChangesAsync();
            var right = new Answer { QuestionId = q.Id, Text = "doğru", Order = 1 };
            var wrong = new Answer { QuestionId = q.Id, Text = "yanlış", Order = 2 };
            db.Answers.AddRange(right, wrong);
            await db.SaveChangesAsync();
            q.CorrectAnswerId = right.Id;
            db.TestQuestions.Add(new WorksheetQuestion { TestId = sheet.Id, QuestionId = q.Id, Order = ++order });
            PracticeSessionQuestion Row(bool ok) => new()
            {
                QuestionId = q.Id, ShownAt = now.AddDays(-2), AnsweredAt = now.AddDays(-2),
                SelectedAnswerId = ok ? right.Id : wrong.Id, IsCorrect = ok, TimeTaken = 10,
            };
            for (var i = 0; i < students; i++)
                sessions[i].Questions.Add(Row(i < correct));
            if (solvedByMe)
                mine.Questions.Add(Row(true));
            return q.Id;
        }

        var hard = await QuestionAsync(pool, 20, 5);
        var medium = await QuestionAsync(pool, 20, 15);
        var tooFew = await QuestionAsync(pool, 19, 0);
        var hidden = await QuestionAsync(restricted, 20, 0);
        var otherGradeQ = await QuestionAsync(other, 20, 0);
        var openAssigned = await QuestionAsync(assigned, 20, 0);
        var solved = await QuestionAsync(pool, 20, 0, solvedByMe: true);
        db.WorksheetAssignments.Add(new WorksheetAssignment { WorksheetId = assigned.Id, StudentId = me.Id, StartAt = now.AddDays(-1) });
        db.PracticeSessions.AddRange(sessions);
        db.PracticeSessions.Add(mine);
        await db.SaveChangesAsync();
        return new GlobalSeeded(hard, medium, tooFew, hidden, otherGradeQ, openAssigned, solved);
    });

    private async Task<int> RunStatsJobAsync()
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IQuestionAnswerStatsJob>().RunAsync();
    }

    [Fact]
    public async Task Global_mode_lowest_correct_rate_first_only_with_20_students_in_the_students_pool_and_not_solved_by_them()
    {
        var s = await SeedGlobalAsync();
        (await RunStatsJobAsync()).ShouldBe(7);
        var client = await StudentAsync();

        var result = await client.GetFromJsonAsync<JsonElement>(Url + "?mode=global", Json);

        result.GetProperty("mode").GetString().ShouldBe("global");
        var items = result.GetProperty("items").EnumerateArray().ToList();
        // 19 students: too few. Restricted visibility, another grade, an open assigned test, one they solved: out.
        items.Select(i => i.GetProperty("questionId").GetInt32()).ShouldBe([s.Hard, s.Medium]);
        items[0].GetProperty("correctPercent").GetInt32().ShouldBe(25);
        items[1].GetProperty("correctPercent").GetInt32().ShouldBe(75);
        items[0].GetProperty("attempts").GetInt32().ShouldBe(0);
        // Only the aggregate percent: no count of students, nothing about another student.
        foreach (var item in items)
            item.EnumerateObject().Select(p => p.Name).ShouldNotContain(n => n.StartsWith("correct") && n != "correctPercent" || n.Contains("student", StringComparison.OrdinalIgnoreCase));

        var created = await client.PostAsJsonAsync("/api/practice/sessions", new { questionIds = new[] { s.Medium, s.Hard } });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        foreach (var refused in new[] { s.TooFew, s.Restricted, s.OtherGrade, s.OpenAssigned, s.SolvedByMe })
            (await client.PostAsJsonAsync("/api/practice/sessions", new { questionIds = new[] { refused } }))
                .StatusCode.ShouldBe(HttpStatusCode.BadRequest, refused.ToString());
    }

    [Fact]
    public async Task The_stats_job_counts_a_student_once_with_their_first_answer_and_replaces_old_rows()
    {
        var s = await SeedGlobalAsync();
        await WithDbAsync(async db =>
        {
            // Student 0 got Hard right first; solving it wrong later does not change their count.
            var session = new PracticeSession { StudentId = db.Students.Single(st => st.UserId == 7410).Id, GradeId = db.Grades.First().Id, StartTime = DateTime.UtcNow };
            var wrong = db.Answers.Single(a => a.QuestionId == s.Hard && a.Order == 2);
            session.Questions.Add(new PracticeSessionQuestion { QuestionId = s.Hard, ShownAt = DateTime.UtcNow, AnsweredAt = DateTime.UtcNow, SelectedAnswerId = wrong.Id, IsCorrect = false, TimeTaken = 5 });
            db.PracticeSessions.Add(session);
            await db.SaveChangesAsync();
        });

        await RunStatsJobAsync();
        await RunStatsJobAsync();

        var stat = await WithDbAsync(db => Task.FromResult(db.QuestionAnswerStats.Single(x => x.QuestionId == s.Hard)));
        stat.StudentCount.ShouldBe(20);
        stat.CorrectCount.ShouldBe(5);
    }

    [Fact]
    public async Task The_query_is_validated()
    {
        var client = await StudentAsync();

        foreach (var query in new[] { "?mode=other", "?days=0", "?days=91", "?take=0", "?take=31" })
            (await client.GetAsync(Url + query)).StatusCode.ShouldBe(HttpStatusCode.BadRequest, query);
    }

    [Fact]
    public async Task Teachers_are_refused()
    {
        var teacher = await ClientAsAsync(7402, "Teacher", "kc-hq-teacher", "Teacher");

        (await teacher.GetAsync(Url)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
