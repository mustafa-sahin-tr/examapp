using System.IO.Compression;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.QuestionTransfer;
using ExamApp.Api.Services.Teachers;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #289 (security D2): aktarım işi kuyruğa girdikten sonra sahibi askıya alınırsa / onayı yoksa iş başında
/// soru yazmadan Failed ile sonlanır. Admin sahipli iş ve sahibi bilinmeyen eski iş (CreateUserId 0) muaftır.
/// </summary>
public class QuestionTransferJobRunnerOwnerTests : IDisposable
{
    private const int ApprovedOwner = 11;
    private const int SuspendedOwner = 12;
    private const int PendingOwner = 13;

    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();

    public QuestionTransferJobRunnerOwnerTests()
    {
        using var ctx = _db.NewContext();
        ctx.Teachers.AddRange(
            new Teacher { UserId = ApprovedOwner, AccountApprovedAt = DateTime.UtcNow },
            new Teacher
            {
                UserId = SuspendedOwner, AccountApprovedAt = null, AccountSuspendedAt = DateTime.UtcNow,
                AccountSuspensionReason = "neden"
            },
            new Teacher { UserId = PendingOwner, ApprovalStatus = TeacherApprovalStatus.Pending });
        ctx.SaveChanges();

        // Boş manifest'li geçerli bir import zip'i: iş çalışırsa soru eklemeden Completed olur.
        _minio.GetFileStreamAsync(Arg.Any<string>()).Returns(_ => Task.FromResult<Stream?>(EmptyImportZip()));
    }

    public void Dispose() => _db.Dispose();

    private static Stream EmptyImportZip()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var s = zip.CreateEntry("manifest.json").Open();
            JsonSerializer.Serialize(s, new { sourceKey = "src", questions = Array.Empty<object>() });
        }
        ms.Position = 0;
        return ms;
    }

    private async Task<Guid> SeedJobAsync(int ownerUserId, QuestionTransferJobKind kind = QuestionTransferJobKind.Import)
    {
        await using var ctx = _db.NewContext();
        ctx.SetCurrentUser(ownerUserId);
        var job = new QuestionTransferJob
        {
            Id = Guid.NewGuid(), Kind = kind, SourceKey = "src", FileUrl = "http://minio/in.zip", Message = "Queued",
            RequestJson = kind == QuestionTransferJobKind.Export ? "{\"QuestionIds\":[1]}" : null
        };
        ctx.Add(job);
        await ctx.SaveChangesAsync();
        return job.Id;
    }

    private async Task<QuestionTransferJob> RunImportAsync(Guid jobId, bool ownerIsAdmin)
    {
        await using (var ctx = _db.NewContext())
            await new QuestionTransferJobRunner(ctx, _minio, new ApprovedTeacherGuard(ctx)).RunImportAsync(jobId, ownerIsAdmin);
        await using var check = _db.NewContext();
        return await check.Set<QuestionTransferJob>().AsNoTracking().SingleAsync(j => j.Id == jobId);
    }

    [Theory]
    [InlineData(SuspendedOwner)]
    [InlineData(PendingOwner)]
    [InlineData(4242)] // öğretmen kaydı yok
    public async Task Import_of_a_non_approved_teacher_owner_fails_without_touching_storage_or_questions(int owner)
    {
        var jobId = await SeedJobAsync(owner);

        var job = await RunImportAsync(jobId, ownerIsAdmin: false);

        job.Status.ShouldBe(QuestionTransferJobStatus.Failed);
        job.Message.ShouldBe(QuestionTransferJobRunner.OwnerNotApprovedMessage);
        await _minio.DidNotReceiveWithAnyArgs().GetFileStreamAsync(default!);
        await using var ctx = _db.NewContext();
        (await ctx.Questions.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Export_of_a_suspended_owner_fails_before_running()
    {
        var jobId = await SeedJobAsync(SuspendedOwner, QuestionTransferJobKind.Export);

        await using (var ctx = _db.NewContext())
            await new QuestionTransferJobRunner(ctx, _minio, new ApprovedTeacherGuard(ctx)).RunExportAsync(jobId, ownerIsAdmin: false);

        await using var check = _db.NewContext();
        var job = await check.Set<QuestionTransferJob>().AsNoTracking().SingleAsync(j => j.Id == jobId);
        job.Status.ShouldBe(QuestionTransferJobStatus.Failed);
        job.Message.ShouldBe(QuestionTransferJobRunner.OwnerNotApprovedMessage);
        (await check.Set<QuestionTransferExportBundle>().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Approved_owner_runs_the_job()
    {
        var job = await RunImportAsync(await SeedJobAsync(ApprovedOwner), ownerIsAdmin: false);

        job.Status.ShouldBe(QuestionTransferJobStatus.Completed);
        await _minio.Received(1).GetFileStreamAsync("http://minio/in.zip");
    }

    [Fact]
    public async Task Admin_owned_job_is_exempt_even_if_the_admin_has_a_suspended_teacher_row()
    {
        var job = await RunImportAsync(await SeedJobAsync(SuspendedOwner), ownerIsAdmin: true);

        job.Status.ShouldBe(QuestionTransferJobStatus.Completed);
    }

    [Fact]
    public async Task Legacy_job_without_owner_is_not_checked()
    {
        var jobId = await SeedJobAsync(0);

        await using (var ctx = _db.NewContext())
            await new QuestionTransferJobRunner(ctx, _minio, new ApprovedTeacherGuard(ctx)).RunImportAsync(jobId); // eski imza

        await using var check = _db.NewContext();
        (await check.Set<QuestionTransferJob>().AsNoTracking().SingleAsync(j => j.Id == jobId))
            .Status.ShouldBe(QuestionTransferJobStatus.Completed);
    }
}
