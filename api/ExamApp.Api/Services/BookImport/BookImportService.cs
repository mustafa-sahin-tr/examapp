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

/// <summary>Issue #1018: işin sahibi (exam kullanıcı id'si ve hakla ilgili rolleri); Admin tüm işleri görür.</summary>
public sealed record BookImportOwner(EntitlementSubject Subject)
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
}

public sealed record StartBookImportResult(BookImportJobDto? Job, StartBookImportError Error, EntitlementCheck? Quota = null);

public interface IBookImportService
{
    Task<StartBookImportResult> StartAsync(BookImportOwner owner, StartBookImportRequest request, CancellationToken ct);
    Task<List<BookImportJobDto>> ListAsync(BookImportOwner owner, int take, CancellationToken ct);
    Task<BookImportJobDetailDto?> GetAsync(BookImportOwner owner, Guid id, CancellationToken ct);
    Task<BookImportQuotaDto> GetQuotaAsync(BookImportOwner owner, CancellationToken ct);

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
    Task<ConfirmSetupResult> ConfirmSetupAsync(BookImportOwner owner, Guid id, ConfirmBookImportSetupDto request, CancellationToken ct);

    /// <summary>Onay bekleyen işi iptal eder (#1021 "anahtar yok → iptal"); diğer durumlar #1025.</summary>
    Task<CancelBookImportResult> CancelAsync(BookImportOwner owner, Guid id, CancellationToken ct);
}

/// <summary>
/// Issue #1018 (epic #1017): PDF'i MinIO'ya (özel, <c>book-imports/</c>) koyar, işi açar ve Hangfire kuyruğuna verir
/// (<see cref="BookImportJobRunner"/>). Kullanıcı yalnız kendi işlerini görür; Admin hepsini.
/// </summary>
public sealed partial class BookImportService : IBookImportService
{
    /// <summary>Formdaki sınır; <c>BookImport:MaxUploadBytes</c> bunu düşürebilir, yükseltemez (istek sınırı controller'da).</summary>
    public const long MaxUploadBytesCeiling = 200_000_000;

    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    private readonly AppDbContext _db;
    private readonly IMinIoService _minio;
    private readonly IBackgroundJobClient _jobs;
    private readonly IEntitlementService _entitlements;
    private readonly IBookRegistryService _books;
    private readonly long _maxUploadBytes;

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
    }

    public async Task<StartBookImportResult> StartAsync(BookImportOwner owner, StartBookImportRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(request);

        var bookName = request.BookName?.Trim() ?? string.Empty;
        if (bookName.Length == 0 || bookName.Length > BookImportJob.MaxBookNameLength)
            return new(null, StartBookImportError.InvalidBookName);
        if (!await _db.Grades.AnyAsync(g => g.Id == request.GradeId, ct))
            return new(null, StartBookImportError.InvalidGrade);
        if (request.SubjectId is { } subjectId && !await _db.Subjects.AnyAsync(s => s.Id == subjectId, ct))
            return new(null, StartBookImportError.InvalidSubject);
        if (request.FileSizeBytes <= 0 || request.FileSizeBytes > _maxUploadBytes)
            return new(null, StartBookImportError.FileTooLarge);

        var quota = await _entitlements.CheckAsync(owner.Subject, EntitlementFeatures.BookImport, ct);
        if (!quota.Allowed)
            return new(null, StartBookImportError.QuotaReached, quota);

        // İçerik türüne güvenilmez: dosya %PDF- ile başlamalı.
        var head = new byte[PdfMagic.Length];
        var read = await request.Content.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, ct);
        if (read < head.Length || !head.AsSpan().SequenceEqual(PdfMagic))
            return new(null, StartBookImportError.NotPdf);

        var job = new BookImportJob
        {
            Id = Guid.NewGuid(),
            BookName = bookName,
            GradeId = request.GradeId,
            SubjectId = request.SubjectId,
            FileName = TrimFileName(request.FileName),
            FileSizeBytes = request.FileSizeBytes,
            Status = BookImportJobStatus.Queued,
            Stage = BookImportStage.Upload,
        };

        // Form dosyası aranabilir (ASP.NET büyük dosyayı diske tamponlar): başa dönüp olduğu gibi yüklenir, belleğe alınmaz.
        if (!request.Content.CanSeek)
            throw new InvalidOperationException("Upload stream must be seekable.");
        request.Content.Position = 0;
        job.FileUrl = await _minio.UploadFileAsync(request.Content, $"book-imports/{job.Id}/source.pdf", contentType: "application/pdf");

        _db.SetCurrentUser(owner.UserId);
        _db.BookImportJobs.Add(job);
        await _db.SaveChangesAsync(ct);

        _jobs.Enqueue<BookImportJobRunner>(r => r.RunAsync(job.Id));
        return new(ToDto(job), StartBookImportError.None);
    }

    public async Task<List<BookImportJobDto>> ListAsync(BookImportOwner owner, int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, 200);
        var jobs = await Visible(owner)
            .OrderByDescending(j => j.CreateTime)
            .Take(take)
            .AsNoTracking()
            .ToListAsync(ct);
        return jobs.Select(ToDto).ToList();
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
        detail.Tests = tests.Select(t => new BookImportJobTestDto
        {
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

    private IQueryable<BookImportJob> Visible(BookImportOwner owner) =>
        owner.IsAdmin ? _db.BookImportJobs : _db.BookImportJobs.Where(j => j.CreateUserId == owner.UserId);

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
        dto.BookName = job.BookName;
        dto.GradeId = job.GradeId;
        dto.SubjectId = job.SubjectId;
        dto.BookId = job.BookId;
        dto.Status = job.Status.ToString();
        dto.Stage = job.Stage.ToString();
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
    }
}
