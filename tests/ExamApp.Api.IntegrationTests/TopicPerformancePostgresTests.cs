using System.Net;
using System.Net.Http.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.IntegrationTests;

/// <summary>Issue #497: gerçek Postgres ve HTTP üzerinden. Sorguların çevirisi, yetki ve parametre sınırları SQLite'ta görünmez.</summary>
[Collection(IntegrationCollection.Name)]
public class TopicPerformancePostgresTests : IntegrationTestBase
{
    private const int StudentUser = 7101;
    private const string Api = "/api/student/me/topic-performance";

    public TopicPerformancePostgresTests(IntegrationApiFactory factory) : base(factory)
    {
    }

    private Task<HttpClient> StudentAsync() => ClientAsAsync(StudentUser, "Student", "kc-tp-student", "Student");

    private async Task SeedAsync()
    {
        await WithDbAsync(async db =>
        {
            var grade = new Grade { Name = "5. Sınıf" };
            var subject = new Subject { Name = "Matematik" };
            db.AddRange(grade, subject);
            await db.SaveChangesAsync();
            var topic = new Topic { Name = "Açılar", SubjectId = subject.Id, GradeId = grade.Id };
            db.Add(topic);
            await db.SaveChangesAsync();
            var subTopic = new SubTopic { Name = "Üçgen", TopicId = topic.Id };
            var student = new Student { UserId = StudentUser, StudentNumber = "S7101", SchoolName = "s", GradeId = grade.Id };
            db.AddRange(subTopic, student);
            await db.SaveChangesAsync();

            var question = new Question { Text = "Soru", Point = 5, DifficultyLevel = 5, InteractionType = "mcq", SubjectId = subject.Id };
            db.Questions.Add(question);
            await db.SaveChangesAsync();
            var right = new Answer { QuestionId = question.Id, Text = "doğru", Order = 1 };
            var wrong = new Answer { QuestionId = question.Id, Text = "yanlış", Order = 2 };
            db.Answers.AddRange(right, wrong);
            await db.SaveChangesAsync();
            question.CorrectAnswerId = right.Id;
            db.QuestionSubTopics.Add(new QuestionSubTopic { QuestionId = question.Id, SubTopicId = subTopic.Id });

            var now = DateTime.UtcNow;
            var worksheet = new Worksheet { Name = "Test", Description = "d", GradeId = grade.Id };
            var running = new Worksheet { Name = "Süren", Description = "d", GradeId = grade.Id };
            db.Worksheets.AddRange(worksheet, running);
            await db.SaveChangesAsync();
            var item = new WorksheetQuestion { TestId = worksheet.Id, QuestionId = question.Id, Order = 1 };
            var runningItem = new WorksheetQuestion { TestId = running.Id, QuestionId = question.Id, Order = 1 };
            db.AddRange(item, runningItem);
            var finished = new WorksheetInstance { StudentId = student.Id, WorksheetId = worksheet.Id, StartTime = now.AddDays(-2), EndTime = now.AddDays(-2).AddMinutes(5), Status = WorksheetInstanceStatus.Completed };
            var ongoing = new WorksheetInstance { StudentId = student.Id, WorksheetId = running.Id, StartTime = now.AddMinutes(-1), Status = WorksheetInstanceStatus.Started };
            db.TestInstances.AddRange(finished, ongoing);
            await db.SaveChangesAsync();
            db.TestInstanceQuestions.AddRange(
                new WorksheetInstanceQuestion { WorksheetInstanceId = finished.Id, WorksheetQuestionId = item.Id, SelectedAnswerId = wrong.Id, TimeTaken = 30 },
                new WorksheetInstanceQuestion { WorksheetInstanceId = ongoing.Id, WorksheetQuestionId = runningItem.Id, SelectedAnswerId = right.Id, TimeTaken = 5 });

            var session = new PracticeSession { StudentId = student.Id, GradeId = grade.Id, StartTime = now.AddDays(-1) };
            session.Questions.Add(new PracticeSessionQuestion { QuestionId = question.Id, ShownAt = now.AddDays(-1), AnsweredAt = now.AddDays(-1), SelectedAnswerId = right.Id, IsCorrect = true, TimeTaken = 10 });
            db.PracticeSessions.Add(session);
            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task Student_gets_tests_and_practice_by_topic_without_the_running_test()
    {
        await SeedAsync();
        var client = await StudentAsync();

        var result = await client.GetFromJsonAsync<TopicPerformanceDto>($"{Api}?minQuestions=1", Json);

        var item = result!.Items.ShouldHaveSingleItem();
        (item.TopicName, item.SubjectName).ShouldBe(("Açılar", "Matematik"));
        (item.Correct, item.Wrong, item.Blank, item.Total).ShouldBe((1, 1, 0, 2));
        item.AccuracyPct.ShouldBe(50m);
        item.AvgTimeSec.ShouldBe(20m);
        result.UnclassifiedCount.ShouldBe(0);

        var bySubTopic = await client.GetFromJsonAsync<TopicPerformanceDto>($"{Api}?minQuestions=1&groupBy=subtopic&days=90", Json);
        bySubTopic!.Items.ShouldHaveSingleItem().SubTopicName.ShouldBe("Üçgen");
    }

    [Fact]
    public async Task The_default_threshold_hides_a_topic_with_too_few_questions()
    {
        await SeedAsync();
        var client = await StudentAsync();

        var result = await client.GetFromJsonAsync<TopicPerformanceDto>(Api, Json);

        result!.Items.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("days=0", "InvalidDays")]
    [InlineData("days=91", "InvalidDays")]
    [InlineData("groupBy=grade", "InvalidGroupBy")]
    [InlineData("minQuestions=0", "InvalidMinQuestions")]
    public async Task Out_of_range_parameters_are_refused(string query, string errorCode)
    {
        await SeedAsync();
        var client = await StudentAsync();

        var response = await client.GetAsync($"{Api}?{query}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain(errorCode);
    }

    [Fact]
    public async Task Weekly_timeline_counts_tests_and_practice_for_a_topic()
    {
        await SeedAsync();
        var client = await StudentAsync();
        var bySubject = await client.GetFromJsonAsync<TopicPerformanceDto>($"{Api}?minQuestions=1", Json);
        var topicId = bySubject!.Items.Single().TopicId;

        var result = await client.GetFromJsonAsync<TopicProgressTimelineDto>($"{Api}/timeline?topicId={topicId}&weeks=3", Json);

        result!.Weeks.Count.ShouldBe(3);
        result.Weeks.Sum(w => w.Answered).ShouldBe(2);
        result.Weeks.Sum(w => w.Correct).ShouldBe(1);
        result.Weeks.ShouldContain(w => w.AccuracyPct != null);
    }

    [Theory]
    [InlineData("", "InvalidTimelineTarget")]
    [InlineData("topicId=1&subTopicId=1", "InvalidTimelineTarget")]
    [InlineData("topicId=1&weeks=0", "InvalidWeeks")]
    [InlineData("topicId=1&weeks=27", "InvalidWeeks")]
    public async Task Timeline_refuses_bad_parameters(string query, string errorCode)
    {
        await SeedAsync();
        var client = await StudentAsync();

        var response = await client.GetAsync($"{Api}/timeline?{query}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain(errorCode);
    }

    [Fact]
    public async Task Only_a_student_can_read_it()
    {
        (await Anonymous().GetAsync(Api)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Anonymous().GetAsync($"{Api}/timeline?topicId=1")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        await SeedApprovedTeacherAsync(7102);
        var teacher = await ClientAsAsync(7102, "Teacher", "kc-tp-teacher", "Teacher");
        (await teacher.GetAsync(Api)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await teacher.GetAsync($"{Api}/timeline?topicId=1")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
