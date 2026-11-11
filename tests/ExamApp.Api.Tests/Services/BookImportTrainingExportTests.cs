using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Labeling;
using ExamApp.Api.Services.BookImport;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Labeling;
using ExamApp.Api.Tests.Support;
using NSubstitute;
using Shouldly;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #1133: incelenmiş kitap sayfaları → question-detector eğitim verisi.</summary>
public class BookImportTrainingExportTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();
    private Guid _jobId;

    public BookImportTrainingExportTests()
    {
        // Sayfa görseli = adresin baytları; SHA-256 bundan hesaplanır.
        _minio.GetFileStreamAsync(Arg.Any<string>())
            .Returns(ci => Task.FromResult<Stream?>(ci.Arg<string>().EndsWith("/8") ? null : new MemoryStream(Bytes(ci.Arg<string>()))));
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Takes_approved_pages_prefers_the_fix_and_skips_what_was_not_reviewed()
    {
        await SeedAsync();

        var summary = await Export().SummarizeAsync(null, default);

        summary.Pages.ShouldBe(3);
        summary.CompletePages.ShouldBe(2);
        summary.EmptyPages.ShouldBe(0);
        summary.Questions.ShouldBe(6);
        summary.ChoiceCounts.ShouldBe(new Dictionary<int, int> { [3] = 3, [4] = 3 });
        summary.Books.ShouldHaveSingleItem().ShouldBe(new BookImportTrainingBookDto(_jobId, "LGS Matematik", 3, 6, 1));
        summary.Skipped.Select(s => $"{s.FilePage}:{s.Reason}").ShouldBe(
            ["2:openTest", "8:imageMissing", "4:fewChoices", "5:flagged", "9:noDetection"]);
    }

    [Fact]
    public async Task Zip_holds_the_page_images_and_their_boxes()
    {
        await SeedAsync();
        using var buffer = new MemoryStream();

        await Export().WriteZipAsync(_jobId, buffer, default);

        buffer.Position = 0;
        using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);
        using var manifest = JsonDocument.Parse(zip.GetEntry("manifest.json")!.Open());
        var pages = manifest.RootElement.GetProperty("pages").EnumerateArray().ToList();
        pages.Select(p => p.GetProperty("filePage").GetInt32()).ShouldBe([1, 6, 7]);

        var first = pages[0];
        var pageId = Sha16(Url(1));
        first.GetProperty("pageId").GetString().ShouldBe(pageId);
        first.GetProperty("source").GetString().ShouldBe("Ai");
        first.GetProperty("complete").GetBoolean().ShouldBeTrue();
        var question = first.GetProperty("questions")[0];
        question.GetProperty("box").EnumerateArray().Select(v => v.GetInt32()).ShouldBe([100, 100, 900, 400]);
        question.GetProperty("answers").GetArrayLength().ShouldBe(4);
        using (var image = zip.GetEntry(first.GetProperty("file").GetString()!)!.Open())
        using (var read = new MemoryStream())
        {
            await image.CopyToAsync(read);
            read.ToArray().ShouldBe(Bytes(Url(1)));
        }

        pages[1].GetProperty("source").GetString().ShouldBe("Manual");
        // Etkinlik bloğu olan sayfa tam sayfa soru modeline girmez; etkinlik soru sayılmaz.
        pages[2].GetProperty("complete").GetBoolean().ShouldBeFalse();
        pages[2].GetProperty("questions").GetArrayLength().ShouldBe(1);
        manifest.RootElement.GetProperty("summary").GetProperty("pages").GetInt32().ShouldBe(3);
    }

    [Fact]
    public async Task Quick_add_and_other_jobs_are_left_out()
    {
        await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            (await ctx.BookImportJobs.FindAsync(_jobId))!.Kind = BookImportJobKind.QuickAdd;
            await ctx.SaveChangesAsync();
        }

        (await Export().SummarizeAsync(null, default)).Pages.ShouldBe(0);
        (await Export().SummarizeAsync(Guid.NewGuid(), default)).Pages.ShouldBe(0);
    }

    private BookImportTrainingExport Export() => new(_db.NewContext(), _minio);

    /// <summary>
    /// Test 1 (onaylı) s. 1–2, test 2 (incelemede) s. 2–3, test 3 (onaylı) s. 4–9: s. 5 uyarılı, s. 6 uyarılı ama düzeltildi,
    /// s. 4'te iki şıklıdan az soru, s. 7'de etkinlik, s. 8 görselsiz, s. 9 tespitsiz. Silinen test sayılmaz.
    /// </summary>
    private async Task SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "8. Sınıf" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();
        var job = new BookImportJob
        {
            Id = Guid.NewGuid(), BookName = "LGS Matematik", GradeId = grade.Id, Status = BookImportJobStatus.ReadyForReview,
            Stage = BookImportStage.Questions, FileUrl = "http://minio/source.pdf",
        };
        _jobId = job.Id;
        ctx.SetCurrentUser(41);
        ctx.BookImportJobs.Add(job);
        var flags = BookImportReviewFlag.Serialize([
            new BookImportReviewFlag(BookImportReviewFlag.ExtraQuestions, [5, 6]),
            new BookImportReviewFlag(BookImportReviewFlag.CountMismatch, [4, 5, 6, 7, 8, 9], Found: 7, Expected: 6),
        ]);
        ctx.BookImportJobTests.AddRange(
            Test(job.Id, 1, 1, 2, BookImportTestStatus.Approved),
            Test(job.Id, 2, 2, 3, BookImportTestStatus.NeedsReview),
            Test(job.Id, 3, 4, 9, BookImportTestStatus.Approved, flags),
            Test(job.Id, 4, 10, 10, BookImportTestStatus.Removed));
        for (var page = 1; page <= 10; page++)
            ctx.BookImportPages.Add(new BookImportPage { JobId = job.Id, FilePage = page, ImageUrl = Url(page), Width = 2000, Height = 3000 });

        Result(ctx, 1, LabelingSource.Ai, Question(100, 4), Question(800, 4));
        Result(ctx, 2, LabelingSource.Ai, Question(100, 4));
        Result(ctx, 4, LabelingSource.Ai, Question(100, 4), Question(800, 1));
        Result(ctx, 5, LabelingSource.Ai, Question(100, 4));
        Result(ctx, 6, LabelingSource.Ai, Question(100, 1));
        Result(ctx, 6, LabelingSource.Manual, Question(100, 3), Question(800, 3), Question(1500, 3));
        Result(ctx, 7, LabelingSource.Ai, Question(100, 4),
            new PredictionDto { X = 100, Y = 800, Width = 900, Height = 900, Kind = LabelingBlocks.Activity },
            new PredictionDto { X = 100, Y = 1800, Width = 900, Height = 200, Kind = LabelingBlocks.InfoBox });
        Result(ctx, 10, LabelingSource.Ai, Question(100, 4));
        await ctx.SaveChangesAsync();
    }

    private static BookImportJobTest Test(Guid jobId, int order, int from, int to, BookImportTestStatus status, string? flags = null) => new()
    {
        JobId = jobId, Order = order, Unit = 1, TestNo = order, FileFrom = from, FileTo = to, Status = status, ReviewFlagsJson = flags,
    };

    private static void Result(AppDbContext ctx, int filePage, LabelingSource source, params PredictionDto[] blocks)
    {
        var sha = Sha256(Url(filePage));
        var page = ctx.LabelingPages.Local.FirstOrDefault(p => p.ImageSha256 == sha)
                   ?? ctx.LabelingPages.Add(new LabelingPage { ImageSha256 = sha, Width = 2000, Height = 3000, CreatedAt = DateTime.UtcNow }).Entity;
        page.Results.Add(new LabelingResult
        {
            Source = source, ModelName = source == LabelingSource.Ai ? "claude-haiku" : string.Empty,
            BoxesJson = JsonSerializer.Serialize(new PredictionListDto { Predictions = [.. blocks] }),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
    }

    private static PredictionDto Question(int y, int options) => new()
    {
        X = 100, Y = y, Width = 900, Height = 400, Kind = LabelingBlocks.McqQuestion,
        Subpredictions = Enumerable.Range(0, options)
            .Select(i => new PredictionDto { X = 120 + i * 200, Y = y + 300, Width = 150, Height = 60 }).ToList(),
    };

    private static string Url(int filePage) => $"http://minio/book-imports/pages/{filePage}";

    private static byte[] Bytes(string url) => Encoding.UTF8.GetBytes(url);

    private static string Sha256(string url) => Convert.ToHexStringLower(SHA256.HashData(Bytes(url)));

    private static string Sha16(string url) => Sha256(url)[..16];
}
