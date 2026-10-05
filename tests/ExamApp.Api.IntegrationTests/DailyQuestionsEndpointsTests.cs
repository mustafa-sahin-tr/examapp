using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #99 — "Günün soruları" uçları gerçek Postgres'te: eşzamanlı ilk GET'te tek set ((StudentId, Day) unique index +
/// yakala-yeniden-oku), eşzamanlı start'ta tek oturum (koşullu UPDATE), JSON alan adları (UI sözleşmesi), uçtan uca çözme
/// (cevaplar AnswerSubmittedEvent outbox'ına), başka öğrencinin oturumuna erişim reddi.
/// </summary>
public class DailyQuestionsEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private sealed record Seeded(int StudentId, Dictionary<int, int> CorrectAnswerByQuestion);

    private async Task<Seeded> SeedAsync(int userId, int questionCount, int? extraStudentUserId = null)
    {
        return await WithDbAsync(async db =>
        {
            var grade = new Grade { Name = "7" };
            var subject = new Subject { Name = "Matematik" };
            db.AddRange(grade, subject);
            await db.SaveChangesAsync();

            var ws = new Worksheet { Name = "WS", Description = "", GradeId = grade.Id, SubjectId = subject.Id, StudentVisibility = WorksheetStudentVisibility.Normal };
            db.Worksheets.Add(ws);
            var student = new Student { UserId = userId, StudentNumber = $"D{userId}", GradeId = grade.Id };
            db.Students.Add(student);
            if (extraStudentUserId.HasValue)
                db.Students.Add(new Student { UserId = extraStudentUserId.Value, StudentNumber = $"D{extraStudentUserId}", GradeId = grade.Id });
            await db.SaveChangesAsync();

            var correctByQuestion = new Dictionary<int, int>();
            for (var i = 0; i < questionCount; i++)
            {
                var q = new Question { Text = $"Q{i}", SubjectId = subject.Id, Point = 10, DifficultyLevel = 1 };
                db.Questions.Add(q);
                await db.SaveChangesAsync();
                var correct = new Answer { QuestionId = q.Id, Text = "ok", Tag = "A", Order = 0 };
                var wrong = new Answer { QuestionId = q.Id, Text = "no", Tag = "B", Order = 1 };
                db.Answers.AddRange(correct, wrong);
                await db.SaveChangesAsync();
                q.CorrectAnswerId = correct.Id;
                db.TestQuestions.Add(new WorksheetQuestion { TestId = ws.Id, QuestionId = q.Id, Order = i + 1 });
                await db.SaveChangesAsync();
                correctByQuestion[q.Id] = correct.Id;
            }

            return new Seeded(student.Id, correctByQuestion);
        });
    }

    [Fact]
    public async Task Concurrent_first_requests_create_a_single_set()
    {
        const int userId = 99101;
        var seeded = await SeedAsync(userId, 12);
        var client = await ClientAsAsync(userId, "Student", "kc-daily-1", "Student");

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.GetAsync("/api/practice/daily")));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);
        var dtos = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<DailySetDto>(Json)));
        dtos.Select(d => d!.Total).Distinct().ShouldBe(new[] { 5 });

        var (sets, items) = await WithDbAsync(async db => (
            await db.DailyQuestionSets.CountAsync(d => d.StudentId == seeded.StudentId),
            await db.DailyQuestionSetItems.CountAsync()));
        sets.ShouldBe(1);
        items.ShouldBe(5);
    }

    [Fact]
    public async Task Concurrent_starts_link_a_single_session()
    {
        const int userId = 99102;
        var seeded = await SeedAsync(userId, 6);
        var client = await ClientAsAsync(userId, "Student", "kc-daily-2", "Student");
        (await client.GetAsync("/api/practice/daily")).EnsureSuccessStatusCode();

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => client.PostAsync("/api/practice/daily/start", null)));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);
        var results = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<DailyStartResultDto>(Json)));
        results.Select(r => r!.SessionId).Distinct().Count().ShouldBe(1);

        var sessions = await WithDbAsync(db => db.PracticeSessions.CountAsync(p => p.StudentId == seeded.StudentId));
        sessions.ShouldBe(1);
    }

    [Fact]
    public async Task Daily_flow_uses_contract_field_names_and_completes_through_practice_endpoints()
    {
        const int userId = 99103;
        var seeded = await SeedAsync(userId, 3);
        var client = await ClientAsAsync(userId, "Student", "kc-daily-3", "Student");

        // UI sözleşmesi: kesin JSON alan adları.
        using (var doc = JsonDocument.Parse(await client.GetStringAsync("/api/practice/daily")))
        {
            var root = doc.RootElement;
            foreach (var name in new[] { "date", "status", "total", "targetCount", "answered", "correct", "wrong", "skipped", "sessionId", "scope" })
                root.TryGetProperty(name, out _).ShouldBeTrue(name);
            root.GetProperty("status").GetString().ShouldBe("NotStarted");
            root.GetProperty("total").GetInt32().ShouldBe(3);
            root.GetProperty("targetCount").GetInt32().ShouldBe(5);
            root.GetProperty("sessionId").ValueKind.ShouldBe(JsonValueKind.Null);
            root.GetProperty("scope").ValueKind.ShouldBe(JsonValueKind.Null);
            root.GetProperty("date").GetString()!.Length.ShouldBe(10); // yyyy-MM-dd
        }

        var start = await (await client.PostAsync("/api/practice/daily/start", null)).Content.ReadFromJsonAsync<DailyStartResultDto>(Json);
        start!.Status.ShouldBe("NotStarted");

        for (var i = 0; i < 3; i++)
        {
            var next = await client.GetFromJsonAsync<PracticeNextQuestionDto>($"/api/practice/sessions/{start.SessionId}/next", Json);
            next!.Question.ShouldNotBeNull();
            var qid = next.Question.Id;
            var answer = await client.PostAsJsonAsync($"/api/practice/sessions/{start.SessionId}/answer",
                new PracticeAnswerSubmitDto { QuestionId = qid, SelectedAnswerId = seeded.CorrectAnswerByQuestion[qid], TimeTaken = 5 });
            answer.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var done = await client.GetFromJsonAsync<DailySetDto>("/api/practice/daily", Json);
        done!.Status.ShouldBe(DailySetStatus.Completed);
        done.SessionId.ShouldBe(start.SessionId);
        done.Correct.ShouldBe(3);

        var again = await (await client.PostAsync("/api/practice/daily/start", null)).Content.ReadFromJsonAsync<DailyStartResultDto>(Json);
        again!.SessionId.ShouldBe(start.SessionId);
        again.Status.ShouldBe(DailySetStatus.Completed);

        var outbox = await WithDbAsync(db => db.OutboxMessages.AsNoTracking()
            .Where(o => o.Type == OutboxEventRegistry.NameFor<AnswerSubmittedEvent>()).ToListAsync());
        outbox.Count.ShouldBe(3);
        outbox.Select(o => JsonSerializer.Deserialize<AnswerSubmittedEvent>(o.Content)!)
            .ShouldAllBe(e => e.UserId == userId && e.ClientId == "kc-daily-3" && e.TestInstanceId == -start.SessionId);
    }

    [Fact]
    public async Task Another_students_daily_session_is_not_accessible()
    {
        const int ownerUserId = 99104;
        const int intruderUserId = 99105;
        await SeedAsync(ownerUserId, 5, extraStudentUserId: intruderUserId);
        var owner = await ClientAsAsync(ownerUserId, "Student", "kc-daily-4", "Student");
        var intruder = await ClientAsAsync(intruderUserId, "Student", "kc-daily-5", "Student");

        var start = await (await owner.PostAsync("/api/practice/daily/start", null)).Content.ReadFromJsonAsync<DailyStartResultDto>(Json);

        (await intruder.GetAsync($"/api/practice/sessions/{start!.SessionId}/next")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await intruder.PostAsJsonAsync($"/api/practice/sessions/{start.SessionId}/answer",
            new PracticeAnswerSubmitDto { QuestionId = 1, SelectedAnswerId = 1 })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await intruder.GetAsync($"/api/practice/sessions/{start.SessionId}/review")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Empty_pool_returns_Empty_and_start_conflicts()
    {
        const int userId = 99106;
        await SeedAsync(userId, 0);
        var client = await ClientAsAsync(userId, "Student", "kc-daily-6", "Student");

        var dto = await client.GetFromJsonAsync<DailySetDto>("/api/practice/daily", Json);
        dto!.Status.ShouldBe(DailySetStatus.Empty);

        (await client.PostAsync("/api/practice/daily/start", null)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Parallel_answers_for_the_same_daily_question_record_once_and_emit_one_event()
    {
        const int userId = 99107;
        var seeded = await SeedAsync(userId, 3);
        var client = await ClientAsAsync(userId, "Student", "kc-daily-7", "Student");
        var start = await (await client.PostAsync("/api/practice/daily/start", null)).Content.ReadFromJsonAsync<DailyStartResultDto>(Json);
        var next = await client.GetFromJsonAsync<PracticeNextQuestionDto>($"/api/practice/sessions/{start!.SessionId}/next", Json);
        var qid = next!.Question!.Id;

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => client.PostAsJsonAsync(
            $"/api/practice/sessions/{start.SessionId}/answer",
            new PracticeAnswerSubmitDto { QuestionId = qid, SelectedAnswerId = seeded.CorrectAnswerByQuestion[qid], TimeTaken = 5 })));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.BadRequest).ShouldBe(5);
        (await WithDbAsync(db => db.OutboxMessages.CountAsync())).ShouldBe(1);
    }

    [Fact]
    public async Task Daily_endpoints_are_rate_limited_per_student()
    {
        const int userId = 99108;
        await SeedAsync(userId, 2);
        var client = await ClientAsAsync(userId, "Student", "kc-daily-8", "Student");

        HttpResponseMessage? last = null;
        for (var i = 0; i < 31; i++)
            last = await client.GetAsync("/api/practice/daily");

        last!.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await Factory.CreateClient().GetAsync("/api/practice/daily")).StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Session_history_excludes_the_daily_session()
    {
        const int userId = 99109;
        await SeedAsync(userId, 3);
        var client = await ClientAsAsync(userId, "Student", "kc-daily-9", "Student");
        var start = await (await client.PostAsync("/api/practice/daily/start", null)).Content.ReadFromJsonAsync<DailyStartResultDto>(Json);

        using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/practice/sessions"));
        doc.RootElement.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("id").GetInt32()).ShouldNotContain(start!.SessionId);
    }
}
