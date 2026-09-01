using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ExamApp.Api.Tests.Services;

public class QuestionServiceResizeImageTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();
    private QuestionService NewService(AppDbContext ctx) => new(ctx, new ImageHelper(), _minio);

    private static Stream Png(int w, int h)
    {
        var ms = new MemoryStream();
        using var img = new Image<Rgba32>(w, h);
        img.SaveAsPng(ms);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public async Task Reports_each_missing_precondition_in_turn()
    {
        await using var ctx = _db.NewContext();
        var svc = NewService(ctx);

        (await svc.ResizeQuestionImage(404, 2)).Message.ShouldContain("Soru bulunamadı");

        ctx.Questions.Add(new Question { Text = "q" });
        await ctx.SaveChangesAsync();
        var q = await ctx.Questions.SingleAsync();

        (await svc.ResizeQuestionImage(q.Id, 2)).Message.ShouldContain("Soru resmi bulunamadı");

        q.ImageUrl = "questions/x.jpg";
        await ctx.SaveChangesAsync();
        _minio.GetFileStreamAsync(q.ImageUrl).Returns((Stream?)null);
        (await svc.ResizeQuestionImage(q.Id, 2)).Message.ShouldContain("indirilemedi");
    }

    [Fact]
    public async Task Scales_the_image_and_the_stored_geometry_on_success()
    {
        int qId;
        await using (var ctx = _db.NewContext())
        {
            var seeded = new Question
            {
                Text = "q", ImageUrl = "questions/x.jpg",
                X = 10, Y = 20, Width = 100, Height = 200, SanitizedHeight = 150,
            };
            ctx.Questions.Add(seeded);
            await ctx.SaveChangesAsync();
            qId = seeded.Id;
            ctx.Answers.Add(new Answer { QuestionId = seeded.Id, Text = "a", X = 5, Y = 5, Width = 30, Height = 40 });
            await ctx.SaveChangesAsync();
        }

        _minio.GetFileStreamAsync("questions/x.jpg").Returns(_ => Png(100, 200));
        _minio.UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>()).Returns("questions/x-scaled.jpg");

        await using (var ctx = _db.NewContext())
        {
            var result = await NewService(ctx).ResizeQuestionImage(qId, 0.5);
            result.Success.ShouldBeTrue();
        }

        await using var check = _db.NewContext();
        var q = await check.Questions.SingleAsync(x => x.Id == qId);
        q.ImageUrl.ShouldBe("questions/x-scaled.jpg");
        q.Width.ShouldBe(50);
        q.Height.ShouldBe(100);
        q.SanitizedHeight.ShouldBe(75);
        q.X.ShouldBe(5);
    }

    public void Dispose() => _db.Dispose();
}
