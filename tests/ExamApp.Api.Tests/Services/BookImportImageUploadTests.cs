using System.Collections.Concurrent;
using ExamApp.Api.Data;
using ExamApp.Api.Services.BookImport;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Labeling;
using ExamApp.Api.Tests.Support;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SkiaSharp;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #1144: kitap sayfaları görsel olarak (klasörden) — oturum görsel sayısıyla açılır, görsel başına içerik / boyut / genişlik
/// denetimi, eksik görselle tamamlama reddi, iş yükleme önekini kaynak alır; Sayfalar aşaması görseli sayfa biçimine çevirir.
/// </summary>
public class BookImportImageUploadTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();
    private readonly ConcurrentDictionary<string, byte[]> _objects = new(StringComparer.Ordinal);
    private readonly IBackgroundJobClient _jobs = Substitute.For<IBackgroundJobClient>();
    private readonly IEntitlementService _entitlements = Substitute.For<IEntitlementService>();
    private readonly int _gradeId;

    public BookImportImageUploadTests()
    {
        _minio.UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci =>
            {
                using var copy = new MemoryStream();
                ci.ArgAt<Stream>(0).CopyTo(copy);
                var url = "/img/exam-questions/" + ci.ArgAt<string>(1);
                _objects[url] = copy.ToArray();
                return Task.FromResult(url);
            });
        _minio.FileExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(_objects.ContainsKey(ci.ArgAt<string>(0))));
        _minio.DeleteFileByUrlAsync(Arg.Any<string>()).Returns(ci => Task.FromResult(_objects.TryRemove(ci.ArgAt<string>(0), out _)));

        _entitlements.CheckAsync(Arg.Any<EntitlementSubject>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new EntitlementCheck(true, null, 0, null));

        using var ctx = _db.NewContext();
        var grade = new Grade { Name = "8. Sınıf" };
        ctx.Grades.Add(grade);
        ctx.SaveChanges();
        _gradeId = grade.Id;
    }

    public void Dispose() => _db.Dispose();

    private static byte[] Image(int width, int height, SKEncodedImageFormat format = SKEncodedImageFormat.Webp)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.White);
        using var data = bitmap.Encode(format, 80);
        return data.ToArray();
    }

    private async Task<T> As<T>(Func<BookImportService, Task<T>> action, int maxPages = 600)
    {
        await using var ctx = _db.NewContext();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MinioConfig:BucketName"] = "exam-questions", ["BookImport:MaxPages"] = maxPages.ToString(),
            })
            .Build();
        return await action(new BookImportService(ctx, _minio, _jobs, _entitlements, new BookRegistryService(ctx, TimeProvider.System), config));
    }

    private Task<BeginBookImportUploadResult> Begin(int images, int maxPages = 600,
        BookImportSource source = BookImportSource.Images) =>
        As(s => s.BeginUploadAsync(BookImportOwner.Create(41),
            new BeginBookImportUploadRequest("Fen 5", _gradeId, null, "fen-5", 0, source, images), default), maxPages);

    private Task<BookImportUploadPartResult> Put(Guid id, int n, byte[] bytes) =>
        As(s => s.UploadPartAsync(BookImportOwner.Create(41), id, n, new MemoryStream(bytes), default));

    private Task<CompleteBookImportUploadResult> Complete(Guid id) =>
        As(s => s.CompleteUploadAsync(BookImportOwner.Create(41), id, default));

    [Fact]
    public async Task Images_in_any_order_start_a_job_that_reads_them_from_the_upload_prefix()
    {
        var begin = await Begin(3);
        begin.Error.ShouldBe(StartBookImportError.None);
        begin.Upload!.PartCount.ShouldBe(3);
        var id = begin.Upload.Id;

        (await Put(id, 3, Image(1200, 1700, SKEncodedImageFormat.Png))).ShouldBe(BookImportUploadPartResult.Ok);
        (await Put(id, 1, Image(1200, 1700, SKEncodedImageFormat.Jpeg))).ShouldBe(BookImportUploadPartResult.Ok);
        (await Complete(id)).Error.ShouldBe(CompleteBookImportUploadError.Incomplete);
        (await Put(id, 2, Image(1200, 1700))).ShouldBe(BookImportUploadPartResult.Ok);

        var result = await Complete(id);

        result.Error.ShouldBe(CompleteBookImportUploadError.None);
        result.Job!.Source.ShouldBe("Images");
        await using var check = _db.NewContext();
        var job = await check.BookImportJobs.SingleAsync();
        job.Source.ShouldBe(BookImportSource.Images);
        job.PageCount.ShouldBe(3);
        job.FileUrl.ShouldBe($"/img/exam-questions/book-import-uploads/{id}/");
        _objects.Keys.ShouldContain(job.FileUrl + "image-0002");
        (await check.BookImportUploads.AnyAsync()).ShouldBeFalse();
        _jobs.Received(1).Create(Arg.Is<Hangfire.Common.Job>(j => j.Method.Name == nameof(BookImportJobRunner.RunAsync)), Arg.Any<Hangfire.States.IState>());
    }

    [Fact]
    public async Task Loose_images_open_one_test_per_image_and_use_one_book_import()
    {
        var id = (await As(s => s.BeginUploadAsync(BookImportOwner.Create(41),
            new BeginBookImportUploadRequest("Karışık", _gradeId, null, "karisik", 0, BookImportSource.LooseImages, 2), default))).Upload!.Id;
        (await Put(id, 1, Image(1200, 1700))).ShouldBe(BookImportUploadPartResult.Ok);
        (await Put(id, 2, Image(1200, 1700))).ShouldBe(BookImportUploadPartResult.Ok);

        var result = await Complete(id);

        result.Job!.Source.ShouldBe("LooseImages");
        await using var check = _db.NewContext();
        var job = await check.BookImportJobs.Include(j => j.Tests).SingleAsync();
        job.Kind.ShouldBe(BookImportJobKind.Book);
        job.Tests.OrderBy(t => t.Order).Select(t => (t.Title, t.FileFrom, t.FileTo)).ShouldBe([("Görsel 1", 1, 1), ("Görsel 2", 2, 2)]);
        await _entitlements.Received(1).RecordUsageAsync(41, EntitlementFeatures.BookImport, job.Id.ToString(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_book_from_images_records_no_usage_before_setup_confirmation()
    {
        var id = (await Begin(1)).Upload!.Id;
        await Put(id, 1, Image(1200, 1700));
        await Complete(id);

        await using var check = _db.NewContext();
        (await check.BookImportJobs.Include(j => j.Tests).SingleAsync()).Tests.ShouldBeEmpty();
        await _entitlements.DidNotReceiveWithAnyArgs().RecordUsageAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task Image_count_must_fit_the_page_limit()
    {
        (await Begin(0)).Error.ShouldBe(StartBookImportError.TooManyPages);
        (await Begin(5, maxPages: 4)).Error.ShouldBe(StartBookImportError.TooManyPages);
        (await Begin(4, maxPages: 4)).Error.ShouldBe(StartBookImportError.None);
    }

    [Fact]
    public async Task Non_images_narrow_images_and_out_of_range_numbers_are_rejected()
    {
        var id = (await Begin(2)).Upload!.Id;

        (await Put(id, 1, "%PDF-1.7 not an image"u8.ToArray())).ShouldBe(BookImportUploadPartResult.NotImage);
        (await Put(id, 1, Image(BookImportService.MinPageImageWidth - 1, 1400))).ShouldBe(BookImportUploadPartResult.ImageTooSmall);
        (await Put(id, 3, Image(1200, 1700))).ShouldBe(BookImportUploadPartResult.InvalidPart);
        (await Put(id, 0, Image(1200, 1700))).ShouldBe(BookImportUploadPartResult.InvalidPart);
        _objects.Keys.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_loose_question_image_only_needs_the_lower_width()
    {
        var id = (await Begin(2, source: BookImportSource.LooseImages)).Upload!.Id;

        // Tek soru kırpımı tam sayfa eşiğinin altında kabul edilir; okunaksız küçük görsel yine reddedilir.
        (await Put(id, 1, Image(620, 440))).ShouldBe(BookImportUploadPartResult.Ok);
        (await Put(id, 2, Image(BookImportService.MinLooseImageWidth - 1, 300)))
            .ShouldBe(BookImportUploadPartResult.LooseImageTooSmall);
    }

    [Fact]
    public void A_page_image_becomes_a_standard_width_webp_page()
    {
        var (webp, width, height) = BookImportJobRunner.NormalizePageImage(Image(1200, 1800, SKEncodedImageFormat.Jpeg), 1);

        width.ShouldBe(PdfiumRasterizer.DefaultPageWidth);
        height.ShouldBe(3544); // 1800 × 2363 / 1200 = 3544.5
        using var codec = SKCodec.Create(new MemoryStream(webp));
        codec.EncodedFormat.ShouldBe(SKEncodedImageFormat.Webp);
        codec.Info.Width.ShouldBe(width);

        Should.Throw<BookImportFailure>(() => BookImportJobRunner.NormalizePageImage("broken"u8.ToArray(), 7))
            .Message.ShouldBe("Page image 7 could not be decoded.");
    }
}
