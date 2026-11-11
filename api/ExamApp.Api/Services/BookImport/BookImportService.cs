using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Labeling;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ExamApp.Api.Services.BookImport;

/// <summary>
/// Issue #1018: işin sahibi (exam kullanıcı id'si ve hakla ilgili rolleri); Admin tüm işleri görür. #1052: <paramref name="Kind"/>
/// görülen işlerin türü — kitap aktarımı uçları yalnız kitap işlerini, hızlı ekleme uçları yalnız hızlı eklemeleri görür.
/// </summary>
public sealed record BookImportOwner(EntitlementSubject Subject, BookImportJobKind Kind = BookImportJobKind.Book)
{
    public int UserId => Subject.UserId;
    public bool IsAdmin => Subject.IsAdmin;

    public static BookImportOwner Create(int userId, params string[] roles) => new(EntitlementSubject.Create(userId, roles));
}

/// <summary>Yüklenen PDF ve formdaki kitap bilgisi.</summary>
public sealed record StartBookImportRequest(string BookName, int GradeId, int? SubjectId, string? FileName, long FileSizeBytes, Stream Content);

public enum StartBookImportError
{
    None,
    InvalidBookName,
    InvalidGrade,
    InvalidSubject,
    NotPdf,
    FileTooLarge,
    QuotaReached,
    /// <summary>Issue #1144: görsel sayısı 1 ile sayfa sınırı arasında değil.</summary>
    TooManyPages,
}

public sealed record StartBookImportResult(BookImportJobDto? Job, StartBookImportError Error, EntitlementCheck? Quota = null);

/// <summary>Issue #1024: tavanda bekleyen işi sürdürme sonucu.</summary>
public enum ContinueBookImportResult
{
    Continued,
    NotFound,
    InvalidState,
}

public interface IBookImportService
{
    Task<StartBookImportResult> StartAsync(BookImportOwner owner, StartBookImportRequest request, CancellationToken ct);
    Task<List<BookImportJobDto>> ListAsync(BookImportOwner owner, int take, CancellationToken ct);
    Task<BookImportJobDetailDto?> GetAsync(BookImportOwner owner, Guid id, CancellationToken ct);
    Task<BookImportQuotaDto> GetQuotaAsync(BookImportOwner owner, CancellationToken ct);

    /// <summary>Issue #1122: parçalı yükleme (Cloudflare 100 MB istek sınırı).</summary>
    Task<BeginBookImportUploadResult> BeginUploadAsync(BookImportOwner owner, BeginBookImportUploadRequest request, CancellationToken ct);
    Task<BookImportUploadPartResult> UploadPartAsync(BookImportOwner owner, Guid id, int partNumber, Stream content, CancellationToken ct);
    Task<CompleteBookImportUploadResult> CompleteUploadAsync(BookImportOwner owner, Guid id, CancellationToken ct);
    Task<bool> AbortUploadAsync(BookImportOwner owner, Guid id, CancellationToken ct);

    /// <summary>Issue #1122: süresi geçen yarım yüklemeleri ve parçalarını siler (Hangfire recurring).</summary>
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    Task<int> PurgeStaleUploadsAsync(CancellationToken ct = default);

    /// <summary>İşin bir sayfa görseli (#1020); iş görünmüyorsa ya da sayfa yoksa null.</summary>
    Task<Stream?> GetPageImageAsync(BookImportOwner owner, Guid id, int filePage, CancellationToken ct);

    /// <summary>
    /// Kitaba bağlı en son aktarımın sayfa görseli (#1020): işaretleme ekranı <c>bookId</c> + <c>filePage</c> ile okur. Kitap
    /// kaydı işaretleme rolleri için ortak olduğundan sahiplik aranmaz.
    /// </summary>
    Task<Stream?> GetBookPageImageAsync(int bookId, int filePage, CancellationToken ct);

    /// <summary>Kurulum onay ekranı (#1021); iş görünmüyorsa ya da kurulum henüz yoksa null.</summary>
    Task<BookImportSetupDto?> GetSetupAsync(BookImportOwner owner, Guid id, CancellationToken ct);

    /// <summary>Kurulumu onaylar (#1021): hak düşer, kitap kaydı yazılır, iş soru aktarımı için kuyruğa girer.</summary>
    Task<BookImportDemoDto> GetDemoAsync(BookImportOwner owner, CancellationToken ct);

    Task<StartBookImportDemoResult> StartDemoAsync(BookImportOwner owner, CancellationToken ct);

    Task<SetBookImportDemoSourceResult> SetDemoSourceAsync(Guid id, bool isDemoSource, CancellationToken ct);

    Task<ConfirmSetupResult> ConfirmSetupAsync(BookImportOwner owner, Guid id, ConfirmBookImportSetupDto request, CancellationToken ct);

    /// <summary>
    /// Issue #1024: Admin — maliyet tavanında bekleyen işi sürdürür; tavan şimdiki maliyet + <c>BookImport:MaxJobCostUsd</c> olur
    /// (yapılandırma boşsa tavan kalkar) ve iş yeniden kuyruğa girer.
    /// </summary>
    Task<ContinueBookImportResult> ContinueAsync(BookImportOwner owner, Guid id, CancellationToken ct);

    /// <summary>
    /// İşi iptal eder (#1021 onay bekleyen; #1025 kuyrukta, çalışan, bekleyen ya da hatalı): çalışma test / sayfa arasında durur.
    /// Taslaklar kalır (incelenip onaylanabilir) ya da <paramref name="deleteDrafts"/> ile onaylanmamışlar silinir.
    /// </summary>
    Task<CancelBookImportResult> CancelAsync(BookImportOwner owner, Guid id, bool deleteDrafts, CancellationToken ct);

    /// <summary>Issue #1025: hatalı ya da takılmış işi kaldığı yerden yeniden kuyruğa verir.</summary>
    Task<RetryBookImportResult> RetryAsync(BookImportOwner owner, Guid id, CancellationToken ct);

    /// <summary>Issue #1025: hatalı testi (<paramref name="testId"/>) ya da bütün hatalı testleri yeniden aktarır.</summary>
    Task<RetryBookImportResult> RetryTestsAsync(BookImportOwner owner, Guid id, int? testId, CancellationToken ct);

    /// <summary>Issue #1023: incelemedeki testin soruları (numara, sayfa, cevap); test yoksa null.</summary>
    Task<List<BookImportReviewQuestionDto>?> GetReviewQuestionsAsync(BookImportOwner owner, Guid id, int testId, CancellationToken ct);

    /// <summary>Issue #1023: taslak sorunun doğru şıkkını seç (cevapsız sorular için).</summary>
    Task<BookImportReviewResult> SetAnswerAsync(BookImportOwner owner, Guid id, int testId, int questionId, int optionIndex, CancellationToken ct);

    /// <summary>Issue #1023: testi onayla — taslak test ve soruları etkinleşir; cevapsız soru varsa engellenir.</summary>
    Task<BookImportReviewResult> ApproveTestAsync(BookImportOwner owner, Guid id, int testId, CancellationToken ct);

    /// <summary>Issue #1023: onaylanabilen bütün testleri onayla; engellenenler nedeniyle döner. İş yoksa null.</summary>
    Task<ApproveAllBookImportResultDto?> ApproveAllAsync(BookImportOwner owner, Guid id, CancellationToken ct,
        string? collectTestName = null);

    /// <summary>Issue #1023: testi sil — yalnız o testin taslak testi ve taslak soruları silinir.</summary>
    Task<BookImportReviewResult> RemoveTestAsync(BookImportOwner owner, Guid id, int testId, CancellationToken ct);

    /// <summary>
    /// İncelemedeki onaylanmamış testi baştan aktarır: taslakları silinir, test yeniden kuyruğa girer (sayfa tespitleri
    /// önbellekten gelir). Ör. numaralama düzeltmesinden sonra kayık cevaplı testi yeniden üretmek için.
    /// </summary>
    Task<RetryBookImportResult> ReimportTestAsync(BookImportOwner owner, Guid id, int testId, CancellationToken ct);
}

/// <summary>
/// Issue #1018 (epic #1017): PDF'i MinIO'ya (özel, <c>book-imports/</c>) koyar, işi açar ve Hangfire kuyruğuna verir
/// (<see cref="BookImportJobRunner"/>). Kullanıcı yalnız kendi işlerini görür; Admin hepsini.
/// </summary>
public sealed partial class BookImportService : IBookImportService
{
    /// <summary>Formdaki sınır; <c>BookImport:MaxUploadBytes</c> bunu düşürebilir, yükseltemez (istek sınırı controller'da).</summary>
    public const long MaxUploadBytesCeiling = 200_000_000;

    /// <summary>Issue #1025: bu süreden uzun ilerlemeyen kuyruktaki / çalışan iş takılmış sayılır; <c>BookImport:StallMinutes</c>.</summary>
    public const int DefaultStallMinutes = 15;

    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    private readonly AppDbContext _db;
    private readonly IMinIoService _minio;
    private readonly IBackgroundJobClient _jobs;
    private readonly IEntitlementService _entitlements;
    private readonly IBookRegistryService _books;
    private readonly long _maxUploadBytes;
    private readonly decimal? _maxJobCostUsd;
    private readonly TimeSpan _stallAfter;

    public BookImportService(AppDbContext db, IMinIoService minio, IBackgroundJobClient jobs, IEntitlementService entitlements,
        IBookRegistryService books, IConfiguration configuration)
    {
        _db = db;
        _minio = minio;
        _jobs = jobs;
        _entitlements = entitlements;
        _books = books;
        var configured = configuration.GetValue<long?>("BookImport:MaxUploadBytes") ?? MaxUploadBytesCeiling;
        _maxUploadBytes = Math.Clamp(configured, 1, MaxUploadBytesCeiling);
        _maxJobCostUsd = BookImportJobRunner.MaxJobCost(configuration);
        _demoMaxRuns = Math.Max(0, configuration.GetValue<int?>("BookImport:DemoMaxRuns") ?? DefaultDemoMaxRuns);
        _uploadBucket = configuration["MinioConfig:BucketName"] ?? string.Empty;
        _maxPages = Math.Max(1, configuration.GetValue<int?>("BookImport:MaxPages") ?? BookImportJobRunner.DefaultMaxPages);
        _stallAfter = TimeSpan.FromMinutes(Math.Max(1, configuration.GetValue<int?>("BookImport:StallMinutes") ?? DefaultStallMinutes));
    }

    public async Task<StartBookImportResult> StartAsync(BookImportOwner owner, StartBookImportRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(request);

        var error = await ValidateUploadAsync(request.BookName, request.GradeId, request.SubjectId, request.FileSizeBytes, ct);
        if (error != StartBookImportError.None)
            return new(null, error);

        var quota = await _entitlements.CheckAsync(owner.Subject, EntitlementFeatures.BookImport, ct);
        if (!quota.Allowed)
            return new(null, StartBookImportError.QuotaReached, quota);

        // İçerik türüne güvenilmez: dosya %PDF- ile başlamalı.
        if (!await StartsWithPdfMagicAsync(request.Content, ct))
            return new(null, StartBookImportError.NotPdf);

        // Form dosyası aranabilir (ASP.NET büyük dosyayı diske tamponlar): başa dönüp olduğu gibi yüklenir, belleğe alınmaz.
        if (!request.Content.CanSeek)
            throw new InvalidOperationException("Upload stream must be seekable.");
        request.Content.Position = 0;
        var job = await CreateJobAsync(owner, request.BookName, request.GradeId, request.SubjectId, request.FileName,
            request.FileSizeBytes, request.Content, ct);
        return new(ToDto(job), StartBookImportError.None);
    }

    /// <summary>Formdaki kitap bilgisi ve dosya boyutu; tek istekli ve parçalı yükleme (#1122) aynı kuralları uygular.</summary>
    private async Task<StartBookImportError> ValidateUploadAsync(string? bookName, int gradeId, int? subjectId, long fileSizeBytes,
        CancellationToken ct)
    {
        var name = bookName?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > BookImportJob.MaxBookNameLength)
            return StartBookImportError.InvalidBookName;
        if (!await _db.Grades.AnyAsync(g => g.Id == gradeId, ct))
            return StartBookImportError.InvalidGrade;
        if (subjectId is { } id && !await _db.Subjects.AnyAsync(s => s.Id == id, ct))
            return StartBookImportError.InvalidSubject;
        if (fileSizeBytes <= 0 || fileSizeBytes > _maxUploadBytes)
            return StartBookImportError.FileTooLarge;
        return StartBookImportError.None;
    }

    private static async Task<bool> StartsWithPdfMagicAsync(Stream content, CancellationToken ct)
    {
        var head = new byte[PdfMagic.Length];
        var read = await content.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, ct);
        return read == head.Length && head.AsSpan().SequenceEqual(PdfMagic);
    }

    /// <summary>PDF'i işin kaynağı olarak yükler, işi kaydeder ve kuyruğa alır. <paramref name="content"/> başta olmalı.</summary>
    private async Task<BookImportJob> CreateJobAsync(BookImportOwner owner, string bookName, int gradeId, int? subjectId, string? fileName,
        long fileSizeBytes, Stream content, CancellationToken ct)
    {
        var job = new BookImportJob
        {
            Id = Guid.NewGuid(),
            BookName = bookName.Trim(),
            GradeId = gradeId,
            SubjectId = subjectId,
            FileName = TrimFileName(fileName),
            FileSizeBytes = fileSizeBytes,
            Status = BookImportJobStatus.Queued,
            Stage = BookImportStage.Upload,
        };
        job.FileUrl = await _minio.UploadFileAsync(content, $"book-imports/{job.Id}/source.pdf", contentType: "application/pdf");

        _db.SetCurrentUser(owner.UserId);
        _db.BookImportJobs.Add(job);
        await _db.SaveChangesAsync(ct);

        _jobs.Enqueue<BookImportJobRunner>(r => r.RunAsync(job.Id));
        return job;
    }

    public async Task<List<BookImportJobDto>> ListAsync(BookImportOwner owner, int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, 200);
        var jobs = await Visible(owner)
            .OrderByDescending(j => j.CreateTime)
            .Take(take)
            .AsNoTracking()
            .ToListAsync(ct);
        return jobs.Select(job =>
        {
            var dto = ToDto(job);
            dto.Stalled = IsStalled(job);
            if (owner.IsAdmin)
                FillCost(dto, job);
            return dto;
        }).ToList();
    }

    public async Task<BookImportJobDetailDto?> GetAsync(BookImportOwner owner, Guid id, CancellationToken ct)
    {
        var job = await Visible(owner).AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null)
            return null;

        var tests = await _db.BookImportJobTests.AsNoTracking()
            .Where(t => t.JobId == id)
            .OrderBy(t => t.Order)
            .ToListAsync(ct);

        var detail = new BookImportJobDetailDto();
        Fill(detail, job);
        detail.Stalled = IsStalled(job);
        var testCosts = new Dictionary<int, decimal>();
        if (owner.IsAdmin)
        {
            FillCost(detail, job);
            var costs = await _db.BookImportCosts.AsNoTracking().Where(c => c.JobId == id)
                .GroupBy(c => new { c.Stage, c.TestId, c.ModelId })
                .Select(g => new
                {
                    g.Key.Stage, g.Key.TestId, g.Key.ModelId, Calls = g.Count(), Input = g.Sum(c => c.InputTokens),
                    Output = g.Sum(c => c.OutputTokens), Usd = g.Sum(c => c.CostUsd),
                })
                .ToListAsync(ct);
            detail.Costs = costs.GroupBy(c => new { c.Stage, c.ModelId }).OrderBy(g => g.Key.Stage).ThenBy(g => g.Key.ModelId)
                .Select(g => new BookImportCostDto
                {
                    Stage = g.Key.Stage.ToString(), ModelId = g.Key.ModelId, Calls = g.Sum(c => c.Calls),
                    InputTokens = g.Sum(c => c.Input), OutputTokens = g.Sum(c => c.Output), CostUsd = g.Sum(c => c.Usd),
                }).ToList();
            testCosts = costs.Where(c => c.TestId is not null).GroupBy(c => c.TestId!.Value).ToDictionary(g => g.Key, g => g.Sum(c => c.Usd));
        }
        var answerless = await AnswerlessCountsAsync(tests.Where(t => t.WorksheetId is not null).Select(t => t.WorksheetId!.Value).ToList(), ct);
        detail.Tests = tests.Select(t => new BookImportJobTestDto
        {
            Id = t.Id,
            Order = t.Order,
            Unit = t.Unit,
            TestNo = t.TestNo,
            Title = t.Title,
            Status = t.Status.ToString(),
            QuestionsFound = t.QuestionsFound,
            KeyAnswers = t.KeyAnswers,
            Flags = ParseFlags(t.FlagsJson),
            QuestionsSaved = t.QuestionsSaved,
            DuplicatesSkipped = t.DuplicatesSkipped,
            ReviewFlags = BookImportReviewFlag.Parse(t.ReviewFlagsJson),
            WorksheetId = t.WorksheetId,
            FileFrom = t.FileFrom,
            FileTo = t.FileTo,
            Answerless = t.WorksheetId is { } w ? answerless.GetValueOrDefault(w) : 0,
            CostUsd = owner.IsAdmin ? testCosts.GetValueOrDefault(t.Id) : null,
            Error = t.Error,
        }).ToList();
        return detail;
    }

    public async Task<Stream?> GetPageImageAsync(BookImportOwner owner, Guid id, int filePage, CancellationToken ct)
    {
        var url = await Visible(owner).Where(j => j.Id == id)
            .SelectMany(j => j.Pages.Where(p => p.FilePage == filePage).Select(p => p.ImageUrl))
            .FirstOrDefaultAsync(ct);
        return url is null ? null : await _minio.GetFileStreamAsync(url);
    }

    public async Task<Stream?> GetBookPageImageAsync(int bookId, int filePage, CancellationToken ct)
    {
        var url = await _db.BookImportPages.AsNoTracking()
            .Where(p => p.Job.BookId == bookId && p.FilePage == filePage)
            .OrderByDescending(p => p.Job.CreateTime)
            .Select(p => p.ImageUrl)
            .FirstOrDefaultAsync(ct);
        return url is null ? null : await _minio.GetFileStreamAsync(url);
    }

    public async Task<BookImportQuotaDto> GetQuotaAsync(BookImportOwner owner, CancellationToken ct)
    {
        var check = await _entitlements.CheckAsync(owner.Subject, EntitlementFeatures.BookImport, ct);
        return new BookImportQuotaDto
        {
            Allowed = check.Allowed, Limit = check.Limit, Used = check.Used, ResetsAtUtc = check.ResetsAtUtc, PlanKey = check.PlanKey,
        };
    }

    public async Task<ContinueBookImportResult> ContinueAsync(BookImportOwner owner, Guid id, CancellationToken ct)
    {
        var job = owner.IsAdmin ? await _db.BookImportJobs.AsTracking().FirstOrDefaultAsync(j => j.Id == id, ct) : null;
        if (job is null)
            return ContinueBookImportResult.NotFound;
        if (job.Status != BookImportJobStatus.AwaitingCostApproval)
            return ContinueBookImportResult.InvalidState;

        job.CostCeilingUsd = _maxJobCostUsd is { } step ? job.AiCostUsd + step : null;
        job.Status = BookImportJobStatus.Queued;
        await _db.SaveChangesAsync(ct);
        _jobs.Enqueue<BookImportJobRunner>(r => r.RunAsync(id));
        return ContinueBookImportResult.Continued;
    }

    private IQueryable<BookImportJob> Visible(BookImportOwner owner) =>
        owner.IsAdmin
            ? _db.BookImportJobs.Where(j => j.Kind == owner.Kind)
            : _db.BookImportJobs.Where(j => j.Kind == owner.Kind && j.CreateUserId == owner.UserId);

    private static string? TrimFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        var file = Path.GetFileName(name.Trim());
        return file.Length <= BookImportJob.MaxFileNameLength ? file : file[..BookImportJob.MaxFileNameLength];
    }

    private static List<string> ParseFlags(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    internal static BookImportJobDto ToDto(BookImportJob job)
    {
        var dto = new BookImportJobDto();
        Fill(dto, job);
        return dto;
    }

    private static void Fill(BookImportJobDto dto, BookImportJob job)
    {
        dto.Id = job.Id;
        dto.Kind = job.Kind.ToString();
        dto.Source = job.Source.ToString();
        dto.BookName = job.BookName;
        dto.GradeId = job.GradeId;
        dto.SubjectId = job.SubjectId;
        dto.BookId = job.BookId;
        dto.Status = job.Status.ToString();
        dto.Stage = job.Stage.ToString();
        dto.IsDemo = job.DemoSourceJobId is not null;
        dto.IsDemoSource = job.IsDemoSource;
        dto.FileName = job.FileName;
        dto.FileSizeBytes = job.FileSizeBytes;
        dto.PageCount = job.PageCount;
        dto.StageTotal = job.StageTotal;
        dto.StageProcessed = job.StageProcessed;
        dto.Message = job.Message;
        dto.Error = job.Error;
        dto.CreatedAt = job.CreateTime;
        dto.StartedAt = job.StartedAt;
        dto.FinishedAt = job.FinishedAt;
        dto.OwnerId = job.CreateUserId;
        dto.ResumeAt = job.ResumeAt;
    }

    private void FillCost(BookImportJobDto dto, BookImportJob job)
    {
        dto.AiCostUsd = job.AiCostUsd;
        dto.CostCeilingUsd = job.CostCeilingUsd ?? _maxJobCostUsd;
    }
}
