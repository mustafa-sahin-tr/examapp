using ExamApp.Api.Data;
using ExamApp.Api.Services.BookImport;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #1052: hızlı soru ekleme yüklemesi — doğrulama, EXIF yönü ve küçültme, fotoğraf başına test satırı ve hak, cevap anahtarı
/// biçimleri.
/// </summary>
public class QuickAddServiceTests : IDisposable
{
    private const int OwnerId = 52;

    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();
    private readonly IBackgroundJobClient _jobs = Substitute.For<IBackgroundJobClient>();
    private readonly IEntitlementService _entitlements = Substitute.For<IEntitlementService>();
    private readonly Dictionary<string, byte[]> _uploads = new();
    private readonly int _gradeId;

    public QuickAddServiceTests()
    {
        _minio.UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(ci =>
        {
            using var copy = new MemoryStream();
            ci.ArgAt<Stream>(0).CopyTo(copy);
            _uploads[ci.ArgAt<string>(1)] = copy.ToArray();
            return Task.FromResult("http://minio/exam-questions/" + ci.ArgAt<string>(1));
        });
        Quota(new EntitlementCheck(true, 5, 0, DateTime.UtcNow.AddDays(10)));
        using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5. Sınıf" };
        ctx.Grades.Add(grade);
        ctx.SaveChanges();
        _gradeId = grade.Id;
    }

    [Fact]
    public async Task Photos_are_stored_upright_and_scaled_with_one_test_row_each_and_counted_per_photo()
    {
        var rotated = TestImages.WithExifOrientation(TestImages.Jpeg(4000, 3000), 6); // gösterimde 3000x4000

        var result = await StartAsync(Photo(rotated, "1-A 2-C"), Photo(TestImages.Png(800, 600)));

        result.Error.ShouldBe(StartQuickAddError.None);
        result.Job!.Kind.ShouldBe("QuickAdd");
        await using var check = _db.NewContext();
        var job = await check.BookImportJobs.Include(j => j.Pages).Include(j => j.Tests).SingleAsync();
        (job.Kind, job.Stage, job.Status, job.BookId, job.PageCount, job.CreateUserId)
            .ShouldBe((BookImportJobKind.QuickAdd, BookImportStage.Questions, BookImportJobStatus.Queued, (int?)null, (int?)2, (int?)OwnerId));

        var pages = job.Pages.OrderBy(p => p.FilePage).ToList();
        (pages[0].Width, pages[0].Height).ShouldBe((1800, QuickAddService.PhotoMaxEdge));
        (pages[1].Width, pages[1].Height).ShouldBe((800, 600));
        pages[0].ImageUrl.ShouldEndWith(QuickAddService.PhotoObjectName(job.Id, 1));
        var stored = TestImages.Info(_uploads[QuickAddService.PhotoObjectName(job.Id, 2)]);
        stored.Format.ShouldBe(SkiaSharp.SKEncodedImageFormat.Jpeg);

        var tests = job.Tests.OrderBy(t => t.Order).ToList();
        tests.Select(t => (t.Title, t.FileFrom, t.FileTo, t.AnswerKey))
            .ShouldBe([("Fotoğraf 1", 1, 1, "AC"), ("Fotoğraf 2", 2, 2, null)]);

        await _entitlements.Received(1).RecordUsageAsync(OwnerId, EntitlementFeatures.QuickAdd, $"quick-add:{job.Id}:1", Arg.Any<CancellationToken>());
        await _entitlements.Received(1).RecordUsageAsync(OwnerId, EntitlementFeatures.QuickAdd, $"quick-add:{job.Id}:2", Arg.Any<CancellationToken>());
        _jobs.ReceivedWithAnyArgs(1).Create(default!, default!);
    }

    [Fact]
    public async Task Upload_is_rejected_when_the_remaining_allowance_does_not_cover_every_photo()
    {
        var resets = DateTime.UtcNow.AddDays(3);
        Quota(new EntitlementCheck(true, 5, 4, resets));

        var result = await StartAsync(Photo(TestImages.Jpeg(100, 100)), Photo(TestImages.Jpeg(100, 100)));

        result.Error.ShouldBe(StartQuickAddError.QuotaReached);
        result.Quota!.Remaining.ShouldBe(1);
        await AssertNothingStoredAsync();
    }

    [Fact]
    public async Task A_broken_photo_rejects_the_whole_upload_and_names_the_photo()
    {
        var result = await StartAsync(Photo(TestImages.Jpeg(100, 100)), Photo("not an image"u8.ToArray()));

        (result.Error, result.Photo).ShouldBe((StartQuickAddError.InvalidImage, 2));
        await AssertNothingStoredAsync();
    }

    [Theory]
    [InlineData(0, StartQuickAddError.NoPhotos)]
    [InlineData(QuickAddService.MaxPhotos + 1, StartQuickAddError.TooManyPhotos)]
    public async Task Photo_count_is_validated(int count, StartQuickAddError expected)
    {
        var photos = Enumerable.Range(0, count).Select(_ => Photo(TestImages.Jpeg(10, 10))).ToArray();

        (await StartAsync(photos)).Error.ShouldBe(expected);
    }

    [Fact]
    public async Task Grade_size_and_answer_key_are_validated_before_anything_is_stored()
    {
        await using var ctx = _db.NewContext();
        var service = new QuickAddService(ctx, _minio, _jobs, _entitlements);
        var photo = Photo(TestImages.Jpeg(10, 10));

        (await service.StartAsync(Owner, new(9999, null, [photo]), CancellationToken.None)).Error.ShouldBe(StartQuickAddError.InvalidGrade);
        (await StartAsync(photo with { Length = QuickAddService.MaxPhotoBytes + 1 })).Error.ShouldBe(StartQuickAddError.PhotoTooLarge);
        var badKey = await StartAsync(photo, photo with { AnswerKey = "A F" });
        (badKey.Error, badKey.Photo).ShouldBe((StartQuickAddError.InvalidAnswerKey, 2));
        await AssertNothingStoredAsync();
    }

    [Theory]
    [InlineData("A C B D", "ACBD")]
    [InlineData("acbd", "ACBD")]
    [InlineData("1-A 2-C, 3) b; 4.D 5:e", "ACBDE")]
    [InlineData("1A 2C", "AC")]
    [InlineData("A ? - _ B", "A???B")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Answer_key_formats_are_read_in_order(string? input, string? expected)
    {
        QuickAddService.TryParseAnswerKey(input, out var key).ShouldBeTrue();
        key.ShouldBe(expected);
    }

    [Theory]
    [InlineData("A F")]
    [InlineData("1-")]
    [InlineData("A/B")]
    public void Unreadable_answer_keys_are_rejected(string input) =>
        QuickAddService.TryParseAnswerKey(input, out _).ShouldBeFalse();

    private static readonly BookImportOwner Owner = new(EntitlementSubject.Create(OwnerId), BookImportJobKind.QuickAdd);

    private async Task<StartQuickAddResult> StartAsync(params QuickAddPhoto[] photos)
    {
        await using var ctx = _db.NewContext();
        return await new QuickAddService(ctx, _minio, _jobs, _entitlements)
            .StartAsync(Owner, new StartQuickAddRequest(_gradeId, null, photos), CancellationToken.None);
    }

    private static QuickAddPhoto Photo(byte[] bytes, string? key = null) =>
        new("photo.jpg", bytes.Length, new MemoryStream(bytes), key);

    private void Quota(EntitlementCheck check) =>
        _entitlements.CheckAsync(Arg.Any<EntitlementSubject>(), EntitlementFeatures.QuickAdd, Arg.Any<CancellationToken>()).Returns(check);

    private async Task AssertNothingStoredAsync()
    {
        _uploads.ShouldBeEmpty();
        await using var check = _db.NewContext();
        (await check.BookImportJobs.CountAsync()).ShouldBe(0);
        await _entitlements.DidNotReceiveWithAnyArgs().RecordUsageAsync(default, default!, default!, default);
    }

    public void Dispose() => _db.Dispose();
}
