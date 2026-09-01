using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

public class ExamServiceTestFlowTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();

    private ExamService NewService(AppDbContext ctx) => new(ctx, new ImageHelper(), _minio);

    private sealed record World(int StudentId, int StudentUserId, int WorksheetId, int Q1, int Q2, int CorrectA1, int WrongA1);

    /// <summary>Student + a 2-question worksheet. Q1 has answers (A1 correct), Q2 has none.</summary>
    private async Task<World> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();

        var student = new Student { UserId = 300, StudentNumber = "S", SchoolName = "Sch", GradeId = grade.Id };
        var worksheet = new Worksheet { Name = "Test", Description = "", GradeId = grade.Id, MaxDurationSeconds = 600 };
        var q1 = new Question { Text = "Q1", Point = 10 };
        var q2 = new Question { Text = "Q2", Point = 10 };
        ctx.AddRange(student, worksheet, q1, q2);
        await ctx.SaveChangesAsync();

        var a1 = new Answer { QuestionId = q1.Id, Text = "correct", Tag = "A", Order = 0 };
        var a2 = new Answer { QuestionId = q1.Id, Text = "wrong", Tag = "B", Order = 1 };
        ctx.Answers.AddRange(a1, a2);
        await ctx.SaveChangesAsync();
        q1.CorrectAnswerId = a1.Id;

        ctx.TestQuestions.AddRange(
            new WorksheetQuestion { TestId = worksheet.Id, QuestionId = q1.Id, Order = 1 },
            new WorksheetQuestion { TestId = worksheet.Id, QuestionId = q2.Id, Order = 2 });
        await ctx.SaveChangesAsync();

        return new World(student.Id, student.UserId, worksheet.Id, q1.Id, q2.Id, a1.Id, a2.Id);
    }

    private static StudentProfileDto Student(World w) => new() { Id = w.StudentId };

    // ---- StartTestAsync ----

    [Fact]
    public async Task StartTest_creates_an_instance_with_a_row_per_worksheet_question()
    {
        var w = await SeedAsync();
        int instanceId;
        await using (var ctx = _db.NewContext())
        {
            var result = await NewService(ctx).StartTestAsync(w.WorksheetId, Student(w));
            result.Success.ShouldBeTrue();
            instanceId = result.InstanceId;
        }

        await using var check = _db.NewContext();
        var inst = await check.TestInstances.Include(i => i.WorksheetInstanceQuestions)
            .FirstAsync(i => i.Id == instanceId);
        inst.Status.ShouldBe(WorksheetInstanceStatus.Started);
        inst.WorksheetInstanceQuestions.Count.ShouldBe(2);
    }

    [Fact]
    public async Task StartTest_returns_the_open_instance_instead_of_creating_a_second()
    {
        var w = await SeedAsync();
        int first;
        await using (var ctx = _db.NewContext())
            first = (await NewService(ctx).StartTestAsync(w.WorksheetId, Student(w))).InstanceId;

        await using (var ctx = _db.NewContext())
        {
            var again = await NewService(ctx).StartTestAsync(w.WorksheetId, Student(w));
            again.Success.ShouldBeTrue();
            again.InstanceId.ShouldBe(first);
        }

        (await _db.NewContext().TestInstances.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task StartTest_refuses_when_the_test_is_already_completed()
    {
        var w = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            ctx.TestInstances.Add(new WorksheetInstance
            {
                StudentId = w.StudentId, WorksheetId = w.WorksheetId,
                Status = WorksheetInstanceStatus.Completed, StartTime = DateTime.UtcNow.AddHours(-1),
            });
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = _db.NewContext();
        var result = await NewService(ctx2).StartTestAsync(w.WorksheetId, Student(w));
        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("tamamlan");
    }

    // ---- GetTestInstanceQuestionsAsync ----

    [Fact]
    public async Task GetTestInstanceQuestions_returns_null_for_another_user()
    {
        var w = await SeedAsync();
        int instanceId;
        await using (var ctx = _db.NewContext())
            instanceId = (await NewService(ctx).StartTestAsync(w.WorksheetId, Student(w))).InstanceId;

        await using var ctx2 = _db.NewContext();
        (await NewService(ctx2).GetTestInstanceQuestionsAsync(instanceId, userId: 999)).ShouldBeNull();
    }

    [Fact]
    public async Task GetTestInstanceQuestions_projects_questions_answers_and_order()
    {
        var w = await SeedAsync();
        int instanceId;
        await using (var ctx = _db.NewContext())
            instanceId = (await NewService(ctx).StartTestAsync(w.WorksheetId, Student(w))).InstanceId;

        await using var ctx2 = _db.NewContext();
        var dto = await NewService(ctx2).GetTestInstanceQuestionsAsync(instanceId, w.StudentUserId);

        dto.ShouldNotBeNull();
        dto!.TestName.ShouldBe("Test");
        dto.MaxDurationSeconds.ShouldBe(600);
        dto.TestInstanceQuestions.Count.ShouldBe(2);
        var first = dto.TestInstanceQuestions.OrderBy(x => x.Order).First();
        first.Order.ShouldBe(1);
        first.Question.Answers.Count.ShouldBe(2);
    }

    // ---- GetCompletedTestsAsync ----

    [Fact]
    public async Task GetCompletedTests_only_lists_completed_and_computes_the_score()
    {
        var w = await SeedAsync();

        await using (var ctx = _db.NewContext())
        {
            // a started (not completed) instance — must be excluded
            ctx.TestInstances.Add(new WorksheetInstance
            {
                StudentId = w.StudentId, WorksheetId = w.WorksheetId,
                Status = WorksheetInstanceStatus.Started, StartTime = DateTime.UtcNow,
            });

            // a completed instance: Q1 answered correctly, Q2 answered (wrong — no correct answer defined)
            var wqs = await ctx.TestQuestions.Where(t => t.TestId == w.WorksheetId).OrderBy(t => t.Order).ToListAsync();
            var done = new WorksheetInstance
            {
                StudentId = w.StudentId, WorksheetId = w.WorksheetId,
                Status = WorksheetInstanceStatus.Completed,
                StartTime = DateTime.UtcNow.AddMinutes(-20), EndTime = DateTime.UtcNow,
                WorksheetInstanceQuestions = new List<WorksheetInstanceQuestion>
                {
                    new() { WorksheetQuestionId = wqs[0].Id, SelectedAnswerId = w.CorrectA1 },
                    new() { WorksheetQuestionId = wqs[1].Id, SelectedAnswerId = w.WrongA1 },
                },
            };
            ctx.TestInstances.Add(done);
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var page = await NewService(read).GetCompletedTestsAsync(Student(w), pageNumber: 1, pageSize: 10);

        page.TotalCount.ShouldBe(1);
        var item = page.Items.ShouldHaveSingleItem();
        item.TotalQuestions.ShouldBe(2);
        item.CorrectAnswers.ShouldBe(1);
        item.WrongAnswers.ShouldBe(1);
        item.Score.ShouldBe(50);
    }

    public void Dispose() => _db.Dispose();
}
