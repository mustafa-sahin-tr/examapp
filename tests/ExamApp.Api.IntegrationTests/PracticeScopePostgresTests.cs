using System.Net;
using System.Net.Http.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.IntegrationTests;

/// <summary>Issue #500: gerçek Postgres ve HTTP üzerinden: yeniden kapsam (toplu silme, transaction), 409, alt konu ve soru sayısı.</summary>
[Collection(IntegrationCollection.Name)]
public class PracticeScopePostgresTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const int StudentUser = 7301;

    private sealed record Seeded(int Math, int Turkish, int SubTopic, int OtherGradeSubTopic, List<int> MathQuestions, List<int> TurkishQuestions, List<int> SubTopicQuestions);

    private async Task<Seeded> SeedAsync() => await WithDbAsync(async db =>
    {
        var grade = new Grade { Name = "5. Sınıf" };
        var otherGrade = new Grade { Name = "6. Sınıf" };
        var math = new Subject { Name = "Matematik" };
        var turkish = new Subject { Name = "Türkçe" };
        db.AddRange(grade, otherGrade, math, turkish);
        await db.SaveChangesAsync();
        db.AddRange(new GradeSubject { GradeId = grade.Id, SubjectId = math.Id }, new GradeSubject { GradeId = grade.Id, SubjectId = turkish.Id });
        var angles = new Topic { Name = "Açılar", SubjectId = math.Id, GradeId = grade.Id };
        var otherTopic = new Topic { Name = "Oranlar", SubjectId = math.Id, GradeId = otherGrade.Id };
        db.AddRange(angles, otherTopic);
        await db.SaveChangesAsync();
        var subTopic = new SubTopic { Name = "Üçgen", TopicId = angles.Id };
        var otherSubTopic = new SubTopic { Name = "Oran", TopicId = otherTopic.Id };
        var worksheet = new Worksheet { Name = "WS", Description = "", GradeId = grade.Id, SubjectId = math.Id, StudentVisibility = WorksheetStudentVisibility.Normal };
        var student = new Student { UserId = StudentUser, StudentNumber = "S7301", SchoolName = "s", GradeId = grade.Id };
        db.AddRange(subTopic, otherSubTopic, worksheet, student);
        await db.SaveChangesAsync();

        var order = 0;
        async Task<List<int>> AddAsync(int count, int subjectId, int? topicId = null, int? subTopicId = null)
        {
            var ids = new List<int>();
            for (var i = 0; i < count; i++)
            {
                var question = new Question { Text = $"Q{order}", SubjectId = subjectId, TopicId = topicId, Point = 10, DifficultyLevel = 1 };
                db.Questions.Add(question);
                await db.SaveChangesAsync();
                var right = new Answer { QuestionId = question.Id, Text = "ok", Tag = "A", Order = 0 };
                db.Answers.Add(right);
                await db.SaveChangesAsync();
                question.CorrectAnswerId = right.Id;
                db.TestQuestions.Add(new WorksheetQuestion { TestId = worksheet.Id, QuestionId = question.Id, Order = ++order });
                if (subTopicId is { } id)
                    db.Add(new QuestionSubTopic { QuestionId = question.Id, SubTopicId = id });
                await db.SaveChangesAsync();
                ids.Add(question.Id);
            }

            return ids;
        }

        var mathQuestions = await AddAsync(6, math.Id);
        var turkishQuestions = await AddAsync(6, turkish.Id);
        var subTopicQuestions = await AddAsync(3, math.Id, angles.Id, subTopic.Id);
        return new Seeded(math.Id, turkish.Id, subTopic.Id, otherSubTopic.Id, mathQuestions, turkishQuestions, subTopicQuestions);
    });

    private Task<HttpClient> StudentAsync() => ClientAsAsync(StudentUser, "Student", "kc-ps-student", "Student");

    private static async Task<DailyStartResultDto> StartDailyAsync(HttpClient client, object? scope)
    {
        var response = await client.PostAsJsonAsync("/api/practice/daily/start", scope);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<DailyStartResultDto>(Json))!;
    }

    private Task<List<int>> SetQuestionsAsync() => WithDbAsync(async db =>
        await db.DailyQuestionSetItems.AsNoTracking().OrderBy(i => i.Order).Select(i => i.QuestionId).ToListAsync());

    [Fact]
    public async Task An_opened_but_unanswered_daily_set_takes_a_new_scope_and_an_answered_one_refuses_it()
    {
        var seeded = await SeedAsync();
        var client = await StudentAsync();

        // Pano seti açtı, öğrenci bir soru gördü ama cevaplamadı.
        var opened = await StartDailyAsync(client, null);
        var shown = await client.GetFromJsonAsync<PracticeNextQuestionDto>($"/api/practice/sessions/{opened.SessionId}/next", Json);
        shown!.Question.ShouldNotBeNull();

        var scoped = await StartDailyAsync(client, new { subjectId = seeded.Turkish });
        scoped.SessionId.ShouldBe(opened.SessionId);
        scoped.Scope!.SubjectId.ShouldBe(seeded.Turkish);
        (await SetQuestionsAsync()).ShouldAllBe(id => seeded.TurkishQuestions.Contains(id));

        var next = await client.GetFromJsonAsync<PracticeNextQuestionDto>($"/api/practice/sessions/{opened.SessionId}/next", Json);
        seeded.TurkishQuestions.ShouldContain(next!.Question!.Id);

        var answer = await client.PostAsJsonAsync($"/api/practice/sessions/{opened.SessionId}/answer", new { questionId = next.Question.Id, skipped = true, timeTaken = 1 });
        answer.StatusCode.ShouldBe(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());

        var refused = await client.PostAsJsonAsync("/api/practice/daily/start", new { subjectId = seeded.Math });
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await refused.Content.ReadAsStringAsync()).ShouldContain("DailySetAlreadyStarted");

        // Devam eden ekran bozulmaz.
        (await StartDailyAsync(client, null)).SessionId.ShouldBe(opened.SessionId);
        (await StartDailyAsync(client, new { subjectId = seeded.Turkish })).SessionId.ShouldBe(opened.SessionId);
    }

    /// <summary>Issue #1109: tek ders (Türkçe) planında kendi pratiği o derse daralır; başka ders / konu 403 outOfScope.</summary>
    [Fact]
    public async Task A_single_subject_plan_narrows_practice_to_its_subject_issue1109()
    {
        var seeded = await SeedAsync();
        await WithDbAsync(async db =>
        {
            var plan = await db.SubscriptionPlans.SingleAsync(p => p.Key == SubscriptionPlan.StudentProKey);
            db.UserSubscriptionPlans.Add(new UserSubscriptionPlan
            {
                UserId = StudentUser, PlanId = plan.Id, ValidFromUtc = DateTime.UtcNow.AddDays(-1), ScopeSubjectId = seeded.Turkish,
            });
            await db.SaveChangesAsync();
        });
        var client = await StudentAsync();

        var created = await client.PostAsJsonAsync("/api/practice/sessions", new { targetCount = 3 });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var session = (await created.Content.ReadFromJsonAsync<PracticeSessionDto>(Json))!;
        session.SubjectIds.ShouldBe([seeded.Turkish]);
        var next = await client.GetFromJsonAsync<PracticeNextQuestionDto>($"/api/practice/sessions/{session.Id}/next", Json);
        seeded.TurkishQuestions.ShouldContain(next!.Question!.Id);

        foreach (var body in new object[] { new { subjectIds = new[] { seeded.Math } }, new { subTopicIds = new[] { seeded.SubTopic } } })
        {
            var refused = await client.PostAsJsonAsync("/api/practice/sessions", body);
            refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            var limit = (await refused.Content.ReadFromJsonAsync<EntitlementLimitReachedDto>(Json))!;
            (limit.Code, limit.ScopeSubjectId).ShouldBe((EntitlementLimitReachedDto.OutOfScopeCode, (int?)seeded.Turkish));
        }

        (await client.GetAsync($"/api/practice/mistakes?subjectId={seeded.Math}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/api/practice/due-review?subjectId={seeded.Math}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/practice/mistakes")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>Issue #1109: tek ders planında kendi destesi yalnız planın dersine (ya da derssiz) açılır.</summary>
    [Fact]
    public async Task A_single_subject_plan_keeps_own_flashcard_decks_in_its_subject_issue1109()
    {
        var seeded = await SeedAsync();
        await WithDbAsync(async db =>
        {
            var plan = await db.SubscriptionPlans.SingleAsync(p => p.Key == SubscriptionPlan.StudentProKey);
            db.UserSubscriptionPlans.Add(new UserSubscriptionPlan
            {
                UserId = StudentUser, PlanId = plan.Id, ValidFromUtc = DateTime.UtcNow.AddDays(-1), ScopeSubjectId = seeded.Turkish,
            });
            await db.SaveChangesAsync();
        });
        var client = await StudentAsync();

        var refused = await client.PostAsJsonAsync("/api/flashcards/decks", new { title = "Mat", subjectId = seeded.Math });
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await refused.Content.ReadFromJsonAsync<EntitlementLimitReachedDto>(Json))!.Code.ShouldBe(EntitlementLimitReachedDto.OutOfScopeCode);

        var created = await client.PostAsJsonAsync("/api/flashcards/decks", new { title = "Türkçe", subjectId = seeded.Turkish });
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var deck = (await created.Content.ReadFromJsonAsync<FlashcardDeckDetailDto>(Json))!;
        (await client.PutAsJsonAsync($"/api/flashcards/decks/{deck.Id}", new { title = "Mat", subjectId = seeded.Math }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync("/api/flashcards/decks", new { title = "Genel" })).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>Issue #1109: tek ders planında çalışma sayfası listesi o derse daralır, başka dersin sayfası 403.</summary>
    [Fact]
    public async Task A_single_subject_plan_narrows_study_pages_to_its_subject_issue1109()
    {
        var seeded = await SeedAsync();
        var (mathPage, turkishPage) = await WithDbAsync(async db =>
        {
            var plan = await db.SubscriptionPlans.SingleAsync(p => p.Key == SubscriptionPlan.StudentProKey);
            db.UserSubscriptionPlans.Add(new UserSubscriptionPlan
            {
                UserId = StudentUser, PlanId = plan.Id, ValidFromUtc = DateTime.UtcNow.AddDays(-1), ScopeSubjectId = seeded.Turkish,
            });
            StudyItem Page(string title, int subjectId) => new()
            {
                Title = title, Description = "", IsPublished = true, SubjectId = subjectId,
                CreatedByUserId = 1, CreatedByName = "T", CreatedByRole = "Teacher",
            };
            var math = Page("Mat", seeded.Math);
            var turkish = Page("Türkçe", seeded.Turkish);
            db.StudyItems.AddRange(math, turkish);
            await db.SaveChangesAsync();
            return (math.Id, turkish.Id);
        });
        var client = await StudentAsync();

        var page = await client.GetFromJsonAsync<Paged<StudyItemDto>>("/api/study-items?pageSize=50", Json);
        page!.Items.Select(i => i.Id).ShouldBe([turkishPage]);
        (await client.GetAsync($"/api/study-items?subjectId={seeded.Math}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/api/study-items/{mathPage}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/api/study-items/{turkishPage}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_free_session_filters_by_subtopic_and_stops_at_the_target()
    {
        var seeded = await SeedAsync();
        var client = await StudentAsync();

        var created = await client.PostAsJsonAsync("/api/practice/sessions", new { subTopicIds = new[] { seeded.SubTopic }, targetCount = 2 });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var session = (await created.Content.ReadFromJsonAsync<PracticeSessionDto>(Json))!;
        (session.SubTopicIds.Single(), session.TargetCount).ShouldBe((seeded.SubTopic, (int?)2));

        for (var i = 0; i < 2; i++)
        {
            var next = await client.GetFromJsonAsync<PracticeNextQuestionDto>($"/api/practice/sessions/{session.Id}/next", Json);
            seeded.SubTopicQuestions.ShouldContain(next!.Question!.Id);
            (await client.PostAsJsonAsync($"/api/practice/sessions/{session.Id}/answer", new { questionId = next.Question.Id, skipped = true, timeTaken = 1 })).EnsureSuccessStatusCode();
        }

        var end = await client.GetFromJsonAsync<PracticeNextQuestionDto>($"/api/practice/sessions/{session.Id}/next", Json);
        (end!.Question, end.TargetReached).ShouldBe((null, true));
    }

    [Theory]
    [InlineData(true, 5)]
    [InlineData(false, 0)]
    [InlineData(false, 51)]
    public async Task Bad_free_session_requests_are_refused(bool otherGradeSubTopic, int target)
    {
        var seeded = await SeedAsync();
        var client = await StudentAsync();
        var subTopics = otherGradeSubTopic ? new[] { seeded.OtherGradeSubTopic } : new[] { seeded.SubTopic };

        var response = await client.PostAsJsonAsync("/api/practice/sessions", new { subTopicIds = subTopics, targetCount = target });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
