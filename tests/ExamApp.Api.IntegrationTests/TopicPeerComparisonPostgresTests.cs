using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Services.Practice;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #533: the student's topic accuracy next to their grade's, from the nightly stats. Defaults: 5 answers in a topic make a
/// student count, 20 students make a peer value (else null). Only aggregates: no student id, rank or percentile.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class TopicPeerComparisonPostgresTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const int StudentUser = 7701;
    private const string Url = "/api/student/me/topic-comparison";

    private sealed record Seeded(int Angles, int Fractions);

    private Task<HttpClient> StudentAsync() => ClientAsAsync(StudentUser, "Student", "kc-pc-student", "Student");

    /// <summary>
    /// Angles: 20 classmates (10 at 100 %, 10 at 60 %) and one in another grade at 0 %; the student 40 %. Fractions: 19 classmates
    /// (too few) and one with only 4 answers; the student 80 %.
    /// </summary>
    private async Task<Seeded> SeedAsync() => await WithDbAsync(async db =>
    {
        var grade = new Grade { Name = "6. Sınıf" };
        var otherGrade = new Grade { Name = "7. Sınıf" };
        var subject = new Subject { Name = "Matematik" };
        db.AddRange(grade, otherGrade, subject);
        await db.SaveChangesAsync();
        var angles = new Topic { Name = "Açılar", SubjectId = subject.Id, GradeId = grade.Id };
        var fractions = new Topic { Name = "Kesirler", SubjectId = subject.Id, GradeId = grade.Id };
        var pool = new Worksheet { Name = "Havuz", Description = "d", GradeId = grade.Id, StudentVisibility = WorksheetStudentVisibility.Normal };
        var me = new Student { UserId = StudentUser, StudentNumber = "S7701", SchoolName = "s", GradeId = grade.Id };
        var classmates = Enumerable.Range(0, 21)
            .Select(i => new Student { UserId = 7710 + i, StudentNumber = $"S{7710 + i}", SchoolName = "s", GradeId = grade.Id }).ToList();
        var older = new Student { UserId = 7740, StudentNumber = "S7740", SchoolName = "s", GradeId = otherGrade.Id };
        db.AddRange(angles, fractions, pool, me, older);
        db.AddRange(classmates);
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        var order = 0;
        async Task<List<(Question Q, Answer Right, Answer Wrong)>> QuestionsAsync(Topic topic)
        {
            var list = new List<(Question, Answer, Answer)>();
            for (var i = 0; i < 5; i++)
            {
                var q = new Question { Text = $"P{order}", Point = 5, DifficultyLevel = 3, SubjectId = subject.Id, TopicId = topic.Id };
                db.Questions.Add(q);
                await db.SaveChangesAsync();
                var right = new Answer { QuestionId = q.Id, Text = "doğru", Order = 1 };
                var wrong = new Answer { QuestionId = q.Id, Text = "yanlış", Order = 2 };
                db.Answers.AddRange(right, wrong);
                await db.SaveChangesAsync();
                q.CorrectAnswerId = right.Id;
                db.TestQuestions.Add(new WorksheetQuestion { TestId = pool.Id, QuestionId = q.Id, Order = ++order });
                list.Add((q, right, wrong));
            }
            return list;
        }
        void Answer(Student student, List<(Question Q, Answer Right, Answer Wrong)> questions, int correct, int count = 5)
        {
            var session = new PracticeSession { StudentId = student.Id, GradeId = student.GradeId!.Value, StartTime = now.AddDays(-3) };
            for (var i = 0; i < count; i++)
            {
                var (q, right, wrong) = questions[i];
                session.Questions.Add(new PracticeSessionQuestion
                {
                    QuestionId = q.Id, ShownAt = now.AddDays(-3), AnsweredAt = now.AddDays(-3),
                    SelectedAnswerId = i < correct ? right.Id : wrong.Id, IsCorrect = i < correct, TimeTaken = 30,
                });
            }
            db.PracticeSessions.Add(session);
        }

        var angleQs = await QuestionsAsync(angles);
        var fractionQs = await QuestionsAsync(fractions);
        for (var i = 0; i < 20; i++)
            Answer(classmates[i], angleQs, i < 10 ? 5 : 3);
        Answer(older, angleQs, 0);
        Answer(me, angleQs, 2);
        for (var i = 0; i < 19; i++)
            Answer(classmates[i], fractionQs, 4);
        Answer(classmates[20], fractionQs, 4, count: 4);
        Answer(me, fractionQs, 4);
        await db.SaveChangesAsync();
        return new Seeded(angles.Id, fractions.Id);
    });

    private async Task RunStatsJobAsync()
    {
        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IQuestionAnswerStatsJob>().RunAsync();
    }

    [Fact]
    public async Task Peer_values_come_from_the_nightly_stats_only_with_20_students_and_hold_no_student_data()
    {
        var s = await SeedAsync();
        var client = await StudentAsync();

        // Before the job: the student's own values, no peer value (never computed at request time).
        var before = (await client.GetFromJsonAsync<JsonElement>(Url, Json)).GetProperty("items").EnumerateArray().ToList();
        before.Count.ShouldBe(2);
        before.ShouldAllBe(i => i.GetProperty("peerAccuracyPct").ValueKind == JsonValueKind.Null);

        await RunStatsJobAsync();
        var raw = await client.GetStringAsync(Url);
        var items = JsonDocument.Parse(raw).RootElement.GetProperty("items").EnumerateArray().ToList();

        // Angles first: furthest behind the grade. 21 students (20 classmates + the student) at 100 ×10, 60 ×10, 40: mean 78, median 60;
        // the other grade's student is not a peer.
        items.Select(i => i.GetProperty("topicId").GetInt32()).ShouldBe([s.Angles, s.Fractions]);
        var angles = items[0];
        (angles.GetProperty("myAccuracyPct").GetInt32(), angles.GetProperty("peerAccuracyPct").GetInt32(), angles.GetProperty("peerMedianPct").GetInt32())
            .ShouldBe((40, 78, 60));
        // Fractions: 19 classmates + the student = 20; the classmate with only 4 answers does not count.
        var fractions = items[1];
        fractions.GetProperty("myAccuracyPct").GetInt32().ShouldBe(80);
        fractions.GetProperty("peerAccuracyPct").GetInt32().ShouldBe(80);

        foreach (var forbidden in new[] { "studentId", "userId", "studentCount", "rank", "percentile", "name\":\"S" })
            raw.ShouldNotContain(forbidden, Case.Insensitive);
    }

    [Fact]
    public async Task Below_20_students_the_peer_value_is_null()
    {
        var s = await SeedAsync();
        await WithDbAsync(async db =>
        {
            // One fraction classmate leaves: 19 with the student.
            var leaving = db.Students.Single(st => st.UserId == 7710);
            db.PracticeSessionQuestions.RemoveRange(db.PracticeSessionQuestions.Where(r => r.PracticeSession.StudentId == leaving.Id && r.Question.TopicId == s.Fractions));
            await db.SaveChangesAsync();
        });
        await RunStatsJobAsync();
        var client = await StudentAsync();

        var items = (await client.GetFromJsonAsync<JsonElement>(Url, Json)).GetProperty("items").EnumerateArray().ToList();

        var fractions = items.Single(i => i.GetProperty("topicId").GetInt32() == s.Fractions);
        fractions.GetProperty("peerAccuracyPct").ValueKind.ShouldBe(JsonValueKind.Null);
        items[^1].GetProperty("topicId").GetInt32().ShouldBe(s.Fractions);
    }

    [Fact]
    public async Task Teachers_are_refused()
    {
        var teacher = await ClientAsAsync(7702, "Teacher", "kc-pc-teacher", "Teacher");

        (await teacher.GetAsync(Url)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
