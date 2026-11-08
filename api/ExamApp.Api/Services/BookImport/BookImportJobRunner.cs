using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Teachers;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Api.Services.BookImport;

/// <summary>
/// Issue #1018 (epic #1017): kitaptan aktarım işini aşama aşama yürütür. Sayfalar (#1020) PDF'i sayfa görsellerine çevirir; kitap
/// kurulumu (#1021, <see cref="BookSetupStage"/>) testleri ve cevap anahtarını çıkarır, iş kurulum onayında bekler. Onaydan sonra
/// iş yeniden kuyruğa girer; soru aktarımı (#1022) henüz iskelet. Emsal <c>QuestionTransferJobRunner</c>:
/// kendi Hangfire kuyruğu, iş başına dağıtık kilit, sahibin öğretmen onayı iş başında yeniden kontrol edilir.
/// </summary>
public class BookImportJobRunner
{
    public const string Queue = "book-import";
    public const string OwnerNotApprovedMessage = "Job owner's teacher account is not approved (or is suspended); job was not run.";

    /// <summary>Varsayılan sayfa sınırı; <c>BookImport:MaxPages</c> ile değişir.</summary>
    public const int DefaultMaxPages = 400;

    /// <summary>Kurulum taramasında tek Claude çağrısına giden sayfa; <c>BookImport:SurveyBatchSize</c>.</summary>
    public const int DefaultSurveyBatchSize = 8;

    private readonly AppDbContext _db;
    private readonly IApprovedTeacherGuard _teacherGuard;
    private readonly IMinIoService _minio;
    private readonly IPdfRasterizer _rasterizer;
    private readonly IBookSetupAi _setupAi;
    private readonly int _maxPages;
    private readonly int _surveyBatchSize;
    private readonly ILogger<BookImportJobRunner> _logger;

    public BookImportJobRunner(AppDbContext db, IApprovedTeacherGuard teacherGuard, IMinIoService minio, IPdfRasterizer rasterizer,
        IBookSetupAi setupAi, IConfiguration? configuration = null, ILogger<BookImportJobRunner>? logger = null)
    {
        _db = db;
        _teacherGuard = teacherGuard;
        _minio = minio;
        _rasterizer = rasterizer;
        _setupAi = setupAi;
        _maxPages = Math.Max(1, configuration?.GetValue<int?>("BookImport:MaxPages") ?? DefaultMaxPages);
        _surveyBatchSize = Math.Clamp(configuration?.GetValue<int?>("BookImport:SurveyBatchSize") ?? DefaultSurveyBatchSize, 1, 20);
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
            if (job.Stage < BookImportStage.SetupConfirmation)
            {
                await RenderPagesAsync(job);

                await EnterStageAsync(job, BookImportStage.Setup);
                await new BookSetupStage(_db, _minio, _setupAi, _surveyBatchSize, _logger).RunAsync(job);

                // Kurulum onayı: kullanıcı onaylayana kadar iş bekler; onay ucu (BookImportService.ConfirmSetupAsync) işi
                // Questions aşamasında yeniden kuyruğa verir.
                job.Stage = BookImportStage.SetupConfirmation;
                job.Status = BookImportJobStatus.AwaitingConfirmation;
                job.StageTotal = 0;
                job.StageProcessed = 0;
                await _db.SaveChangesAsync();
                return;
            }

            // #1022: test test soru aktarımı. İskelet: iş yapmadan inceleme aşamasına geçilir.
            await EnterStageAsync(job, BookImportStage.Questions);
            job.Stage = BookImportStage.Review;
            job.Status = BookImportJobStatus.ReadyForReview;
            await _db.SaveChangesAsync();
        }
        catch (Exception failure) when (failure is BookImportFailure or PdfRasterizeException)
        {
            _logger.LogWarning("Book import job {JobId} failed in stage {Stage}: {Reason}", job.Id, job.Stage, failure.Message);
            Fail(job, failure.Message);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Book import job {JobId} failed in stage {Stage}", job.Id, job.Stage);
            Fail(job, ex.Message);
            await _db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Issue #1020: PDF'i indirir, sayfa sayısını ve sınırı denetler, sayfaları tek tek WebP olarak MinIO'ya yazar. Yarıda kalan
    /// iş kaydı olan sayfaları atlayarak devam eder (#1025 yeniden dene).
    /// </summary>
    private async Task RenderPagesAsync(BookImportJob job)
    {
        if (job.Stage != BookImportStage.Pages)
            await EnterStageAsync(job, BookImportStage.Pages);

        if (string.IsNullOrEmpty(job.FileUrl))
            throw new BookImportFailure("The uploaded PDF is missing.");
        byte[] pdf;
        await using (var source = await _minio.GetFileStreamAsync(job.FileUrl)
                                  ?? throw new BookImportFailure("The uploaded PDF could not be read from storage."))
        {
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer);
            pdf = buffer.ToArray();
        }

        var pageCount = _rasterizer.GetPageCount(pdf);
        if (pageCount < 1)
            throw new BookImportFailure("The PDF has no pages.");
        if (pageCount > _maxPages)
            throw new BookImportFailure($"The PDF has {pageCount} pages; the limit is {_maxPages}.");

        var done = await _db.BookImportPages.Where(p => p.JobId == job.Id).Select(p => p.FilePage).ToListAsync();
        var next = done.Count == 0 ? 1 : done.Max() + 1;
        job.PageCount = pageCount;
        job.StageTotal = pageCount;
        job.StageProcessed = next - 1;
        await _db.SaveChangesAsync();

        var started = DateTime.UtcNow;
        foreach (var page in _rasterizer.Render(pdf, next, pageCount))
        {
            using var image = new MemoryStream(page.Image, writable: false);
            var url = await _minio.UploadFileAsync(image, PageObjectName(job.Id, page.FilePage), contentType: "image/webp");
            _db.BookImportPages.Add(new BookImportPage
            {
                JobId = job.Id,
                FilePage = page.FilePage,
                ImageUrl = url,
                Width = page.Width,
                Height = page.Height,
                SizeBytes = page.Image.LongLength,
            });
            job.StageProcessed = page.FilePage;
            await _db.SaveChangesAsync();
        }

        _logger.LogInformation("Book import job {JobId}: rendered {Pages} pages in {Seconds:F1}s", job.Id, pageCount - next + 1,
            (DateTime.UtcNow - started).TotalSeconds);
    }

    public static string PageObjectName(Guid jobId, int filePage) => $"book-imports/{jobId}/pages/{filePage:D4}.webp";

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

/// <summary>İşi okunur bir hatayla düşüren beklenen durum (bozuk / şifreli / çok uzun PDF vb.); yığın izi loglanmaz.</summary>
public sealed class BookImportFailure(string message) : Exception(message);
