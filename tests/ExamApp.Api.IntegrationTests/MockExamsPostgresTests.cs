using System.Net;
using System.Net.Http.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Worksheets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>Issue #693: gerçek Postgres ve HTTP üzerinden. Kimlik sayacı, oturum kilitleri ve tam akış SQLite'ta görünmez.</summary>
[Collection(IntegrationCollection.Name)]
public class MockExamsPostgresTests : IntegrationTestBase
{
    private const int AdminUser = 7001;
    private const int TeacherUser = 7002;
    private const int StudentUser = 7003;
    private const string Api = "/api/mock-exams";

    public MockExamsPostgresTests(IntegrationApiFactory factory) : base(factory)
    {
    }

    private static ExamTemplateDto NewTemplate() => new()
    {
        Code = "kisa",
        Name = "Kısa deneme",
        WrongAnswersPerCorrect = 4,
        SourceReference = "test",
        Sessions = [new ExamTemplateSessionDto { Name = "Tek", DurationSeconds = 600, Sections = [new ExamTemplateSectionDto { Name = "Matematik", QuestionCount = 2 }] }],
    };

    private Task<HttpClient> AdminAsync() => ClientAsAsync(AdminUser, "Admin", "kc-admin", "Admin");

    private async Task<HttpClient> TeacherAsync()
    {
        await SeedApprovedTeacherAsync(TeacherUser);
        return await ClientAsAsync(TeacherUser, "Teacher", "kc-teacher", "Teacher");
    }

    private async Task<ExamTemplateDto> CreateTemplateAsync()
    {
        var response = await (await AdminAsync()).PostAsJsonAsync($"{Api}/templates", NewTemplate());
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ExamTemplateDto>(Json))!;
    }

    private Task<(int GradeId, List<int> Questions)> SeedAsync() =>
        WithDbAsync(async db =>
        {
            var grade = new Grade { Name = "8. Sınıf" };
            db.Add(grade);
            await db.SaveChangesAsync();
            db.SetCurrentUser(TeacherUser);
            var ids = new List<int>();
            for (var i = 0; i < 3; i++)
            {
                var question = new Question { Text = $"Soru {i}", Point = 5, DifficultyLevel = 1, InteractionType = "mcq" };
                db.Questions.Add(question);
                await db.SaveChangesAsync();
                var answers = new[] { new Answer { QuestionId = question.Id, Text = "doğru", Order = 1 }, new Answer { QuestionId = question.Id, Text = "yanlış", Order = 2 } };
                db.Answers.AddRange(answers);
                await db.SaveChangesAsync();
                question.CorrectAnswerId = answers[0].Id;
                await db.SaveChangesAsync();
                ids.Add(question.Id);
            }
            return (grade.Id, ids);
        });

    private static async Task<MockExamDetailDto> ReadAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<MockExamDetailDto>(Json))!;
    }

    [Fact]
    public async Task A_new_template_does_not_collide_with_the_seeded_ids()
    {
        var created = await CreateTemplateAsync();

        created.Id.ShouldBeGreaterThanOrEqualTo(1000);
        created.Sessions[0].Id.ShouldBeGreaterThanOrEqualTo(1000);
        created.Sessions[0].Sections[0].Id.ShouldBeGreaterThanOrEqualTo(1000);

        var list = await (await TeacherAsync()).GetFromJsonAsync<List<ExamTemplateDto>>($"{Api}/templates", Json);
        list!.ShouldContain(t => t.Code == "kisa");

        // Değiştirme eski satırları yumuşak siler: yeni satırlar çakışmamalı, kod aynı kalabilmeli.
        var changed = NewTemplate();
        changed.Sessions[0].Sections[0].QuestionCount = 3;
        var update = await (await AdminAsync()).PutAsJsonAsync($"{Api}/templates/{created.Id}", changed);
        update.StatusCode.ShouldBe(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Teacher_cannot_define_templates_and_student_cannot_build_exams()
    {
        var teacher = await TeacherAsync();
        (await teacher.PostAsJsonAsync($"{Api}/templates", NewTemplate())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var student = await ClientAsAsync(StudentUser, "Student", "kc-student", "Student");
        (await student.GetAsync(Api)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Anonymous().GetAsync($"{Api}/mine")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Full_flow_from_template_to_net_with_sessions_locked_against_other_screens()
    {
        var template = await CreateTemplateAsync();
        var (gradeId, questions) = await SeedAsync();
        var teacher = await TeacherAsync();

        var exam = await ReadAsync(await teacher.PostAsJsonAsync(Api, new CreateMockExamDto { ExamTemplateId = template.Id, Name = "Deneme", GradeId = gradeId }));
        var section = exam.Sessions[0].Sections[0];
        var worksheetId = exam.Sessions[0].WorksheetId;

        // Eksik soruyla yayın yok.
        var refused = await teacher.PostAsJsonAsync($"{Api}/{exam.Id}/publish", new PublishMockExamDto { OpenToGrade = true });
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Soru çıkarılıp yeniden eklenebilir: yumuşak silinen satırlar benzersiz dizinlere takılmamalı.
        var path = $"{Api}/{exam.Id}/sections/{section.SectionId}/questions";
        await ReadAsync(await teacher.PutAsJsonAsync(path, new SetSectionQuestionsDto { QuestionIds = [questions[0], questions[1]] }));
        await ReadAsync(await teacher.PutAsJsonAsync(path, new SetSectionQuestionsDto { QuestionIds = [questions[1]] }));
        var filled = await ReadAsync(await teacher.PutAsJsonAsync(path, new SetSectionQuestionsDto { QuestionIds = [questions[0], questions[1]] }));
        filled.Problems.ShouldBeEmpty();
        filled.Sessions[0].Sections[0].Questions.Select(q => q.Text).ShouldBe(["Soru 0", "Soru 1"], ignoreOrder: true);

        // Oturum testler ekranında düzenlenebilir görünmez ve kopyayla dışarı çıkmaz; ayrıntısı atama için açık kalır.
        (await (await teacher.GetAsync("/api/worksheet/list?pageNumber=1&pageSize=50")).Content.ReadAsStringAsync()).ShouldNotContain("Deneme");
        var sessionDetail = await teacher.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/worksheet/{worksheetId}/detail", Json);
        sessionDetail.GetProperty("worksheet").GetProperty("canEdit").GetBoolean().ShouldBeFalse();
        (await teacher.PostAsync($"/api/worksheet/{worksheetId}/copy", null)).IsSuccessStatusCode.ShouldBeFalse();

        // Taslak oturum başka ekranlardan değişmez.
        (await teacher.DeleteAsync($"/api/worksheet/{worksheetId}")).IsSuccessStatusCode.ShouldBeFalse();
        (await teacher.DeleteAsync($"/api/questions/test/{worksheetId}/question/{questions[0]}")).IsSuccessStatusCode.ShouldBeFalse();
        (await teacher.PostAsJsonAsync($"/api/questions/test/{worksheetId}/bank-questions", new { questionIds = new[] { questions[2] } })).IsSuccessStatusCode.ShouldBeFalse();

        (await ReadAsync(await teacher.PostAsJsonAsync($"{Api}/{exam.Id}/publish", new PublishMockExamDto { OpenToGrade = true }))).PublishedAt.ShouldNotBeNull();

        await WithDbAsync(async db =>
        {
            // Yayın kaydı açmaz: normal listeler ve pratik havuzu bu kaydı görmez.
            (await db.Worksheets.AsNoTracking().SingleAsync(w => w.Id == worksheetId)).StudentVisibility.ShouldBe(WorksheetStudentVisibility.Restricted);
            db.Add(new Student { UserId = StudentUser, StudentNumber = "S7003", SchoolName = "s", GradeId = gradeId });
            await db.SaveChangesAsync();
        });

        var student = await ClientAsAsync(StudentUser, "Student", "kc-student", "Student");
        var mine = await student.GetFromJsonAsync<List<StudentMockExamDto>>($"{Api}/mine", Json);
        mine!.ShouldHaveSingleItem().Sessions.ShouldHaveSingleItem().Status.ShouldBe("NotStarted");

        var start = await student.PostAsync($"/api/worksheet/start-test/{worksheetId}", null);
        start.StatusCode.ShouldBe(HttpStatusCode.OK, await start.Content.ReadAsStringAsync());
        var instanceId = (await start.Content.ReadFromJsonAsync<TestStartResultDto>(Json))!.InstanceId;

        // Süren oturumda sonuç sayısı yok.
        var running = await student.GetFromJsonAsync<MockExamResultDto>($"{Api}/{exam.Id}/result", Json);
        running!.Sections.ShouldHaveSingleItem().Pending.ShouldBeTrue();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sessions = scope.ServiceProvider.GetRequiredService<ITestSessionService>();
            var user = new UserProfileDto { Id = StudentUser, KeycloakId = "kc-student", Role = "Student" };
            var rows = await db.TestInstanceQuestions.AsNoTracking()
                .Where(r => r.WorksheetInstanceId == instanceId)
                .Select(r => new { r.Id, r.WorksheetQuestion.QuestionId })
                .ToListAsync();
            var right = await db.Questions.AsNoTracking().Where(q => q.Id == questions[0]).Select(q => q.CorrectAnswerId!.Value).SingleAsync();
            var wrong = await db.Answers.AsNoTracking().Where(a => a.QuestionId == questions[1] && a.Text == "yanlış").Select(a => a.Id).SingleAsync();
            (await sessions.SaveAnswer(new SaveAnswerDto { TestInstanceId = instanceId, TestQuestionId = rows.Single(r => r.QuestionId == questions[0]).Id, SelectedAnswerId = right, TimeTaken = 2 }, user)).Success.ShouldBeTrue();
            (await sessions.SaveAnswer(new SaveAnswerDto { TestInstanceId = instanceId, TestQuestionId = rows.Single(r => r.QuestionId == questions[1]).Id, SelectedAnswerId = wrong, TimeTaken = 2 }, user)).Success.ShouldBeTrue();
            (await sessions.EndTest(instanceId, StudentUser)).Success.ShouldBeTrue();
        }

        var result = await student.GetFromJsonAsync<MockExamResultDto>($"{Api}/{exam.Id}/result", Json);
        result!.Complete.ShouldBeTrue();
        var math = result.Sections.ShouldHaveSingleItem();
        (math.Correct, math.Wrong, math.Blank, math.Net).ShouldBe((1, 1, 0, 0.75m));
        result.Net.ShouldBe(0.75m);

        // Yayınlanmış deneme de başka ekrandan değişmez.
        (await teacher.DeleteAsync($"/api/worksheet/{worksheetId}")).IsSuccessStatusCode.ShouldBeFalse();
    }
}
