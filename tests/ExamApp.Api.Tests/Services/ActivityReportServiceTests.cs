using Microsoft.EntityFrameworkCore;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #584: öğretmen etkinlik analizi — kapsam takip ekranıyla aynı (kendi ataması, okul), analize yalnız tamamlanmış ve
/// yeniden çözülmeyen oturumlar girer; öğe başına doğru oranı ve en sık yanlış cevap, MCQ şık etiketi, açık uçlu madde
/// puanlanamaz, öğrenci × madde hücreleri. Etkinlik olmayan / atamasız sayfa null.
/// </summary>
public class ActivityReportServiceTests : IDisposable
{
    private const int TeacherUserId = 584;

    private const string PlacePlan =
        """{"version":1,"type":"dragPlace","mode":"place","targets":[{"id":"t1","label":"İskelet","capacity":2},{"id":"t2","label":"Kaslar","capacity":2}],"cards":[{"id":"c1","label":"Kemik"},{"id":"c2","label":"Kas lifi"},{"id":"c3","label":"Çeldirici"}]}""";

    private const string PlaceSolution = """{"t1":["c1"],"t2":["c2"]}""";

    private readonly TestDb _db = TestDb.Create();
    private int _schoolId, _worksheetId, _dragId, _mcqId, _openId, _answerA, _answerB;
    private readonly Dictionary<string, int> _instances = new();

    public void Dispose() => _db.Dispose();

    private static string Place(params (string T, string C)[] placements)
        => System.Text.Json.JsonSerializer.Serialize(new
        {
            version = 1, type = "dragPlace", placements = placements.Select(p => new { targetId = p.T, cardId = p.C })
        });

    private async Task SeedAsync(WorksheetKind kind = WorksheetKind.Activity)
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5. Sınıf" };
        var school = new School { Name = "Okul" };
        ctx.AddRange(grade, school);
        await ctx.SaveChangesAsync();
        _schoolId = school.Id;

        ctx.SetCurrentUser(TeacherUserId);
        var ws = new Worksheet { Name = "Vücudumuz", Description = "", GradeId = grade.Id, Kind = kind };
        ctx.Worksheets.Add(ws);
        await ctx.SaveChangesAsync();
        _worksheetId = ws.Id;

        var drag = new Question { Text = "Yerleştir", Point = 0, InteractionType = "dragPlace", InteractionPlan = PlacePlan, InteractionSolution = PlaceSolution };
        var mcq = new Question { Text = "Seç", Point = 0 };
        var open = new Question { Text = "Yaz", Point = 0, InteractionType = "openText", InteractionPlan = """{"version":1,"type":"openText"}""" };
        ctx.AddRange(drag, mcq, open);
        await ctx.SaveChangesAsync();
        var a = new Answer { QuestionId = mcq.Id, Text = "Kalp", Tag = "A", Order = 0 };
        var b = new Answer { QuestionId = mcq.Id, Text = "Akciğer", Tag = "B", Order = 1 };
        ctx.AddRange(a, b);
        await ctx.SaveChangesAsync();
        mcq.CorrectAnswerId = a.Id;
        (_answerA, _answerB) = (a.Id, b.Id);

        var wqDrag = new WorksheetQuestion { TestId = ws.Id, QuestionId = drag.Id, Order = 1 };
        var wqMcq = new WorksheetQuestion { TestId = ws.Id, QuestionId = mcq.Id, Order = 2 };
        var wqOpen = new WorksheetQuestion { TestId = ws.Id, QuestionId = open.Id, Order = 3 };
        ctx.AddRange(wqDrag, wqMcq, wqOpen);
        await ctx.SaveChangesAsync();
        (_dragId, _mcqId, _openId) = (wqDrag.Id, wqMcq.Id, wqOpen.Id);

        var students = new[] { "s1", "s2", "s3", "s4" }
            .Select((n, i) => new Student
            {
                UserId = 9000 + i, StudentNumber = n, SchoolName = "s", SchoolId = school.Id, SchoolVerifiedAt = DateTime.UtcNow, GradeId = grade.Id
            })
            .ToList();
        ctx.AddRange(students);
        await ctx.SaveChangesAsync();

        ctx.WorksheetAssignments.Add(new WorksheetAssignment { WorksheetId = ws.Id, GradeId = grade.Id, StartAt = DateTime.UtcNow.AddDays(-1) });
        await ctx.SaveChangesAsync();

        var now = DateTime.UtcNow;
        WorksheetInstance Instance(Student s, WorksheetInstanceStatus status, bool completedBefore) => new()
        {
            StudentId = s.Id, WorksheetId = ws.Id, Status = status, StartTime = now.AddHours(-2),
            EndTime = status == WorksheetInstanceStatus.Completed ? now.AddHours(-1) : null,
            FirstCompletedAt = completedBefore ? now.AddHours(-1) : null
        };
        var i1 = Instance(students[0], WorksheetInstanceStatus.Completed, true);
        var i2 = Instance(students[1], WorksheetInstanceStatus.Completed, true);
        var i3 = Instance(students[2], WorksheetInstanceStatus.Started, true); // yeniden çözüyor
        ctx.TestInstances.AddRange(i1, i2, i3);
        await ctx.SaveChangesAsync();
        (_instances["s1"], _instances["s2"], _instances["s3"]) = (i1.Id, i2.Id, i3.Id);

        ctx.TestInstanceQuestions.AddRange(
            // s1: hepsi doğru
            new WorksheetInstanceQuestion { WorksheetInstanceId = i1.Id, WorksheetQuestionId = wqDrag.Id, AnswerPayload = Place(("t1", "c1"), ("t2", "c2")) },
            new WorksheetInstanceQuestion { WorksheetInstanceId = i1.Id, WorksheetQuestionId = wqMcq.Id, SelectedAnswerId = a.Id },
            new WorksheetInstanceQuestion { WorksheetInstanceId = i1.Id, WorksheetQuestionId = wqOpen.Id, AnswerPayload = """{"version":1,"type":"openText","text":"Kalp atar."}""" },
            // s2: kemiği kaslara, çeldiriciyi iskelete; MCQ B; açık uçlu boş
            new WorksheetInstanceQuestion { WorksheetInstanceId = i2.Id, WorksheetQuestionId = wqDrag.Id, AnswerPayload = Place(("t2", "c1"), ("t1", "c3")) },
            new WorksheetInstanceQuestion { WorksheetInstanceId = i2.Id, WorksheetQuestionId = wqMcq.Id, SelectedAnswerId = b.Id },
            new WorksheetInstanceQuestion { WorksheetInstanceId = i2.Id, WorksheetQuestionId = wqOpen.Id },
            // s3: yeniden çözüyor — cevabı analize girmemeli
            new WorksheetInstanceQuestion { WorksheetInstanceId = i3.Id, WorksheetQuestionId = wqMcq.Id, SelectedAnswerId = b.Id });
        await ctx.SaveChangesAsync();
    }

    private async Task<ExamApp.Api.Models.Dtos.ActivityReportDto?> ReportAsync(int teacherUserId = TeacherUserId)
    {
        await using var ctx = _db.NewContext();
        var service = new ActivityReportService(ctx, new WorksheetAssignmentService(ctx, new SchoolAccessPolicy(ctx)),
            InteractionGraderRegistry.Default);
        return await service.GetReportAsync(_worksheetId, SchoolScope.For(teacherUserId, _schoolId));
    }

    [Fact]
    public async Task Counts_only_completed_not_retaking_sessions()
    {
        await SeedAsync();

        var report = (await ReportAsync()).ShouldNotBeNull();

        report.WorksheetName.ShouldBe("Vücudumuz");
        report.StudentCount.ShouldBe(4);
        report.AnalyzedCount.ShouldBe(2);
        report.RetakingCount.ShouldBe(1);
        report.Students.Select(s => s.StudentNumber).ShouldBe(new[] { "s1", "s2" });
    }

    [Fact]
    public async Task DragPlace_items_show_rate_and_most_common_wrong_target()
    {
        await SeedAsync();

        var drag = (await ReportAsync()).ShouldNotBeNull().Questions[0];

        drag.WorksheetQuestionId.ShouldBe(_dragId);
        drag.Number.ShouldBe(1);
        drag.Gradable.ShouldBeTrue();
        drag.FullyCorrectCount.ShouldBe(1);
        drag.AnsweredCount.ShouldBe(2);
        drag.Items.Select(i => i.Label).ShouldBe(new[] { "Kemik", "Kas lifi", "Çeldirici" });

        var bone = drag.Items[0];
        bone.CorrectCount.ShouldBe(1);
        bone.StudentCount.ShouldBe(2);
        bone.WrongAnswers.Single().Label.ShouldBe("Kaslar");
        bone.WrongAnswers.Single().Count.ShouldBe(1);

        var fibre = drag.Items[1];
        fibre.CorrectCount.ShouldBe(1);
        fibre.EmptyCount.ShouldBe(1);
        fibre.WrongAnswers.ShouldBeEmpty();

        var distractor = drag.Items[2];
        distractor.Distractor.ShouldBeTrue();
        distractor.CorrectCount.ShouldBe(1);
        distractor.WrongAnswers.Single().Label.ShouldBe("İskelet");
    }

    [Fact]
    public async Task Mcq_wrong_answer_uses_option_tag_and_open_text_is_not_gradable()
    {
        await SeedAsync();

        var report = (await ReportAsync()).ShouldNotBeNull();
        var mcq = report.Questions[1];
        var open = report.Questions[2];

        mcq.WorksheetQuestionId.ShouldBe(_mcqId);
        mcq.FullyCorrectCount.ShouldBe(1);
        mcq.Items.Single().WrongAnswers.Single().Label.ShouldBe("B"); // s3'ün (yeniden çözüyor) B'si sayılmaz

        open.WorksheetQuestionId.ShouldBe(_openId);
        open.InteractionType.ShouldBe("openText");
        open.Gradable.ShouldBeFalse();
        open.Items.ShouldBeEmpty();
        open.AnsweredCount.ShouldBe(1);
    }

    [Fact]
    public async Task Heatmap_cells_follow_question_order()
    {
        await SeedAsync();

        var report = (await ReportAsync()).ShouldNotBeNull();
        var s1 = report.Students[0].Cells;
        var s2 = report.Students[1].Cells;

        s1.Select(c => (c.Answered, c.CorrectCount, c.TotalCount)).ShouldBe(new[] { (true, 2, 2), (true, 1, 1), (true, 0, 0) });
        s2.Select(c => (c.Answered, c.CorrectCount, c.TotalCount)).ShouldBe(new[] { (true, 0, 2), (true, 0, 1), (false, 0, 0) });
    }

    [Fact]
    public async Task Returns_null_for_another_teacher_or_a_test_worksheet()
    {
        await SeedAsync();
        (await ReportAsync(teacherUserId: 999)).ShouldBeNull();

        using var other = new ActivityReportServiceTests();
        await other.SeedAsync(WorksheetKind.Test);
        (await other.ReportAsync()).ShouldBeNull();
    }

    // issue #684: sayfadan kaldırılan madde, onu çözmüş oturum analizdeyse analizde kalır; kimse çözmediyse düşer.
    [Fact]
    public async Task Removed_item_stays_in_report_for_sessions_that_solved_it_issue684()
    {
        await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            var wq = await ctx.TestQuestions.SingleAsync(x => x.Id == _mcqId);
            wq.IsDeleted = true;
            await ctx.SaveChangesAsync();
        }

        var report = (await ReportAsync()).ShouldNotBeNull();
        report.Questions.Select(q => q.WorksheetQuestionId).ShouldBe(new[] { _dragId, _mcqId, _openId });
        report.Questions[1].FullyCorrectCount.ShouldBe(1);
    }

    [Fact]
    public async Task Removed_item_nobody_solved_is_not_in_report_issue684()
    {
        await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(TeacherUserId);
            var extra = new Question { Text = "Sonradan", Point = 0 };
            ctx.Questions.Add(extra);
            await ctx.SaveChangesAsync();
            ctx.TestQuestions.Add(new WorksheetQuestion { TestId = _worksheetId, QuestionId = extra.Id, Order = 4, IsDeleted = true });
            await ctx.SaveChangesAsync();
        }

        (await ReportAsync()).ShouldNotBeNull().Questions.Select(q => q.WorksheetQuestionId).ShouldBe(new[] { _dragId, _mcqId, _openId });
    }

}
