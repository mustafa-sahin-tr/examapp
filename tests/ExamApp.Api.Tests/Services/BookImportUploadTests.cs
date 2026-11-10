using System.Collections.Concurrent;
using System.Text;
using ExamApp.Api.Data;
using ExamApp.Api.Services.BookImport;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Labeling;
using ExamApp.Api.Tests.Support;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #1122: parçalı PDF yükleme — oturum yaşam döngüsü, sahiplik, parça kuralları, süpürme.</summary>
public class BookImportUploadTests : IDisposable
{
    private const long PartSize = BookImportService.UploadPartSizeBytes;

    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();
    private readonly ConcurrentDictionary<string, byte[]> _objects = new(StringComparer.Ordinal);
    private readonly IBackgroundJobClient _jobs = Substitute.For<IBackgroundJobClient>();
    private IEntitlementService _entitlements = Allow(true);
    private readonly int _gradeId;

    /// <summary>İki parçalı dosya: tam bir parça + 10 bayt.</summary>
    private readonly byte[] _file = TwoPartPdf();

    public BookImportUploadTests()
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
        _minio.GetFileStreamAsync(Arg.Any<string>())
            .Returns(ci => Task.FromResult<Stream?>(_objects.TryGetValue(ci.ArgAt<string>(0), out var bytes) ? new MemoryStream(bytes) : null));
        _minio.DeleteFileByUrlAsync(Arg.Any<string>()).Returns(ci => Task.FromResult(_objects.TryRemove(ci.ArgAt<string>(0), out _)));

        using var ctx = _db.NewContext();
        var grade = new Grade { Name = "8. Sınıf" };
        ctx.Grades.Add(grade);
        ctx.SaveChanges();
        _gradeId = grade.Id;
    }

    private BookImportService NewService(AppDbContext ctx)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["MinioConfig:BucketName"] = "exam-questions" })
            .Build();
        return new BookImportService(ctx, _minio, _jobs, _entitlements, new BookRegistryService(ctx, TimeProvider.System), config);
    }

    private async Task<T> As<T>(Func<BookImportService, Task<T>> action)
    {
        await using var ctx = _db.NewContext();
        return await action(NewService(ctx));
    }

    private Task<BeginBookImportUploadResult> Begin(int userId, long size, string name = "LGS Matematik") =>
        As(s => s.BeginUploadAsync(BookImportOwner.Create(userId),
            new BeginBookImportUploadRequest(name, _gradeId, null, "../lgs.pdf", size), default));

    private Task<BookImportUploadPartResult> Part(int userId, Guid id, int n, byte[] bytes) =>
        As(s => s.UploadPartAsync(BookImportOwner.Create(userId), id, n, new MemoryStream(bytes), default));

    private Task<CompleteBookImportUploadResult> Complete(int userId, Guid id) =>
        As(s => s.CompleteUploadAsync(BookImportOwner.Create(userId), id, default));

    private byte[] Slice(int n) => n == 1 ? _file[..(int)PartSize] : _file[(int)PartSize..];

    [Fact]
    public async Task Parts_in_any_order_are_joined_into_the_source_pdf_and_the_job_starts()
    {
        var begin = await Begin(41, _file.Length);
        begin.Error.ShouldBe(StartBookImportError.None);
        begin.Upload!.PartCount.ShouldBe(2);
        begin.Upload.PartSizeBytes.ShouldBe(PartSize);
        var id = begin.Upload.Id;

        (await Part(41, id, 2, Slice(2))).ShouldBe(BookImportUploadPartResult.Ok);
        (await Part(41, id, 1, Slice(1))).ShouldBe(BookImportUploadPartResult.Ok);
        // Yeniden denenen parça üzerine yazar.
        (await Part(41, id, 2, Slice(2))).ShouldBe(BookImportUploadPartResult.Ok);

        var result = await Complete(41, id);

        result.Error.ShouldBe(CompleteBookImportUploadError.None);
        result.Job!.FileName.ShouldBe("lgs.pdf");
        await using var check = _db.NewContext();
        var job = await check.BookImportJobs.SingleAsync();
        job.CreateUserId.ShouldBe(41);
        job.BookName.ShouldBe("LGS Matematik");
        job.FileSizeBytes.ShouldBe(_file.Length);
        _objects[$"/img/exam-questions/book-imports/{job.Id}/source.pdf"].ShouldBe(_file);
        _jobs.Received(1).Create(Arg.Is<Hangfire.Common.Job>(j => j.Method.Name == nameof(BookImportJobRunner.RunAsync)), Arg.Any<Hangfire.States.IState>());
        // Oturum ve parçalar temizlendi; ikinci tamamlama iş açmaz.
        (await check.BookImportUploads.AnyAsync()).ShouldBeFalse();
        _objects.Keys.ShouldNotContain(k => k.Contains("book-import-uploads"));
        (await Complete(41, id)).Error.ShouldBe(CompleteBookImportUploadError.NotFound);
    }

    [Fact]
    public async Task Begin_applies_the_single_upload_rules()
    {
        (await Begin(41, _file.Length, name: " ")).Error.ShouldBe(StartBookImportError.InvalidBookName);
        (await Begin(41, 0)).Error.ShouldBe(StartBookImportError.FileTooLarge);
        (await Begin(41, BookImportService.MaxUploadBytesCeiling + 1)).Error.ShouldBe(StartBookImportError.FileTooLarge);

        _entitlements = Allow(false);
        (await Begin(41, _file.Length)).Error.ShouldBe(StartBookImportError.QuotaReached);

        await using var check = _db.NewContext();
        (await check.BookImportUploads.AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Another_users_session_is_not_found()
    {
        var id = (await Begin(41, _file.Length)).Upload!.Id;

        (await Part(42, id, 1, Slice(1))).ShouldBe(BookImportUploadPartResult.NotFound);
        (await Complete(42, id)).Error.ShouldBe(CompleteBookImportUploadError.NotFound);
        (await As(s => s.AbortUploadAsync(BookImportOwner.Create(42), id, default))).ShouldBeFalse();
        // Admin de başkasının oturumuna dokunamaz.
        (await As(s => s.AbortUploadAsync(BookImportOwner.Create(42, EntitlementRoles.Admin), id, default))).ShouldBeFalse();
        _objects.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task A_part_number_outside_the_file_is_rejected(int n)
    {
        var id = (await Begin(41, _file.Length)).Upload!.Id;
        (await Part(41, id, n, Slice(2))).ShouldBe(BookImportUploadPartResult.InvalidPart);
    }

    [Fact]
    public async Task A_part_of_the_wrong_size_is_rejected()
    {
        var id = (await Begin(41, _file.Length)).Upload!.Id;

        (await Part(41, id, 2, [.. Slice(2), 0])).ShouldBe(BookImportUploadPartResult.InvalidPart);
        (await Part(41, id, 1, Slice(1)[..100])).ShouldBe(BookImportUploadPartResult.InvalidPart);
        _objects.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_first_part_that_is_not_a_pdf_drops_the_session()
    {
        var zip = Encoding.ASCII.GetBytes("PK\u0003\u0004 zip archive");
        var id = (await Begin(41, zip.Length)).Upload!.Id;

        (await Part(41, id, 1, zip)).ShouldBe(BookImportUploadPartResult.NotPdf);

        (await Complete(41, id)).Error.ShouldBe(CompleteBookImportUploadError.NotFound);
    }

    [Fact]
    public async Task Completing_with_a_missing_part_keeps_the_session_for_a_retry()
    {
        var id = (await Begin(41, _file.Length)).Upload!.Id;
        await Part(41, id, 1, Slice(1));

        (await Complete(41, id)).Error.ShouldBe(CompleteBookImportUploadError.Incomplete);

        await Part(41, id, 2, Slice(2));
        (await Complete(41, id)).Error.ShouldBe(CompleteBookImportUploadError.None);
    }

    [Fact]
    public async Task Complete_rechecks_the_plan_limit()
    {
        var id = (await Begin(41, _file.Length)).Upload!.Id;
        await Part(41, id, 1, Slice(1));
        await Part(41, id, 2, Slice(2));

        _entitlements = Allow(false);
        (await Complete(41, id)).Error.ShouldBe(CompleteBookImportUploadError.QuotaReached);
        await using var check = _db.NewContext();
        (await check.BookImportJobs.AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Abort_deletes_the_session_and_its_parts()
    {
        var id = (await Begin(41, _file.Length)).Upload!.Id;
        await Part(41, id, 1, Slice(1));

        (await As(s => s.AbortUploadAsync(BookImportOwner.Create(41), id, default))).ShouldBeTrue();

        _objects.ShouldBeEmpty();
        (await Part(41, id, 2, Slice(2))).ShouldBe(BookImportUploadPartResult.NotFound);
    }

    [Fact]
    public async Task Opening_more_sessions_than_allowed_drops_the_oldest()
    {
        var ids = new List<Guid>();
        for (var i = 0; i <= BookImportService.MaxOpenUploadsPerUser; i++)
        {
            ids.Add((await Begin(41, 20)).Upload!.Id);
            await Task.Delay(5);
        }
        var other = (await Begin(42, 20)).Upload!.Id;

        await using var check = _db.NewContext();
        var open = await check.BookImportUploads.Select(u => u.Id).ToListAsync();
        open.ShouldNotContain(ids[0]);
        open.ShouldBe([.. ids.Skip(1), other], ignoreOrder: true);
    }

    [Fact]
    public async Task The_sweep_removes_only_expired_sessions_and_their_parts()
    {
        var old = (await Begin(41, _file.Length)).Upload!.Id;
        await Part(41, old, 1, Slice(1));
        var fresh = (await Begin(41, _file.Length)).Upload!.Id;
        await using (var age = _db.NewContext())
        {
            var row = await age.BookImportUploads.SingleAsync(u => u.Id == old);
            row.CreatedAt = DateTime.UtcNow - BookImportService.UploadExpiry - TimeSpan.FromMinutes(1);
            await age.SaveChangesAsync();
        }

        (await As(s => s.PurgeStaleUploadsAsync())).ShouldBe(1);

        await using var check = _db.NewContext();
        (await check.BookImportUploads.Select(u => u.Id).ToListAsync()).ShouldBe([fresh]);
        _objects.ShouldBeEmpty();
    }

    private static byte[] TwoPartPdf()
    {
        var bytes = new byte[PartSize + 10];
        "%PDF-1.7"u8.CopyTo(bytes);
        for (var i = 8; i < bytes.Length; i++)
            bytes[i] = (byte)(i % 251);
        return bytes;
    }

    private static IEntitlementService Allow(bool allowed)
    {
        var entitlements = Substitute.For<IEntitlementService>();
        entitlements.CheckAsync(Arg.Any<EntitlementSubject>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new EntitlementCheck(allowed, 5, allowed ? 0 : 5, null));
        return entitlements;
    }

    public void Dispose() => _db.Dispose();
}
