using System.Text;
using ExamApp.Api.Data;
using ExamApp.Api.Services.BookImport;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Teachers;
using ExamApp.Api.Tests.Support;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #1018: kitaptan aktarım işi — yükleme doğrulaması, sahiplik, kota kapısı ve iskelet aşamalar.</summary>
public class BookImportServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();
    private readonly IBackgroundJobClient _jobs = Substitute.For<IBackgroundJobClient>();
    private IEntitlementService _entitlements = new UnlimitedEntitlementService();
    private readonly int _gradeId;

    public BookImportServiceTests()
    {
        _minio.UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci => Task.FromResult("http://minio/exam-questions/" + ci.ArgAt<string>(1)));
        using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5. Sınıf" };
        ctx.Grades.Add(grade);
        ctx.SaveChanges();
        _gradeId = grade.Id;
    }

    private BookImportService NewService(AppDbContext ctx, long? maxUploadBytes = null)
    {
        var settings = new Dictionary<string, string?>();
        if (maxUploadBytes is { } max)
            settings["BookImport:MaxUploadBytes"] = max.ToString();
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new BookImportService(ctx, _minio, _jobs, _entitlements, config);
    }

    private StartBookImportRequest Pdf(string name = "Puan BTS 5 Matematik", int? gradeId = null, string content = "%PDF-1.7 body")
    {
        var bytes = Encoding.ASCII.GetBytes(content);
        return new StartBookImportRequest(name, gradeId ?? _gradeId, null, "../kitap.pdf", bytes.Length, new MemoryStream(bytes));
    }

    private async Task<StartBookImportResult> StartAs(int userId, StartBookImportRequest request, bool isAdmin = false, long? maxUploadBytes = null)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx, maxUploadBytes).StartAsync(new BookImportOwner(userId, isAdmin), request, default);
    }

    [Fact]
    public async Task Start_uploads_the_pdf_privately_queues_the_job_and_records_the_owner()
    {
        var result = await StartAs(41, Pdf());

        result.Error.ShouldBe(StartBookImportError.None);
        result.Job!.Status.ShouldBe("Queued");
        result.Job.Stage.ShouldBe("Upload");
        result.Job.FileName.ShouldBe("kitap.pdf");

        await using var check = _db.NewContext();
        var job = await check.BookImportJobs.SingleAsync();
        job.CreateUserId.ShouldBe(41);
        job.FileUrl.ShouldBe($"http://minio/exam-questions/book-imports/{job.Id}/source.pdf");
        await _minio.Received(1).UploadFileAsync(Arg.Any<Stream>(), $"book-imports/{job.Id}/source.pdf", Arg.Any<string>(), "application/pdf");
        _jobs.Received(1).Create(
            Arg.Is<Hangfire.Common.Job>(j => j.Method.Name == nameof(BookImportJobRunner.RunAsync) && (Guid)j.Args[0]! == job.Id),
            Arg.Any<Hangfire.States.IState>());
    }

    [Fact]
    public async Task Start_uploads_the_whole_file_including_the_bytes_read_for_the_pdf_check()
    {
        byte[]? uploaded = null;
        _minio.UploadFileAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci =>
            {
                using var copy = new MemoryStream();
                ci.ArgAt<Stream>(0).CopyTo(copy);
                uploaded = copy.ToArray();
                return Task.FromResult("http://minio/x.pdf");
            });

        (await StartAs(41, Pdf(content: "%PDF-1.4 whole file"))).Error.ShouldBe(StartBookImportError.None);

        Encoding.ASCII.GetString(uploaded!).ShouldBe("%PDF-1.4 whole file");
    }

    [Theory]
    [InlineData("", StartBookImportError.InvalidBookName)]
    [InlineData("   ", StartBookImportError.InvalidBookName)]
    public async Task Start_rejects_a_missing_book_name(string name, StartBookImportError expected)
    {
        (await StartAs(41, Pdf(name))).Error.ShouldBe(expected);
        await AssertNothingStartedAsync();
    }

    [Fact]
    public async Task Start_rejects_an_unknown_grade()
    {
        (await StartAs(41, Pdf(gradeId: 9999))).Error.ShouldBe(StartBookImportError.InvalidGrade);
        await AssertNothingStartedAsync();
    }

    [Fact]
    public async Task Start_rejects_a_file_that_is_not_a_pdf_whatever_its_name()
    {
        (await StartAs(41, Pdf(content: "PK\u0003\u0004 zip"))).Error.ShouldBe(StartBookImportError.NotPdf);
        await AssertNothingStartedAsync();
    }

    [Fact]
    public async Task Start_rejects_a_file_above_the_configured_limit()
    {
        (await StartAs(41, Pdf(), maxUploadBytes: 4)).Error.ShouldBe(StartBookImportError.FileTooLarge);
        await AssertNothingStartedAsync();
    }

    [Fact]
    public async Task Start_is_blocked_when_the_plan_limit_is_reached()
    {
        var resets = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);
        _entitlements = Substitute.For<IEntitlementService>();
        _entitlements.CheckAsync(41, false, EntitlementFeatures.BookImport, Arg.Any<CancellationToken>())
            .Returns(new EntitlementCheck(false, 5, 5, resets));

        var result = await StartAs(41, Pdf());

        result.Error.ShouldBe(StartBookImportError.QuotaReached);
        result.Quota.ShouldBe(new EntitlementCheck(false, 5, 5, resets));
        await AssertNothingStartedAsync();
    }

    [Fact]
    public async Task List_and_get_show_only_the_callers_jobs_but_admin_sees_all()
    {
        var mine = (await StartAs(41, Pdf("Benim"))).Job!.Id;
        var theirs = (await StartAs(42, Pdf("Başkası"))).Job!.Id;

        await using var ctx = _db.NewContext();
        var service = NewService(ctx);
        var owner = new BookImportOwner(41, false);
        (await service.ListAsync(owner, 50, default)).Select(j => j.Id).ShouldBe(new[] { mine });
        (await service.GetAsync(owner, theirs, default)).ShouldBeNull();
        (await service.GetAsync(owner, mine, default))!.BookName.ShouldBe("Benim");

        var admin = new BookImportOwner(1, true);
        (await service.ListAsync(admin, 50, default)).Select(j => j.Id).ShouldBe(new[] { mine, theirs }, ignoreOrder: true);
        (await service.GetAsync(admin, theirs, default)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Get_returns_tests_in_book_order_with_flags()
    {
        var id = (await StartAs(41, Pdf())).Job!.Id;
        await using (var seed = _db.NewContext())
        {
            seed.BookImportJobTests.AddRange(
                new BookImportJobTest { JobId = id, Order = 2, Unit = 1, TestNo = 2, Status = BookImportTestStatus.NeedsReview, FlagsJson = "[\"missingNumbers\"]" },
                new BookImportJobTest { JobId = id, Order = 1, Unit = 1, TestNo = 1, Status = BookImportTestStatus.Done });
            await seed.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        var detail = await NewService(ctx).GetAsync(new BookImportOwner(41, false), id, default);

        detail!.Tests.Select(t => t.TestNo).ShouldBe(new[] { 1, 2 });
        detail.Tests[1].Status.ShouldBe("NeedsReview");
        detail.Tests[1].Flags.ShouldBe(new[] { "missingNumbers" });
    }

    [Fact]
    public async Task Runner_walks_the_stub_stages_and_waits_for_setup_confirmation()
    {
        var id = (await StartAs(41, Pdf())).Job!.Id;
        var guard = Substitute.For<IApprovedTeacherGuard>();
        guard.CheckAsync(41, Arg.Any<CancellationToken>()).Returns(TeacherApprovalCheck.NoTeacherProfile);

        await using (var ctx = _db.NewContext())
            await new BookImportJobRunner(ctx, guard).RunLockedAsync(id);

        await using var check = _db.NewContext();
        var job = await check.BookImportJobs.SingleAsync();
        job.Status.ShouldBe(BookImportJobStatus.AwaitingConfirmation);
        job.Stage.ShouldBe(BookImportStage.SetupConfirmation);
        job.StartedAt.ShouldNotBeNull();
        job.Error.ShouldBeNull();
    }

    [Theory]
    [InlineData(TeacherApprovalCheck.NotApproved)]
    [InlineData(TeacherApprovalCheck.Suspended)]
    public async Task Runner_fails_the_job_when_the_owners_teacher_account_is_not_approved(TeacherApprovalCheck check)
    {
        var id = (await StartAs(41, Pdf())).Job!.Id;
        var guard = Substitute.For<IApprovedTeacherGuard>();
        guard.CheckAsync(41, Arg.Any<CancellationToken>()).Returns(check);

        await using (var ctx = _db.NewContext())
            await new BookImportJobRunner(ctx, guard).RunLockedAsync(id);

        await using var verify = _db.NewContext();
        var job = await verify.BookImportJobs.SingleAsync();
        job.Status.ShouldBe(BookImportJobStatus.Failed);
        job.Error.ShouldBe(BookImportJobRunner.OwnerNotApprovedMessage);
    }

    [Fact]
    public async Task Runner_does_not_restart_a_job_that_already_left_the_queue()
    {
        var id = (await StartAs(41, Pdf())).Job!.Id;
        await using (var seed = _db.NewContext())
        {
            var job = await seed.BookImportJobs.SingleAsync();
            job.Status = BookImportJobStatus.Cancelled;
            await seed.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
            await new BookImportJobRunner(ctx, Substitute.For<IApprovedTeacherGuard>()).RunLockedAsync(id);

        await using var check = _db.NewContext();
        (await check.BookImportJobs.SingleAsync()).Status.ShouldBe(BookImportJobStatus.Cancelled);
    }

    private async Task AssertNothingStartedAsync()
    {
        await using var check = _db.NewContext();
        (await check.BookImportJobs.CountAsync()).ShouldBe(0);
        await _minio.DidNotReceiveWithAnyArgs().UploadFileAsync(default!, default!);
        _jobs.DidNotReceiveWithAnyArgs().Create(default!, default!);
    }

    public void Dispose() => _db.Dispose();
}
