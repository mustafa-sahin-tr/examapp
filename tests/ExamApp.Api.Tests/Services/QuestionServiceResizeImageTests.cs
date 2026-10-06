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

        q.ImageUrl = "/img/exam-questions/questions/7/x.jpg";
        await ctx.SaveChangesAsync();
        _minio.GetFileStreamAsync(q.ImageUrl).Returns((Stream?)null);
        (await svc.ResizeQuestionImage(q.Id, 2)).Message.ShouldContain("indirilemedi");
    }

    private const string StoredUrl = "/img/exam-questions/questions/7/x.jpg";

    private async Task<int> SeedQuestionAsync(string imageUrl)
    {
        await using var ctx = _db.NewContext();
        var seeded = new Question
        {
            Text = "q", ImageUrl = imageUrl,
            X = 10, Y = 20, Width = 100, Height = 200, SanitizedHeight = 150,
        };
        ctx.Questions.Add(seeded);
        await ctx.SaveChangesAsync();
        ctx.Answers.Add(new Answer { QuestionId = seeded.Id, Text = "a", X = 5, Y = 5, Width = 30, Height = 40 });
        await ctx.SaveChangesAsync();
        return seeded.Id;
    }

    [Fact]
    public async Task Scales_the_image_and_the_stored_geometry_on_success()
    {
        var qId = await SeedQuestionAsync(StoredUrl);
        _minio.GetFileStreamAsync(StoredUrl).Returns(_ => Png(100, 200));
        _minio.UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(ci => $"/img/{ci.ArgAt<string>(2)}/{ci.ArgAt<string>(1)}");

        await using (var ctx = _db.NewContext())
        {
            var result = await NewService(ctx).ResizeQuestionImage(qId, 0.5);
            result.Success.ShouldBeTrue();
        }

        await using var check = _db.NewContext();
        var q = await check.Questions.SingleAsync(x => x.Id == qId);
        q.ImageUrl.ShouldStartWith("/img/exam-questions/questions/7/");
        q.ImageUrl.ShouldNotBe(StoredUrl);
        q.Width.ShouldBe(50);
        q.Height.ShouldBe(100);
        q.SanitizedHeight.ShouldBe(75);
        q.X.ShouldBe(5);
    }

    /// <summary>
    /// issue #365: eskiden saklanan URL'nin kendisi object key olarak veriliyordu → key `/img/exam-questions/questions/...`
    /// (anonim okunabilir questions/* prefix'i dışı) ve DB'de çift `/img/.../img/...` URL.
    /// </summary>
    [Fact]
    public async Task Resized_image_is_uploaded_under_questions_prefix_of_the_same_bucket_and_url_is_not_doubled()
    {
        var qId = await SeedQuestionAsync(StoredUrl);
        _minio.GetFileStreamAsync(StoredUrl).Returns(_ => Png(100, 200));
        string? key = null, bucket = null;
        _minio.UploadFileAsync(Arg.Any<Stream>(), Arg.Do<string>(k => key = k), Arg.Do<string?>(b => bucket = b), Arg.Any<string?>())
            .Returns(ci => $"/img/{ci.ArgAt<string>(2)}/{ci.ArgAt<string>(1)}");

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).ResizeQuestionImage(qId, 0.5)).Success.ShouldBeTrue();

        bucket.ShouldBe("exam-questions");
        key.ShouldNotBeNull();
        key.ShouldStartWith("questions/7/");
        key.ShouldEndWith(".jpg");
        key.ShouldNotContain("img/");

        await using var check = _db.NewContext();
        var url = (await check.Questions.SingleAsync(x => x.Id == qId)).ImageUrl!;
        url.ShouldBe($"/img/exam-questions/{key}");
        url.IndexOf("/img/", 1, StringComparison.Ordinal).ShouldBe(-1);
    }

    [Theory]
    [InlineData("questions/x.jpg")]                              // /img/{bucket}/ biçimi değil
    [InlineData("/img/exam-questions/question-transfer/a.zip")]  // questions/ dışı
    [InlineData("/img/exam-questions/questions/../answers/a.jpg")]
    public async Task Non_question_image_urls_are_not_rewritten(string storedUrl)
    {
        var qId = await SeedQuestionAsync(storedUrl);
        _minio.GetFileStreamAsync(storedUrl).Returns(_ => Png(100, 200));

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).ResizeQuestionImage(qId, 0.5)).Success.ShouldBeFalse();

        await _minio.DidNotReceiveWithAnyArgs().UploadFileAsync(default!, default!, default, default);
        await using var check = _db.NewContext();
        (await check.Questions.SingleAsync(x => x.Id == qId)).ImageUrl.ShouldBe(storedUrl);
    }

    public void Dispose() => _db.Dispose();
}
