using System.Text;
using ExamApp.Api.Data;
using ExamApp.Api.Services.BookImport;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Teachers;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly FakeBookSetupAi _ai = new();
    private FakePageCleaner? _cleaner;

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
        await _minio.Received(1).DeleteFileByUrlAsync("http://minio/exam-questions/book-imports/x/source.pdf");
    }

    [Fact]
    public async Task Retry_after_all_pages_were_rendered_does_not_need_the_deleted_pdf()
    {
        _minio.GetFileStreamAsync(Arg.Any<string>()).Returns(Task.FromResult<Stream?>(null));
        var id = await SeedJobAsync(stage: BookImportStage.Setup, status: BookImportJobStatus.Queued);
        await using (var seed = _db.NewContext())
        {
            var job = await seed.BookImportJobs.SingleAsync();
            job.PageCount = 2;
            for (var page = 1; page <= 2; page++)
                seed.BookImportPages.Add(new BookImportPage { JobId = id, FilePage = page, ImageUrl = $"http://minio/p/{page}.webp", Width = 400, Height = 566 });
            await seed.SaveChangesAsync();
        }

        await RunAsync(id);

        await using var check = _db.NewContext();
        var after = await check.BookImportJobs.SingleAsync();
        after.Error.ShouldNotBe("The uploaded PDF could not be read from storage.");
        await _minio.DidNotReceive().GetFileStreamAsync("http://minio/exam-questions/book-imports/x/source.pdf");
        await _minio.DidNotReceive().DeleteFileByUrlAsync(Arg.Any<string>());
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
            Substitute.For<ExamApp.Api.Services.Entitlements.IEntitlementService>(),
            new ExamApp.Api.Services.Labeling.BookRegistryService(ctx, TimeProvider.System), new ConfigurationBuilder().Build());

        (await service.GetPageImageAsync(BookImportOwner.Create(OwnerId), id, 2, default)).ShouldNotBeNull();
        (await service.GetPageImageAsync(BookImportOwner.Create(99), id, 2, default)).ShouldBeNull();
        (await service.GetPageImageAsync(BookImportOwner.Create(OwnerId), id, 9, default)).ShouldBeNull();
        (await service.GetBookPageImageAsync(bookId, 3, default)).ShouldNotBeNull();
        (await service.GetBookPageImageAsync(bookId + 1, 3, default)).ShouldBeNull();
        await _minio.Received().GetFileStreamAsync("http://minio/exam-questions/" + BookImportJobRunner.PageObjectName(id, 2));
    }

    [Fact]
    public async Task Pages_are_cleaned_before_setup_and_the_originals_are_kept()
    {
        _cleaner = new FakePageCleaner { Watermark = true, MarkColour = [182, 23, 113] };
        var id = await SeedJobAsync();

        await RunAsync(id);

        await using var check = _db.NewContext();
        var job = await check.BookImportJobs.SingleAsync();
        job.Status.ShouldBe(BookImportJobStatus.AwaitingConfirmation);
        var cleaning = BookPageCleaning.Parse(job.CleanJson).ShouldNotBeNull();
        cleaning.Status.ShouldBe(BookPageCleaning.Done);
        cleaning.Watermark.ShouldBeTrue();
        cleaning.MarkColour.ShouldBe([182, 23, 113]);
        cleaning.Marks.ShouldBe(3);
        cleaning.Processed.ShouldBe(3);
        _cleaner.Sampled.ShouldBe(3);

        foreach (var page in await check.BookImportPages.OrderBy(p => p.FilePage).ToListAsync())
        {
            page.OriginalImageUrl.ShouldBe("http://minio/exam-questions/" + BookImportJobRunner.PageObjectName(id, page.FilePage));
            page.CleanImageUrl.ShouldBe("http://minio/exam-questions/" + BookPageCleaningService.CleanObjectName(id, page.FilePage));
            page.ImageUrl.ShouldBe(page.CleanImageUrl);
            page.MarksJson.ShouldNotBeNull().ShouldContain("\"letter\":\"B\"");
            _uploads[BookPageCleaningService.CleanObjectName(id, page.FilePage)].ShouldBe(FakePageCleaner.Cleaned);
        }

        // "Orijinal sayfaları kullan": etkin görsel orijinale döner, geri alınınca temize.
        await using (var ctx = _db.NewContext())
        {
            var service = new BookPageCleaningService(ctx, _minio, _cleaner, null, NullLogger<BookPageCleaningService>.Instance);
            (await service.SetUseOriginalAsync(id, true, default)).ShouldBe(PageCleaningError.None);
        }

        await using (var ctx = _db.NewContext())
        {
            (await ctx.BookImportPages.Where(p => p.JobId == id).ToListAsync()).ShouldAllBe(p => p.ImageUrl == p.OriginalImageUrl);
            BookPageCleaning.Parse((await ctx.BookImportJobs.SingleAsync()).CleanJson)!.UseOriginal.ShouldBeTrue();
            var service = new BookPageCleaningService(ctx, _minio, _cleaner, null, NullLogger<BookPageCleaningService>.Instance);
            (await service.SetUseOriginalAsync(id, false, default)).ShouldBe(PageCleaningError.None);
        }

        await using var after = _db.NewContext();
        (await after.BookImportPages.Where(p => p.JobId == id).ToListAsync()).ShouldAllBe(p => p.ImageUrl == p.CleanImageUrl);
    }

    [Fact]
    public async Task Pages_stay_original_when_nothing_is_found_or_the_cleaner_fails()
    {
        _cleaner = new FakePageCleaner();
        var nothing = await SeedJobAsync();
        await RunAsync(nothing);

        _cleaner = new FakePageCleaner { Watermark = true, FailOnPage = 2 };
        var failed = await SeedJobAsync();
        await RunAsync(failed);

        await using var check = _db.NewContext();
        foreach (var (id, status) in new[] { (nothing, BookPageCleaning.NothingFound), (failed, BookPageCleaning.Failed) })
        {
            var job = await check.BookImportJobs.SingleAsync(j => j.Id == id);
            job.Status.ShouldBe(BookImportJobStatus.AwaitingConfirmation);
            BookPageCleaning.Parse(job.CleanJson)!.Status.ShouldBe(status);
            (await check.BookImportPages.Where(p => p.JobId == id).ToListAsync())
                .ShouldAllBe(p => p.CleanImageUrl == null && p.MarksJson == null
                                  && p.ImageUrl == "http://minio/exam-questions/" + BookImportJobRunner.PageObjectName(id, p.FilePage));
        }
    }

    [Fact]
    public async Task Cleaning_again_with_a_picked_colour_starts_from_the_original_pages()
    {
        _cleaner = new FakePageCleaner { Watermark = true };
        var id = await SeedJobAsync();
        await RunAsync(id);
        var jobs = Substitute.For<Hangfire.IBackgroundJobClient>();

        await using (var ctx = _db.NewContext())
        {
            var service = new BookPageCleaningService(ctx, _minio, _cleaner, jobs, NullLogger<BookPageCleaningService>.Instance);
            (await service.StartAsync(id, [200, 40, 120], default)).ShouldBe(PageCleaningError.None);
            jobs.ReceivedCalls().Count().ShouldBe(1);
            await service.RunAsync(id, [200, 40, 120], default);
        }

        _cleaner.LastColour.ShouldBe([200, 40, 120]);
        // Temizlik orijinali okur, temiz kopyayı değil.
        _cleaner.Inputs.ShouldAllBe(bytes => !bytes.SequenceEqual(FakePageCleaner.Cleaned));
        await using var check = _db.NewContext();
        var cleaning = BookPageCleaning.Parse((await check.BookImportJobs.SingleAsync()).CleanJson)!;
        cleaning.Status.ShouldBe(BookPageCleaning.Done);
        cleaning.ColourPicked.ShouldBeTrue();
        cleaning.MarkColour.ShouldBe([200, 40, 120]);

        // Onay beklemeyen iş elle temizlenmez.
        (await check.BookImportJobs.SingleAsync()).Status = BookImportJobStatus.ReadyForReview;
        await check.SaveChangesAsync();
        var other = new BookPageCleaningService(check, _minio, _cleaner, jobs, NullLogger<BookPageCleaningService>.Instance);
        (await other.StartAsync(id, null, default)).ShouldBe(PageCleaningError.InvalidState);
    }

    private sealed class FakePageCleaner : IPageCleaner
    {
        public static readonly byte[] Cleaned = Encoding.ASCII.GetBytes("clean");
        public bool Watermark { get; init; }
        public int[]? MarkColour { get; init; }
        public int? FailOnPage { get; init; }
        public int Sampled { get; private set; }
        public int[]? LastColour { get; private set; }
        public List<byte[]> Inputs { get; } = [];
        private int _cleaned;

        public Task<bool> IsAvailableAsync(CancellationToken ct) => Task.FromResult(true);

        public Task<PageCleanAnalysis> AnalyzeAsync(IReadOnlyList<byte[]> pages, int[]? markColour, CancellationToken ct)
        {
            Sampled = pages.Count;
            LastColour = markColour;
            Inputs.AddRange(pages);
            return Task.FromResult(new PageCleanAnalysis("t", Watermark, markColour ?? MarkColour, 0));
        }

        public Task<PageCleanResult> CleanAsync(string token, byte[] page, CancellationToken ct)
        {
            Inputs.Add(page);
            if (++_cleaned == FailOnPage)
                throw new HttpRequestException("detector down");
            return Task.FromResult(new PageCleanResult(Cleaned, [new PageMark("B", 10, 10, 20, 20)], Watermark));
        }
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
        await new BookImportJobRunner(ctx, _guard, _minio, new PdfiumRasterizer(pageWidth: 400), _ai,
            Substitute.For<ExamApp.Api.Services.Labeling.ILabelingService>(), Substitute.For<IServiceScopeFactory>(),
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), pageCleaner: _cleaner).RunLockedAsync(id);
    }

    public void Dispose() => _db.Dispose();
}
