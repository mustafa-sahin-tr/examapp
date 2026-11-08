using System.Text;
using ExamApp.Api.Data;
using ExamApp.Api.Services.BookImport;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Teachers;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SkiaSharp;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #1020: Sayfalar aşaması — PDF → WebP sayfa görselleri, sayfa sınırı, bozuk dosya, devam etme.</summary>
public class BookImportJobRunnerTests : IDisposable
{
    private const int OwnerId = 41;

    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();
    private readonly IApprovedTeacherGuard _guard = Substitute.For<IApprovedTeacherGuard>();
    private readonly Dictionary<string, byte[]> _uploads = new();
    private byte[] _pdf = SamplePdf.Create(3);

    public BookImportJobRunnerTests()
    {
        _guard.CheckAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(TeacherApprovalCheck.NoTeacherProfile);
        _minio.GetFileStreamAsync(Arg.Any<string>()).Returns(_ => Task.FromResult<Stream?>(new MemoryStream(_pdf)));
        _minio.UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci =>
            {
                using var copy = new MemoryStream();
                ci.ArgAt<Stream>(0).CopyTo(copy);
                _uploads[ci.ArgAt<string>(1)] = copy.ToArray();
                return Task.FromResult("http://minio/exam-questions/" + ci.ArgAt<string>(1));
            });
    }

    [Fact]
    public async Task Pages_are_rendered_one_by_one_to_webp_and_recorded()
    {
        var id = await SeedJobAsync();

        await RunAsync(id);

        await using var check = _db.NewContext();
        var job = await check.BookImportJobs.SingleAsync();
        job.Status.ShouldBe(BookImportJobStatus.AwaitingConfirmation);
        job.PageCount.ShouldBe(3);
        job.Error.ShouldBeNull();

        var pages = await check.BookImportPages.OrderBy(p => p.FilePage).ToListAsync();
        pages.Select(p => p.FilePage).ShouldBe([1, 2, 3]);
        foreach (var page in pages)
        {
            var key = BookImportJobRunner.PageObjectName(id, page.FilePage);
            page.ImageUrl.ShouldBe("http://minio/exam-questions/" + key);
            page.Width.ShouldBe(400);
            page.Height.ShouldBe(566); // A4 oranı
            page.SizeBytes.ShouldBe(_uploads[key].LongLength);
            using var codec = SKCodec.Create(new MemoryStream(_uploads[key]));
            codec.EncodedFormat.ShouldBe(SKEncodedImageFormat.Webp);
        }

        await _minio.Received(3).UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), "image/webp");
    }

    [Fact]
    public async Task Pdf_over_the_page_limit_fails_with_a_readable_error_and_stores_nothing()
    {
        var id = await SeedJobAsync();

        await RunAsync(id, maxPages: 2);

        await using var check = _db.NewContext();
        var job = await check.BookImportJobs.SingleAsync();
        job.Status.ShouldBe(BookImportJobStatus.Failed);
        job.Stage.ShouldBe(BookImportStage.Pages);
        job.Error.ShouldBe("The PDF has 3 pages; the limit is 2.");
        (await check.BookImportPages.CountAsync()).ShouldBe(0);
        _uploads.ShouldBeEmpty();
    }

    [Fact]
    public async Task Corrupt_pdf_fails_the_job_with_a_readable_error()
    {
        _pdf = Encoding.ASCII.GetBytes("%PDF-1.7\nthis is not really a pdf");
        var id = await SeedJobAsync();

        await RunAsync(id);

        await using var check = _db.NewContext();
        var job = await check.BookImportJobs.SingleAsync();
        job.Status.ShouldBe(BookImportJobStatus.Failed);
        job.Error.ShouldBe("The PDF could not be opened (damaged or not a valid PDF).");
        job.FinishedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Missing_source_file_fails_the_job()
    {
        _minio.GetFileStreamAsync(Arg.Any<string>()).Returns(Task.FromResult<Stream?>(null));
        var id = await SeedJobAsync();

        await RunAsync(id);

        await using var check = _db.NewContext();
        (await check.BookImportJobs.SingleAsync()).Error.ShouldBe("The uploaded PDF could not be read from storage.");
    }

    [Fact]
    public async Task Interrupted_job_resumes_after_the_last_stored_page()
    {
        var id = await SeedJobAsync(stage: BookImportStage.Pages, status: BookImportJobStatus.Running);
        await using (var seed = _db.NewContext())
        {
            seed.BookImportPages.Add(new BookImportPage { JobId = id, FilePage = 1, ImageUrl = "http://minio/old/1.webp", Width = 400, Height = 566 });
            await seed.SaveChangesAsync();
        }

        await RunAsync(id);

        await using var check = _db.NewContext();
        (await check.BookImportPages.OrderBy(p => p.FilePage).Select(p => p.FilePage).ToListAsync()).ShouldBe([1, 2, 3]);
        _uploads.Keys.ShouldBe([BookImportJobRunner.PageObjectName(id, 2), BookImportJobRunner.PageObjectName(id, 3)], ignoreOrder: true);
        (await check.BookImportJobs.SingleAsync()).Status.ShouldBe(BookImportJobStatus.AwaitingConfirmation);
    }

    [Fact]
    public async Task Page_images_are_served_to_the_owner_and_by_book_once_linked()
    {
        var id = await SeedJobAsync();
        await RunAsync(id);
        int bookId;
        await using (var seed = _db.NewContext())
        {
            var book = new Book { Name = "Puan BTS 5 Matematik" };
            seed.Books.Add(book);
            await seed.SaveChangesAsync();
            (await seed.BookImportJobs.SingleAsync()).BookId = book.Id;
            await seed.SaveChangesAsync();
            bookId = book.Id;
        }

        await using var ctx = _db.NewContext();
        var service = new BookImportService(ctx, _minio, Substitute.For<Hangfire.IBackgroundJobClient>(),
            Substitute.For<ExamApp.Api.Services.Entitlements.IEntitlementService>(), new ConfigurationBuilder().Build());

        (await service.GetPageImageAsync(BookImportOwner.Create(OwnerId), id, 2, default)).ShouldNotBeNull();
        (await service.GetPageImageAsync(BookImportOwner.Create(99), id, 2, default)).ShouldBeNull();
        (await service.GetPageImageAsync(BookImportOwner.Create(OwnerId), id, 9, default)).ShouldBeNull();
        (await service.GetBookPageImageAsync(bookId, 3, default)).ShouldNotBeNull();
        (await service.GetBookPageImageAsync(bookId + 1, 3, default)).ShouldBeNull();
        await _minio.Received().GetFileStreamAsync("http://minio/exam-questions/" + BookImportJobRunner.PageObjectName(id, 2));
    }

    private async Task<Guid> SeedJobAsync(BookImportStage stage = BookImportStage.Upload, BookImportJobStatus status = BookImportJobStatus.Queued)
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5. Sınıf" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();
        var job = new BookImportJob
        {
            Id = Guid.NewGuid(),
            BookName = "Puan BTS 5 Matematik",
            GradeId = grade.Id,
            Status = status,
            Stage = stage,
            FileUrl = "http://minio/exam-questions/book-imports/x/source.pdf",
            FileName = "kitap.pdf",
            FileSizeBytes = _pdf.Length,
        };
        ctx.SetCurrentUser(OwnerId);
        ctx.BookImportJobs.Add(job);
        await ctx.SaveChangesAsync();
        return job.Id;
    }

    private async Task RunAsync(Guid id, int? maxPages = null)
    {
        var settings = new Dictionary<string, string?>();
        if (maxPages is { } max)
            settings["BookImport:MaxPages"] = max.ToString();
        await using var ctx = _db.NewContext();
        await new BookImportJobRunner(ctx, _guard, _minio, new PdfiumRasterizer(pageWidth: 400),
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build()).RunLockedAsync(id);
    }

    public void Dispose() => _db.Dispose();
}
