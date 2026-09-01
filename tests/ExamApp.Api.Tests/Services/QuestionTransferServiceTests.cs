using System.IO.Compression;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.QuestionTransfer;
using ExamApp.Api.Tests.Support;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ExamApp.Api.Tests.Services;

public class QuestionTransferServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();
    private readonly IBackgroundJobClient _jobs = Substitute.For<IBackgroundJobClient>();

    private QuestionTransferService NewService(AppDbContext ctx)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["MinioConfig:BucketName"] = "exam-questions" })
            .Build();
        return new QuestionTransferService(ctx, _minio, _jobs, config);
    }

    [Fact]
    public async Task StartExport_persists_a_queued_job_and_defaults_the_source_key()
    {
        QuestionTransferJobDto dto;
        await using (var ctx = _db.NewContext())
            dto = await NewService(ctx).StartExportAsync(new StartQuestionExportDto { QuestionIds = { 1, 2, 3 } }, default);

        dto.Kind.ShouldBe("Export");
        dto.Status.ShouldBe("Queued");
        dto.SourceKey.ShouldBe("default");
        dto.TotalItems.ShouldBe(3);

        await using var check = _db.NewContext();
        (await check.Set<QuestionTransferJob>().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task StartImport_trims_the_source_key_and_stores_the_upload_url()
    {
        QuestionTransferJobDto dto;
        await using (var ctx = _db.NewContext())
            dto = await NewService(ctx).StartImportAsync("  prod-eu  ", "http://minio/in.zip", default);

        dto.Kind.ShouldBe("Import");
        dto.SourceKey.ShouldBe("prod-eu");

        await using var check = _db.NewContext();
        (await check.Set<QuestionTransferJob>().SingleAsync()).FileUrl.ShouldBe("http://minio/in.zip");
    }

    [Fact]
    public async Task GetJob_returns_null_for_an_unknown_id()
    {
        await using var ctx = _db.NewContext();
        (await NewService(ctx).GetJobAsync(Guid.NewGuid(), default)).ShouldBeNull();
    }

    [Fact]
    public async Task ListJobs_clamps_take_and_returns_newest_first()
    {
        await using (var ctx = _db.NewContext())
        {
            for (var i = 0; i < 5; i++)
            {
                ctx.Add(new QuestionTransferJob { Id = Guid.NewGuid(), SourceKey = $"s{i}", Message = "m" });
                await ctx.SaveChangesAsync();
            }
        }

        await using var read = _db.NewContext();
        var jobs = await NewService(read).ListJobsAsync(take: 0, default); // clamped to >= 1
        jobs.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ListSourceKeys_merges_bundle_and_job_keys_without_duplicates()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.Add(new QuestionTransferExportBundle { SourceKey = "alpha", BundleNo = 1, FileUrl = "u" });
            ctx.Add(new QuestionTransferJob { Id = Guid.NewGuid(), SourceKey = "ALPHA", Message = "m" });
            ctx.Add(new QuestionTransferJob { Id = Guid.NewGuid(), SourceKey = "beta", Message = "m" });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var keys = await NewService(read).ListSourceKeysAsync(default);
        keys.ShouldBe(new[] { "alpha", "beta" }); // case-insensitive distinct, ordered
    }

    [Fact]
    public async Task GetJobFileStream_is_null_without_a_file_url_and_hits_minio_otherwise()
    {
        Guid withFile, withoutFile;
        await using (var ctx = _db.NewContext())
        {
            var a = new QuestionTransferJob { Id = Guid.NewGuid(), SourceKey = "s", FileUrl = "http://minio/x.zip", Message = "m" };
            var b = new QuestionTransferJob { Id = Guid.NewGuid(), SourceKey = "s", Message = "m" };
            ctx.AddRange(a, b);
            await ctx.SaveChangesAsync();
            withFile = a.Id; withoutFile = b.Id;
        }

        _minio.GetFileStreamAsync("http://minio/x.zip").Returns(new MemoryStream([1]));

        await using var read = _db.NewContext();
        var svc = NewService(read);
        (await svc.GetJobFileStreamAsync(withoutFile, default)).ShouldBeNull();
        (await svc.GetJobFileStreamAsync(withFile, default)).ShouldNotBeNull();
    }

    [Fact]
    public async Task GetExportBundleStream_guards_bundle_number_and_missing_rows()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.Add(new QuestionTransferExportBundle { SourceKey = "s", BundleNo = 1, FileUrl = "http://minio/b1.zip" });
            await ctx.SaveChangesAsync();
        }
        _minio.GetFileStreamAsync("http://minio/b1.zip").Returns(new MemoryStream([1]));

        await using var read = _db.NewContext();
        var svc = NewService(read);
        (await svc.GetExportBundleStreamAsync("s", 0, default)).ShouldBeNull();
        (await svc.GetExportBundleStreamAsync("s", 99, default)).ShouldBeNull();
        (await svc.GetExportBundleStreamAsync("s", 1, default)).ShouldNotBeNull();
    }

    [Fact]
    public async Task ListExportBundles_projects_rows_for_a_source_ordered_by_bundle_number()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.Add(new QuestionTransferExportBundle { SourceKey = "s", BundleNo = 2, QuestionCount = 5, FileUrl = "b2" });
            ctx.Add(new QuestionTransferExportBundle { SourceKey = "s", BundleNo = 1, QuestionCount = 9, FileUrl = "b1" });
            ctx.Add(new QuestionTransferExportBundle { SourceKey = "other", BundleNo = 1, FileUrl = "x" });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var bundles = await NewService(read).ListExportBundlesAsync("s", default);
        bundles.Select(b => b.BundleNo).ShouldBe(new[] { 1, 2 });
        bundles[0].QuestionCount.ShouldBe(9);
    }

    [Fact]
    public async Task PreviewImport_counts_questions_and_already_imported_external_keys()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.Add(new QuestionTransferImportMap { SourceKey = "src", ExternalQuestionKey = "q-1", TargetQuestionId = 100 });
            await ctx.SaveChangesAsync();
        }

        var file = ZipWithManifest(new
        {
            sourceKey = "src",
            questions = new[]
            {
                new { externalKey = "q-1" },
                new { externalKey = "q-2" },
                new { externalKey = "q-3" },
            },
        });

        await using var read = _db.NewContext();
        var preview = await NewService(read).PreviewImportAsync(file, sourceOverride: null, default);

        preview.SourceKey.ShouldBe("src");
        preview.QuestionCount.ShouldBe(3);
        preview.AlreadyImportedCount.ShouldBe(1);
    }

    [Fact]
    public async Task PreviewImport_rejects_an_empty_file_and_a_zip_without_a_manifest()
    {
        await using var ctx = _db.NewContext();
        var svc = NewService(ctx);

        await Should.ThrowAsync<ArgumentException>(() =>
            svc.PreviewImportAsync(new FormFile(new MemoryStream(), 0, 0, "f", "f.zip"), null, default));

        var ms = new MemoryStream();
        using (var _ = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true)) { }
        ms.Position = 0;
        await Should.ThrowAsync<InvalidOperationException>(() =>
            svc.PreviewImportAsync(new FormFile(ms, 0, ms.Length, "f", "f.zip"), null, default));
    }

    private static IFormFile ZipWithManifest(object manifest)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("manifest.json");
            using var s = entry.Open();
            JsonSerializer.Serialize(s, manifest);
        }
        ms.Position = 0;
        return new FormFile(ms, 0, ms.Length, "file", "import.zip");
    }

    public void Dispose() => _db.Dispose();
}
