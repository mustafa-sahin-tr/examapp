using System.Text;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using Microsoft.AspNetCore.Http;

namespace ExamApp.Api.Tests.Services;

public class ExamServiceCanvasAndImageTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();
    private ExamService NewService(AppDbContext ctx) => new(ctx, new ImageHelper(), _minio);

    private static IFormFile FakeImage(string contentType = "image/png", int length = 8)
    {
        var bytes = Encoding.UTF8.GetBytes(new string('x', length));
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "bg.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }

    // ---- UpdateWorksheetBackgroundImageAsync ----

    [Fact]
    public async Task Background_image_rejects_an_empty_file()
    {
        await using var ctx = _db.NewContext();
        var empty = new FormFile(new MemoryStream(), 0, 0, "file", "x") { Headers = new HeaderDictionary() };
        (await NewService(ctx).UpdateWorksheetBackgroundImageAsync(1, empty, 1)).Success.ShouldBeFalse();
    }

    [Fact]
    public async Task Background_image_rejects_a_non_image_content_type()
    {
        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).UpdateWorksheetBackgroundImageAsync(1, FakeImage("application/pdf"), 1);
        r.Success.ShouldBeFalse();
        r.Message.ShouldContain("görsel");
    }

    [Fact]
    public async Task Background_image_fails_for_an_unknown_worksheet()
    {
        await using var ctx = _db.NewContext();
        (await NewService(ctx).UpdateWorksheetBackgroundImageAsync(9999, FakeImage(), 1)).Success.ShouldBeFalse();
    }

    [Fact]
    public async Task Background_image_uploads_saves_the_url_and_removes_the_previous_one()
    {
        _minio.UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns("http://minio/new.png");

        int wsId;
        await using (var ctx = _db.NewContext())
        {
            var g = new Grade { Name = "5" };
            ctx.Grades.Add(g);
            await ctx.SaveChangesAsync();
            var ws = new Worksheet { Name = "W", Description = "", GradeId = g.Id, ImageUrl = "http://minio/old.png" };
            ctx.Worksheets.Add(ws);
            await ctx.SaveChangesAsync();
            wsId = ws.Id;
        }

        await using (var ctx = _db.NewContext())
        {
            var r = await NewService(ctx).UpdateWorksheetBackgroundImageAsync(wsId, FakeImage(), 7);
            r.Success.ShouldBeTrue();
            r.ImageUrl.ShouldBe("http://minio/new.png");
        }

        await _minio.Received().DeleteFileByUrlAsync("http://minio/old.png");
        (await _db.NewContext().Worksheets.FindAsync(wsId))!.ImageUrl.ShouldBe("http://minio/new.png");
    }

    // ---- GetAllCanvasQuestions ----

    [Fact]
    public async Task Canvas_questions_returns_only_canvas_questions_after_maxId()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.Questions.Add(new Question { Text = "normal", IsCanvasQuestion = false });
            ctx.Questions.Add(new Question { Text = "c1", IsCanvasQuestion = true });
            ctx.Questions.Add(new Question { Text = "c2", IsCanvasQuestion = true });
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = _db.NewContext();
        var all = await NewService(ctx2).GetAllCanvasQuestions();
        all.Count.ShouldBe(2);

        var afterFirst = await NewService(ctx2).GetAllCanvasQuestions(maxId: all.Min(q => q.Question.Id));
        afterFirst.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Canvas_questions_includes_answers_only_when_requested()
    {
        await using (var ctx = _db.NewContext())
        {
            var q = new Question { Text = "c", IsCanvasQuestion = true };
            ctx.Questions.Add(q);
            await ctx.SaveChangesAsync();
            ctx.Answers.Add(new Answer { QuestionId = q.Id, Text = "a" });
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = _db.NewContext();
        (await NewService(ctx2).GetAllCanvasQuestions(includeAnswers: false))[0].Question.Answers.ShouldBeEmpty();
        (await NewService(ctx2).GetAllCanvasQuestions(includeAnswers: true))[0].Question.Answers.Count.ShouldBe(1);
    }

    // ---- GetCanvasTestResultAsync ----

    [Fact]
    public async Task Canvas_result_returns_null_for_a_missing_or_foreign_instance()
    {
        await using var ctx = _db.NewContext();
        (await NewService(ctx).GetCanvasTestResultAsync(404, userId: 1)).ShouldBeNull();
    }

    [Fact]
    public async Task Canvas_result_hides_correct_answers_unless_completed_and_requested()
    {
        int instanceId, correctAnswerId;
        await using (var ctx = _db.NewContext())
        {
            var g = new Grade { Name = "5" };
            var student = new Student { UserId = 77, StudentNumber = "n", SchoolName = "s" };
            ctx.AddRange(g, student);
            await ctx.SaveChangesAsync();
            var ws = new Worksheet { Name = "W", Description = "", GradeId = g.Id };
            var q = new Question { Text = "q" };
            ctx.AddRange(ws, q);
            await ctx.SaveChangesAsync();
            var a = new Answer { QuestionId = q.Id, Text = "correct" };
            ctx.Answers.Add(a);
            await ctx.SaveChangesAsync();
            q.CorrectAnswerId = a.Id;
            correctAnswerId = a.Id;
            var wq = new WorksheetQuestion { TestId = ws.Id, QuestionId = q.Id, Order = 1 };
            ctx.TestQuestions.Add(wq);
            await ctx.SaveChangesAsync();
            var inst = new WorksheetInstance
            {
                StudentId = student.Id, WorksheetId = ws.Id, Status = WorksheetInstanceStatus.Started,
                StartTime = DateTime.UtcNow,
                WorksheetInstanceQuestions = new List<WorksheetInstanceQuestion>
                {
                    new() { WorksheetQuestionId = wq.Id },
                },
            };
            ctx.TestInstances.Add(inst);
            await ctx.SaveChangesAsync();
            instanceId = inst.Id;
        }

        await using var ctx2 = _db.NewContext();
        var svc = NewService(ctx2);

        // not requesting correct answer -> ok, hidden
        var plain = await svc.GetCanvasTestResultAsync(instanceId, 77, includeCorrectAnswer: false);
        plain.ShouldNotBeNull();
        plain!.TestInstanceQuestions[0].Question.CorrectAnswerId.ShouldBeNull();

        // requesting correct answer on a NON-completed instance -> null
        (await svc.GetCanvasTestResultAsync(instanceId, 77, includeCorrectAnswer: true)).ShouldBeNull();
    }

    public void Dispose() => _db.Dispose();
}
