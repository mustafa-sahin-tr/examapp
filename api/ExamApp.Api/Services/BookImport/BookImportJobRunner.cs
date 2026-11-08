using System;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Teachers;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Api.Services.BookImport;

/// <summary>
/// Issue #1018 (epic #1017): kitaptan aktarım işini aşama aşama yürütür. Bu dilimde aşamalar iskelet: sayfa görselleri (#1020) ve
/// kitap kurulumu (#1021) henüz iş yapmadan geçilir ve iş kurulum onayında bekler. Emsal <c>QuestionTransferJobRunner</c>:
/// kendi Hangfire kuyruğu, iş başına dağıtık kilit, sahibin öğretmen onayı iş başında yeniden kontrol edilir.
/// </summary>
public class BookImportJobRunner
{
    public const string Queue = "book-import";
    public const string OwnerNotApprovedMessage = "Job owner's teacher account is not approved (or is suspended); job was not run.";

    private readonly AppDbContext _db;
    private readonly IApprovedTeacherGuard _teacherGuard;
    private readonly ILogger<BookImportJobRunner> _logger;

    public BookImportJobRunner(AppDbContext db, IApprovedTeacherGuard teacherGuard, ILogger<BookImportJobRunner>? logger = null)
    {
        _db = db;
        _teacherGuard = teacherGuard;
        _logger = logger ?? NullLogger<BookImportJobRunner>.Instance;
    }

    [Queue(Queue)]
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(Guid jobId)
    {
        // Aynı iş iki kez kuyruğa düşerse (yeniden başlatma, elle tetikleme) ikinci çalıştırma beklemeden çıkar.
        using var connection = JobStorage.Current.GetConnection();
        using var jobLock = connection.AcquireDistributedLock($"book-import:{jobId}", TimeSpan.FromSeconds(5));
        await RunLockedAsync(jobId);
    }

    /// <summary>Kilit altında çalışan gövde; birim testler doğrudan bunu çağırır (Hangfire depolaması gerekmez).</summary>
    internal async Task RunLockedAsync(Guid jobId)
    {
        var job = await _db.BookImportJobs.FirstOrDefaultAsync(j => j.Id == jobId);
        if (job is null || job.Status is not (BookImportJobStatus.Queued or BookImportJobStatus.Running))
            return;

        if (await OwnerNotApprovedAsync(job))
        {
            Fail(job, OwnerNotApprovedMessage);
            await _db.SaveChangesAsync();
            return;
        }

        job.Status = BookImportJobStatus.Running;
        job.StartedAt ??= DateTime.UtcNow;
        job.Error = null;
        await _db.SaveChangesAsync();

        try
        {
            // #1020: PDF → sayfa görselleri. İskelet: iş yapmadan geçilir.
            await EnterStageAsync(job, BookImportStage.Pages);

            // #1021: AI ile kitap kurulumu. İskelet: iş yapmadan geçilir.
            await EnterStageAsync(job, BookImportStage.Setup);

            // Kurulum onayı: kullanıcı onaylayana kadar iş bekler (#1021 onay ucu soruları başlatır).
            job.Stage = BookImportStage.SetupConfirmation;
            job.Status = BookImportJobStatus.AwaitingConfirmation;
            job.StageTotal = 0;
            job.StageProcessed = 0;
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Book import job {JobId} failed in stage {Stage}", job.Id, job.Stage);
            Fail(job, ex.Message);
            await _db.SaveChangesAsync();
        }
    }

    private async Task EnterStageAsync(BookImportJob job, BookImportStage stage)
    {
        job.Stage = stage;
        job.StageTotal = 0;
        job.StageProcessed = 0;
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Sahibin Teacher kaydı varsa onaylı olmalı (ApprovedTeacher policy'siyle aynı kural: Teacher rolü olmayan, ör. yalnız
    /// QuestionTrainer, muaf). Onay iş kuyruğa girdikten sonra kaldırılmış olabilir (emsal #289).
    /// </summary>
    private async Task<bool> OwnerNotApprovedAsync(BookImportJob job)
    {
        if (job.CreateUserId is not > 0)
            return false;
        var check = await _teacherGuard.CheckAsync(job.CreateUserId.Value);
        return check is TeacherApprovalCheck.NotApproved or TeacherApprovalCheck.Suspended;
    }

    private static void Fail(BookImportJob job, string error)
    {
        job.Status = BookImportJobStatus.Failed;
        job.Error = error.Length <= BookImportJob.MaxMessageLength ? error : error[..BookImportJob.MaxMessageLength];
        job.FinishedAt = DateTime.UtcNow;
    }
}
