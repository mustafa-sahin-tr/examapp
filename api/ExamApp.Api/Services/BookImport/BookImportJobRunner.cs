using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services.Labeling;
using ExamApp.Api.Services.Teachers;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.BookImport;

/// <summary>
/// Issue #1018 (epic #1017): kitaptan aktarım işini aşama aşama yürütür. Sayfalar (#1020) PDF'i sayfa görsellerine çevirir; kitap
/// kurulumu (#1021, <see cref="BookSetupStage"/>) testleri ve cevap anahtarını çıkarır, iş kurulum onayında bekler. Onaydan sonra
/// iş yeniden kuyruğa girer; soru aktarımı (#1022, <see cref="BookQuestionImportStage"/>) testleri taslak soru olarak kaydeder ve
/// iş incelemeye geçer. Emsal <c>QuestionTransferJobRunner</c>:
/// kendi Hangfire kuyruğu, iş başına dağıtık kilit, sahibin öğretmen onayı iş başında yeniden kontrol edilir. Claude çağrıları
/// <see cref="BookImportBudget"/>'tan geçer (#1024): bütçe dolunca iş "Bütçe bekleniyor"a geçip pencere açılınca yeniden kuyruğa
/// girer, maliyet tavanı aşılınca Admin'in sürdürmesini bekler. İptal (#1025) aşama, test ve sayfa arasında denetlenir; yeniden
/// çalıştırma bitmiş sayfa ve testleri atlar (yarıda kalan testin taslakları kırpma hash'iyle yeniden kullanılır).
/// </summary>
public class BookImportJobRunner
{
    public const string Queue = "book-import";
    public const string OwnerNotApprovedMessage = "Job owner's teacher account is not approved (or is suspended); job was not run.";

    /// <summary>Varsayılan sayfa sınırı; <c>BookImport:MaxPages</c> ile değişir.</summary>
    public const int DefaultMaxPages = 600;

    /// <summary>Kurulum taramasında tek Claude çağrısına giden sayfa; <c>BookImport:SurveyBatchSize</c>.</summary>
    public const int DefaultSurveyBatchSize = 8;

    /// <summary>Soru / şık tespit modeli (işaretleme ekranının "Claude ile tespit"i); <c>BookImport:DetectModel</c>.</summary>
    public const string DefaultDetectModel = "claude-haiku-5-5";

    /// <summary>Bütçe penceresinin kalan süresi bilinmiyorsa yeniden deneme aralığı.</summary>
    public static readonly TimeSpan DefaultBudgetRetry = TimeSpan.FromMinutes(30);

    private readonly AppDbContext _db;
    private readonly IApprovedTeacherGuard _teacherGuard;
    private readonly IMinIoService _minio;
    private readonly IPdfRasterizer _rasterizer;
    private readonly IBookSetupAi _setupAi;
    private readonly ILabelingService _labeling;
    private readonly IServiceScopeFactory _scopes;
    private readonly int _maxPages;
    private readonly int _surveyBatchSize;
    private readonly string _detectModel;
    private readonly decimal? _maxJobCostUsd;
    private readonly IFixedWindowCounterStore _budgetStore;
    private readonly LabelingAiOptions _aiOptions;
    private readonly IBackgroundJobClient? _jobs;
    private readonly TimeSpan _demoStep;
    private readonly ILogger<BookImportJobRunner> _logger;

    public BookImportJobRunner(AppDbContext db, IApprovedTeacherGuard teacherGuard, IMinIoService minio, IPdfRasterizer rasterizer,
        IBookSetupAi setupAi, ILabelingService labeling, IServiceScopeFactory scopes, IConfiguration? configuration = null,
        ILogger<BookImportJobRunner>? logger = null, IFixedWindowCounterStore? budgetStore = null,
        IOptions<LabelingAiOptions>? aiOptions = null, IBackgroundJobClient? jobs = null)
    {
        _db = db;
        _teacherGuard = teacherGuard;
        _minio = minio;
        _rasterizer = rasterizer;
        _setupAi = setupAi;
        _labeling = labeling;
        _scopes = scopes;
        _maxPages = Math.Max(1, configuration?.GetValue<int?>("BookImport:MaxPages") ?? DefaultMaxPages);
        _surveyBatchSize = Math.Clamp(configuration?.GetValue<int?>("BookImport:SurveyBatchSize") ?? DefaultSurveyBatchSize, 1, 20);
        _detectModel = configuration?["BookImport:DetectModel"] is { Length: > 0 } model ? model : DefaultDetectModel;
        _maxJobCostUsd = MaxJobCost(configuration);
        _demoStep = TimeSpan.FromMilliseconds(Math.Max(0, configuration?.GetValue<int?>("BookImport:DemoStepMilliseconds") ?? BookImportDemoReplay.DefaultStepMilliseconds));
        _budgetStore = budgetStore ?? new InMemoryFixedWindowCounterStore();
        _aiOptions = aiOptions?.Value ?? new LabelingAiOptions();
        _jobs = jobs;
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
        if (job is null || job.Status is not (BookImportJobStatus.Queued or BookImportJobStatus.Running or BookImportJobStatus.AwaitingBudget))
            return;

        if (await OwnerNotApprovedAsync(job))
        {
            Fail(job, OwnerNotApprovedMessage);
            await _db.SaveChangesAsync();
            return;
        }

        // Issue #1032: demo hiçbir AI aşamasına girmez; kuyruğa nereden düşerse düşsün (onay, yeniden deneme) burada ayrılır.
        if (job.DemoSourceJobId is not null)
        {
            await new BookImportDemoReplay(_db, _demoStep, _logger).RunAsync(job);
            return;
        }

        job.Status = BookImportJobStatus.Running;
        job.StartedAt ??= DateTime.UtcNow;
        job.Error = null;
        job.ResumeAt = null;
        await _db.SaveChangesAsync();

        var budget = new BookImportBudget(_db, _budgetStore, _aiOptions, job, job.CostCeilingUsd ?? _maxJobCostUsd);

        try
        {
            if (job.Stage < BookImportStage.SetupConfirmation)
                await RenderPagesAsync(job);
            // #1144 karışık sorular: kurulum ve onay yok, test satırları yüklemede açıldı; sayfalardan sonra sorulara geçer.
            if (job.Stage < BookImportStage.SetupConfirmation && job.Source != BookImportSource.LooseImages)
            {
                await EnterStageAsync(job, BookImportStage.Setup);
                await new BookSetupStage(_db, _minio, _setupAi, budget, _surveyBatchSize, _logger).RunAsync(job);
                await BookQuestionImportStage.StopIfCancelledAsync(_db, job.Id, default);

                // Kurulum onayı: kullanıcı onaylayana kadar iş bekler; onay ucu (BookImportService.ConfirmSetupAsync) işi
                // Questions aşamasında yeniden kuyruğa verir.
                job.Stage = BookImportStage.SetupConfirmation;
                job.Status = BookImportJobStatus.AwaitingConfirmation;
                job.StageTotal = 0;
                job.StageProcessed = 0;
                await _db.SaveChangesAsync();
                return;
            }

            // #1022: test test soru aktarımı; kaldığı testten devam eder (aşama sayaçları sıfırlanmaz).
            if (job.Stage != BookImportStage.Questions)
                await EnterStageAsync(job, BookImportStage.Questions);
            await new BookQuestionImportStage(_db, _minio, _labeling, _scopes, budget, _detectModel, _logger).RunAsync(job);
            await BookQuestionImportStage.StopIfCancelledAsync(_db, job.Id, default);
            job.Stage = BookImportStage.Review;
            job.Status = BookImportJobStatus.ReadyForReview;
            await _db.SaveChangesAsync();
            await LogSummaryAsync(job);
        }
        catch (BookImportCancelled)
        {
            // #1025: iptal ucu durumu yazdı; iş burada durur (kaydedilmiş taslaklar iptal seçeneğine göre durur / silinir).
            await budget.FlushAsync();
            _logger.LogInformation("Book import job {JobId} stopped in stage {Stage}: cancelled", job.Id, job.Stage);
        }
        catch (BookImportPause pause)
        {
            await budget.FlushAsync();
            if (!await CancelledAsync(job.Id))
                await PauseAsync(job, pause);
        }
        catch (Exception failure) when (failure is BookImportFailure or PdfRasterizeException)
        {
            _logger.LogWarning("Book import job {JobId} failed in stage {Stage}: {Reason}", job.Id, job.Stage, failure.Message);
            await FailUnlessCancelledAsync(job, failure.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Book import job {JobId} failed in stage {Stage}", job.Id, job.Stage);
            await FailUnlessCancelledAsync(job, ex.Message);
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

        if (BookImportService.IsImageSource(job.Source))
        {
            await NormalizeImagesAsync(job);
            return;
        }

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
            await BookQuestionImportStage.StopIfCancelledAsync(_db, job.Id, default);
        }

        _logger.LogInformation("Book import job {JobId}: rendered {Pages} pages in {Seconds:F1}s", job.Id, pageCount - next + 1,
            (DateTime.UtcNow - started).TotalSeconds);
    }

    /// <summary>
    /// Issue #1144: sayfa görselleri (klasörden yüklenen) PDF sayfalarıyla aynı biçime çevrilir: EXIF yönü uygulanır, genişlik
    /// <see cref="PdfiumRasterizer.DefaultPageWidth"/> (oran korunur), WebP kalite <see cref="PdfiumRasterizer.DefaultWebpQuality"/>.
    /// Kaldığı sayfadan devam eder; hepsi bitince yüklenen kaynak görseller silinir (sayfalar kalır).
    /// </summary>
    private async Task NormalizeImagesAsync(BookImportJob job)
    {
        var pageCount = job.PageCount ?? 0;
        if (pageCount < 1 || string.IsNullOrEmpty(job.FileUrl))
            throw new BookImportFailure("The uploaded page images are missing.");
        if (pageCount > _maxPages)
            throw new BookImportFailure($"The book has {pageCount} page images; the limit is {_maxPages}.");

        var done = await _db.BookImportPages.Where(p => p.JobId == job.Id).Select(p => p.FilePage).ToListAsync();
        var next = done.Count == 0 ? 1 : done.Max() + 1;
        job.StageTotal = pageCount;
        job.StageProcessed = next - 1;
        await _db.SaveChangesAsync();

        var started = DateTime.UtcNow;
        for (var filePage = next; filePage <= pageCount; filePage++)
        {
            byte[] source;
            await using (var stream = await _minio.GetFileStreamAsync(job.FileUrl + BookImportService.PageImageFileName(filePage))
                                      ?? throw new BookImportFailure($"Page image {filePage} could not be read from storage."))
            {
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer);
                source = buffer.ToArray();
            }

            var page = NormalizePageImage(source, filePage);
            using var image = new MemoryStream(page.Webp, writable: false);
            var url = await _minio.UploadFileAsync(image, PageObjectName(job.Id, filePage), contentType: "image/webp");
            _db.BookImportPages.Add(new BookImportPage
            {
                JobId = job.Id,
                FilePage = filePage,
                ImageUrl = url,
                Width = page.Width,
                Height = page.Height,
                SizeBytes = page.Webp.LongLength,
            });
            job.StageProcessed = filePage;
            await _db.SaveChangesAsync();
            await BookQuestionImportStage.StopIfCancelledAsync(_db, job.Id, default);
        }

        for (var filePage = 1; filePage <= pageCount; filePage++)
            await _minio.DeleteFileByUrlAsync(job.FileUrl + BookImportService.PageImageFileName(filePage));

        _logger.LogInformation("Book import job {JobId}: normalized {Pages} page images in {Seconds:F1}s", job.Id, pageCount - next + 1,
            (DateTime.UtcNow - started).TotalSeconds);
    }

    internal static (byte[] Webp, int Width, int Height) NormalizePageImage(byte[] source, int filePage)
    {
        SkiaSharp.SKBitmap decoded;
        try
        {
            decoded = SkiaImaging.DecodeOriented(source, out _);
        }
        catch (Exception ex) when (ex is ImageDecodeException or ImageTooLargeException or NotSupportedException)
        {
            throw new BookImportFailure($"Page image {filePage} could not be decoded.");
        }

        using (decoded)
        {
            var width = PdfiumRasterizer.DefaultPageWidth;
            var height = Math.Max(1, (int)Math.Round((double)decoded.Height * width / decoded.Width));
            if (!SkiaImaging.IsWithinLimits(width, height))
                throw new BookImportFailure($"Page image {filePage} is too tall for a page.");
            using var page = SkiaImaging.CropResize(decoded, new SkiaSharp.SKRectI(0, 0, decoded.Width, decoded.Height), width, height);
            return (SkiaImaging.Encode(page, SkiaSharp.SKEncodedImageFormat.Webp, PdfiumRasterizer.DefaultWebpQuality), width, height);
        }
    }

    /// <summary>
    /// Issue #1024: bütçe doluysa iş pencere açılınca yeniden kuyruğa girer (bitmemiş test baştan; tespit önbelleği tekrar ödetmez),
    /// tavan aşıldıysa Admin'in sürdürmesini bekler (<see cref="BookImportService.ContinueAsync"/>).
    /// </summary>
    private async Task PauseAsync(BookImportJob job, BookImportPause pause)
    {
        if (pause.Reason == BookImportPauseReason.Budget)
        {
            var delay = (pause.RetryAfter ?? DefaultBudgetRetry) + TimeSpan.FromMinutes(1);
            job.Status = BookImportJobStatus.AwaitingBudget;
            job.ResumeAt = DateTime.UtcNow + delay;
            await _db.SaveChangesAsync();
            _jobs?.Schedule<BookImportJobRunner>(r => r.RunAsync(job.Id), delay);
        }
        else
        {
            job.Status = BookImportJobStatus.AwaitingCostApproval;
            await _db.SaveChangesAsync();
        }
        _logger.LogWarning("Book import job {JobId} paused in stage {Stage}: {Reason}; cost ${Cost:F4}, resume at {ResumeAt}",
            job.Id, job.Stage, pause.Reason, job.AiCostUsd, job.ResumeAt);
    }

    /// <summary>Issue #1024: bitmiş işin tek satırlık özeti (sayfa, test, soru, işaretli test, maliyet).</summary>
    private async Task LogSummaryAsync(BookImportJob job)
    {
        var tests = await _db.BookImportJobTests.AsNoTracking().Where(t => t.JobId == job.Id)
            .Select(t => new { t.Status, t.QuestionsSaved }).ToListAsync();
        _logger.LogInformation(
            "Book import job {JobId} finished: {Pages} pages, {Tests} tests, {Questions} questions, {Flagged} flagged tests, {FailedTests} failed tests, cost ${Cost:F4}",
            job.Id, job.PageCount ?? 0, tests.Count, tests.Sum(t => t.QuestionsSaved),
            tests.Count(t => t.Status == BookImportTestStatus.NeedsReview), tests.Count(t => t.Status == BookImportTestStatus.Failed),
            job.AiCostUsd);
    }

    /// <summary>Issue #1024: <c>BookImport:MaxJobCostUsd</c>; boş ya da 0 → tavan yok.</summary>
    internal static decimal? MaxJobCost(IConfiguration? configuration) =>
        configuration?.GetValue<decimal?>("BookImport:MaxJobCostUsd") is > 0 and var max ? max : null;

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

    private Task<bool> CancelledAsync(Guid jobId) =>
        _db.BookImportJobs.AnyAsync(j => j.Id == jobId && j.Status == BookImportJobStatus.Cancelled);

    /// <summary>İptal edilmiş iş hataya çevrilmez (iptal ucu ile çalışma yarışı, #1025).</summary>
    private async Task FailUnlessCancelledAsync(BookImportJob job, string error)
    {
        if (await CancelledAsync(job.Id))
            return;
        Fail(job, error);
        await _db.SaveChangesAsync();
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

/// <summary>Issue #1025: iş çalışırken iptal edildi; çalışma test / sayfa arasında durur.</summary>
public sealed class BookImportCancelled() : Exception("The book import was cancelled.");
