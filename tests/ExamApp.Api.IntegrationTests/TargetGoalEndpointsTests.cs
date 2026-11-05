using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.TargetGoals;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.IntegrationTests;

/// <summary>Issue #695: hedef seçimi, son tamamlanmış denemeler, veli erişimi ve kapatma ayarı. Veriler sentetiktir.</summary>
[Collection(IntegrationCollection.Name)]
public class TargetGoalEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const int AdminUser = 69501;
    private const int StudentUser = 69502;
    private const int ParentUser = 69503;
    private const int OtherParentUser = 69504;
    private const int TeacherUser = 69505;
    private const string StudentApi = "/api/student/me/target-goal";
    private const string AdminApi = "/api/admin/target-programs";

    private Task<HttpClient> AdminAsync() => ClientAsAsync(AdminUser, "Admin", $"kc-{AdminUser}", "Admin");
    private Task<HttpClient> StudentAsync() => ClientAsAsync(StudentUser, "Student", $"kc-{StudentUser}", "Student");

    private Task<int> SeedStudentAsync() => WithDbAsync(async db =>
    {
        var grade = new Grade { Name = "8. Sınıf" };
        db.Add(grade);
        await db.SaveChangesAsync();
        var student = new Student { UserId = StudentUser, StudentNumber = "P69502", SchoolName = "s", GradeId = grade.Id };
        db.Add(student);
        db.Parents.AddRange(new Parent { UserId = ParentUser }, new Parent { UserId = OtherParentUser });
        await db.SaveChangesAsync();
        return student.Id;
    });

    private Task<int> SeedProgramAsync(string scoreType = TargetScoreTypes.Lgs) => WithDbAsync(async db =>
    {
        var program = new TargetProgram
        {
            ScoreType = scoreType, Name = "Örnek Lisesi", City = "Ankara", Year = 2026, MinimumScore = 450, Percentile = 2.5m,
            SourceReference = "https://example.test/source", NormalizedKey = "ornek lisesi||ankara|",
        };
        db.TargetPrograms.Add(program);
        await db.SaveChangesAsync();
        return program.Id;
    });

    /// <summary>Şablonun bütün oturumlarını kurar; <paramref name="finishedAt"/> boşsa son oturum sürüyor sayılır.</summary>
    private Task<int> SeedExamAsync(int studentId, string name, DateTime? finishedAt) => WithDbAsync(async db =>
    {
        var gradeId = await db.Students.Where(s => s.Id == studentId).Select(s => s.GradeId!.Value).SingleAsync();
        // Test tabanı her testte boşaltılır, tohum şablonu kalmaz.
        var template = await db.ExamTemplates.Include(t => t.Sessions).FirstOrDefaultAsync(t => t.Code == "lgs");
        if (template is null)
        {
            template = new ExamTemplate
            {
                Code = "lgs", Name = "LGS", WrongAnswersPerCorrect = 3,
                Sessions =
                [
                    new ExamTemplateSession { Order = 1, Name = "Sözel", DurationSeconds = 600, Sections = [new ExamTemplateSection { Order = 1, Name = "Türkçe", QuestionCount = 20 }] },
                    new ExamTemplateSession { Order = 2, Name = "Sayısal", DurationSeconds = 600, Sections = [new ExamTemplateSection { Order = 1, Name = "Matematik", QuestionCount = 20 }] },
                ],
            };
            db.Add(template);
            await db.SaveChangesAsync();
        }
        var templateSessions = template.Sessions.OrderBy(s => s.Order).ToList();
        var exam = new MockExam { ExamTemplateId = template.Id, Name = name, GradeId = gradeId, PublishedAt = DateTime.UtcNow };
        db.Add(exam);
        await db.SaveChangesAsync();

        for (var i = 0; i < templateSessions.Count; i++)
        {
            var worksheet = new Worksheet { Name = $"{name} {i}", Description = "", GradeId = gradeId };
            db.Add(worksheet);
            await db.SaveChangesAsync();
            db.Add(new MockExamSession { MockExamId = exam.Id, ExamTemplateSessionId = templateSessions[i].Id, WorksheetId = worksheet.Id });
            var running = finishedAt is null && i == templateSessions.Count - 1;
            db.TestInstances.Add(new WorksheetInstance
            {
                StudentId = studentId, WorksheetId = worksheet.Id, StartTime = (finishedAt ?? DateTime.UtcNow).AddMinutes(-30),
                EndTime = running ? null : finishedAt ?? DateTime.UtcNow,
                Status = running ? WorksheetInstanceStatus.Started : WorksheetInstanceStatus.Completed,
            });
            await db.SaveChangesAsync();
        }
        return exam.Id;
    });

    private static MultipartFormDataContent Upload(string csv, string exam = "lgs", int year = 2026)
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        return new MultipartFormDataContent
        {
            { new StringContent(exam), "Exam" },
            { new StringContent(year.ToString()), "Year" },
            { new StringContent("test source"), "Source" },
            { file, "File", "scores.csv" },
        };
    }

    [Fact]
    public async Task Admin_previews_then_confirms_and_only_confirm_writes()
    {
        var admin = await AdminAsync();
        const string csv = "Okul;İl;Taban Puan\nÖnizleme Lisesi;Ankara;450,5\nBozuk Lisesi;Ankara;yok\n";

        var preview = await admin.PostAsync($"{AdminApi}/import/preview", Upload(csv));
        preview.StatusCode.ShouldBe(HttpStatusCode.OK, await preview.Content.ReadAsStringAsync());
        var previewed = await preview.Content.ReadFromJsonAsync<TargetProgramImportPreviewDto>(Json);
        (previewed!.NewCount, previewed.InvalidCount).ShouldBe((1, 1));
        await WithDbAsync(async db => (await db.TargetPrograms.CountAsync()).ShouldBe(0));

        var confirm = await admin.PostAsync($"{AdminApi}/import/confirm", Upload(csv));
        confirm.StatusCode.ShouldBe(HttpStatusCode.OK, await confirm.Content.ReadAsStringAsync());
        (await confirm.Content.ReadFromJsonAsync<TargetProgramImportResultDto>(Json))!.Created.ShouldBe(1);

        var list = await admin.GetFromJsonAsync<TargetProgramPageDto<AdminTargetProgramDto>>($"{AdminApi}?exam=lgs&search=onizleme", Json);
        list!.Items.ShouldHaveSingleItem().MinimumScore.ShouldBe(450.5m);
    }

    [Fact]
    public async Task A_broken_file_gets_a_code_the_screen_can_translate()
    {
        var response = await (await AdminAsync()).PostAsync($"{AdminApi}/import/preview", Upload("Kurum;Puan\nA;1\n"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("CsvHeaderMissing");
    }

    [Theory]
    [InlineData("Student")]
    [InlineData("Teacher")]
    [InlineData("Parent")]
    public async Task Only_admin_reaches_the_admin_endpoints(string role)
    {
        var client = await ClientAsAsync(TeacherUser, role, $"kc-{TeacherUser}", role);

        (await client.GetAsync(AdminApi)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.PostAsync($"{AdminApi}/import/confirm", Upload("Okul;İl;Taban Puan\nA;B;450\n"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Student_chooses_replaces_and_clears_a_target()
    {
        await SeedStudentAsync();
        var first = await SeedProgramAsync();
        var second = await WithDbAsync(async db =>
        {
            var program = new TargetProgram { ScoreType = TargetScoreTypes.Lgs, Name = "İkinci", City = "Ankara", Year = 2026, MinimumScore = 440, SourceReference = "s", NormalizedKey = "ikinci||ankara|" };
            db.TargetPrograms.Add(program);
            await db.SaveChangesAsync();
            return program.Id;
        });
        var student = await StudentAsync();

        (await student.GetFromJsonAsync<TargetGoalDto>(StudentApi, Json))!.Target.ShouldBeNull();

        var catalog = await student.GetFromJsonAsync<TargetProgramPageDto<TargetProgramDto>>($"{StudentApi}/programs?exam=lgs&search=ornek", Json);
        catalog!.Items.ShouldHaveSingleItem().Id.ShouldBe(first);

        (await student.PutAsJsonAsync(StudentApi, new SetTargetGoalDto { TargetProgramId = 999999 })).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var chosen = await student.PutAsJsonAsync(StudentApi, new SetTargetGoalDto { TargetProgramId = first });
        chosen.StatusCode.ShouldBe(HttpStatusCode.OK, await chosen.Content.ReadAsStringAsync());
        (await student.PutAsJsonAsync(StudentApi, new SetTargetGoalDto { TargetProgramId = second })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var goal = await student.GetFromJsonAsync<TargetGoalDto>(StudentApi, Json);
        goal!.Target!.Id.ShouldBe(second);
        await WithDbAsync(async db => (await db.StudentTargetGoals.CountAsync()).ShouldBe(1));

        (await student.DeleteAsync(StudentApi)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await student.GetFromJsonAsync<TargetGoalDto>(StudentApi, Json))!.Target.ShouldBeNull();
    }

    [Fact]
    public async Task The_goal_carries_only_the_latest_exam_with_every_session_finished()
    {
        var studentId = await SeedStudentAsync();
        var program = await SeedProgramAsync();
        await SeedExamAsync(studentId, "Eski", DateTime.UtcNow.AddDays(-10));
        var latest = await SeedExamAsync(studentId, "Son", DateTime.UtcNow.AddDays(-1));
        await SeedExamAsync(studentId, "Süren", null);
        var student = await StudentAsync();

        (await student.PutAsJsonAsync(StudentApi, new SetTargetGoalDto { TargetProgramId = program })).EnsureSuccessStatusCode();
        var goal = await student.GetFromJsonAsync<TargetGoalDto>(StudentApi, Json);

        var result = goal!.Results.ShouldHaveSingleItem();
        (result.Id, result.TemplateCode, result.Complete, result.WrongAnswersPerCorrect).ShouldBe((latest, "lgs", true, 3));
        result.Sections.Select(x => x.Name).ShouldBe(["Türkçe", "Matematik"]);
    }

    [Fact]
    public async Task No_results_before_a_finished_exam()
    {
        var studentId = await SeedStudentAsync();
        var program = await SeedProgramAsync();
        await SeedExamAsync(studentId, "Süren", null);
        var student = await StudentAsync();

        (await student.PutAsJsonAsync(StudentApi, new SetTargetGoalDto { TargetProgramId = program })).EnsureSuccessStatusCode();

        (await student.GetFromJsonAsync<TargetGoalDto>(StudentApi, Json))!.Results.ShouldBeEmpty();
    }

    [Fact]
    public async Task Parent_reads_a_linked_child_without_the_source_and_with_an_audit_row()
    {
        var studentId = await SeedStudentAsync();
        var program = await SeedProgramAsync();
        await WithDbAsync(async db =>
        {
            db.StudentTargetGoals.Add(new StudentTargetGoal { StudentId = studentId, TargetProgramId = program, SelectedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });
        await SeedParentLinkAsync(ParentUser, StudentUser);
        var url = $"/api/parent/children/{studentId}/target-goal";

        var parent = await ClientAsAsync(ParentUser, "Parent", $"kc-{ParentUser}", "Parent");
        var response = await parent.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("http");
        (await response.Content.ReadFromJsonAsync<TargetGoalDto>(Json))!.Target!.MinimumScore.ShouldBe(450m);
        await WithDbAsync(async db =>
            (await db.ParentAccessAudits.AsNoTracking().CountAsync(a => a.Endpoint == ParentAccessEndpoints.ChildTargetGoal)).ShouldBe(1));

        var other = await ClientAsAsync(OtherParentUser, "Parent", $"kc-{OtherParentUser}", "Parent");
        (await other.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Switching_the_feature_off_closes_every_endpoint_and_status_says_so()
    {
        await SeedStudentAsync();
        var options = Factory.Services.GetRequiredService<IOptionsMonitor<TargetGoalsOptions>>();
        var student = await StudentAsync();
        (await student.GetFromJsonAsync<TargetGoalsStatusDto>("/api/target-goals/status", Json))!.Enabled.ShouldBeTrue();

        options.CurrentValue.Enabled = false;
        try
        {
            (await student.GetFromJsonAsync<TargetGoalsStatusDto>("/api/target-goals/status", Json))!.Enabled.ShouldBeFalse();
            (await student.GetAsync(StudentApi)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await (await AdminAsync()).GetAsync(AdminApi)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            // Yetkisiz rol kapalı özelliği öğrenemez: yetki önce gelir.
            (await student.GetAsync(AdminApi)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
        finally
        {
            options.CurrentValue.Enabled = true;
        }
    }
}
