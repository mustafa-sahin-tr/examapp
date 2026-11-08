using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.BookImport;

public enum StartBookImportDemoError
{
    None,
    Unavailable,
    LimitReached,
}

public sealed record StartBookImportDemoResult(BookImportJobDto? Job, StartBookImportDemoError Error = StartBookImportDemoError.None);

public enum SetBookImportDemoSourceResult
{
    Ok,
    NotFound,
    NotEligible,
}

/// <summary>
/// Issue #1032: örnek kitapla demo. Admin'in seçtiği hazır işin ilk testinden en çok 5 soru, gerçek takip ve inceleme
/// ekranlarında oynatılır. Claude çağrısı ve <c>book-import</c> hakkı yoktur; sayı sınırı <c>BookImport:DemoMaxRuns</c>.
/// </summary>
public sealed partial class BookImportService
{
    public const int DefaultDemoMaxRuns = 3;

    private readonly int _demoMaxRuns;

    private static readonly BookImportJobStatus[] ActiveDemoStatuses =
    [
        BookImportJobStatus.Queued, BookImportJobStatus.Running, BookImportJobStatus.AwaitingConfirmation,
        BookImportJobStatus.ReadyForReview,
    ];

    public async Task<BookImportDemoDto> GetDemoAsync(BookImportOwner owner, CancellationToken ct)
    {
        var source = await DemoSourceAsync(ct);
        var used = await DemoRunsAsync(owner, ct);
        var activeId = await ActiveDemo(owner).Select(j => (Guid?)j.Id).FirstOrDefaultAsync(ct);
        return new BookImportDemoDto
        {
            Available = activeId is not null || (source is not null && used < _demoMaxRuns),
            BookName = source?.BookName,
            Used = used,
            Limit = _demoMaxRuns,
            ActiveJobId = activeId,
        };
    }

    public async Task<StartBookImportDemoResult> StartDemoAsync(BookImportOwner owner, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var active = await ActiveDemo(owner).AsNoTracking().FirstOrDefaultAsync(ct);
        if (active is not null)
            return new(ToDto(active));

        var source = await DemoSourceAsync(ct);
        var sourceTest = source is null ? null : await BookImportDemoReplay.SourceTestAsync(_db, source.Id, ct);
        if (source is null || sourceTest is null)
            return new(null, StartBookImportDemoError.Unavailable);
        if (await DemoRunsAsync(owner, ct) >= _demoMaxRuns)
            return new(null, StartBookImportDemoError.LimitReached);

        var count = Math.Min(await BookImportDemoReplay.SourceQuestionIds(_db, sourceTest.WorksheetId!.Value).CountAsync(ct), BookImportDemoReplay.MaxQuestions);
        var key = sourceTest.AnswerKey is { } answerKey && answerKey.Length >= count ? answerKey[..count] : null;
        var job = new BookImportJob
        {
            Id = Guid.NewGuid(),
            BookName = source.BookName,
            GradeId = source.GradeId,
            SubjectId = source.SubjectId,
            FileName = source.FileName,
            FileSizeBytes = source.FileSizeBytes,
            PageCount = source.PageCount,
            SetupJson = source.SetupJson,
            DemoSourceJobId = source.Id,
            Status = BookImportJobStatus.Queued,
            Stage = BookImportStage.Upload,
        };
        job.Tests.Add(new BookImportJobTest
        {
            Order = 1,
            Unit = sourceTest.Unit,
            TestNo = sourceTest.TestNo,
            Title = sourceTest.Title,
            FileFrom = sourceTest.FileFrom,
            FileTo = sourceTest.FileTo,
            Minutes = sourceTest.Minutes,
            ExpectedQuestions = count,
            AnswerKey = key,
            KeyAnswers = key?.Length ?? 0,
        });
        // Görseller paylaşılır, dosya kopyası yok: öğretmen yalnız testin ve cevap anahtarının sayfalarını açabilir.
        var keyRanges = BookSetupSummary.Parse(source.SetupJson)?.AnswerKeyRanges ?? [];
        var pages = await _db.BookImportPages.AsNoTracking().Where(p => p.JobId == source.Id).ToListAsync(ct);
        foreach (var page in pages.Where(p =>
                     (p.FilePage >= sourceTest.FileFrom && p.FilePage <= sourceTest.FileTo)
                     || keyRanges.Any(r => p.FilePage >= r.FileFrom && p.FilePage <= r.FileTo)))
        {
            job.Pages.Add(new BookImportPage
            {
                FilePage = page.FilePage,
                ImageUrl = page.ImageUrl,
                Width = page.Width,
                Height = page.Height,
                SizeBytes = page.SizeBytes,
            });
        }

        _db.SetCurrentUser(owner.UserId);
        _db.BookImportJobs.Add(job);
        await _db.SaveChangesAsync(ct);

        _jobs.Enqueue<BookImportJobRunner>(r => r.RunAsync(job.Id));
        return new(ToDto(job));
    }

    public async Task<SetBookImportDemoSourceResult> SetDemoSourceAsync(Guid id, bool isDemoSource, CancellationToken ct)
    {
        var job = await _db.BookImportJobs.AsTracking().FirstOrDefaultAsync(j => j.Id == id && j.Kind == BookImportJobKind.Book, ct);
        if (job is null)
            return SetBookImportDemoSourceResult.NotFound;
        if (!isDemoSource)
        {
            job.IsDemoSource = false;
            await _db.SaveChangesAsync(ct);
            return SetBookImportDemoSourceResult.Ok;
        }

        if (job.DemoSourceJobId is not null || await BookImportDemoReplay.SourceTestAsync(_db, id, ct) is null)
            return SetBookImportDemoSourceResult.NotEligible;

        // Önce eski seçim kalkar: filtreli tekil dizin iki kaynağa izin vermez.
        await _db.BookImportJobs.Where(j => j.IsDemoSource && j.Id != id)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.IsDemoSource, false), ct);
        job.IsDemoSource = true;
        await _db.SaveChangesAsync(ct);
        return SetBookImportDemoSourceResult.Ok;
    }

    /// <summary>Demoda kurulum düzenlenmez ve kitap kaydı yazılmaz: onay yalnız soru aşamasını başlatır.</summary>
    private async Task<ConfirmSetupResult> ConfirmDemoSetupAsync(Guid id, CancellationToken ct)
    {
        var claimed = await _db.BookImportJobs
            .Where(j => j.Id == id && j.Status == BookImportJobStatus.AwaitingConfirmation && j.Stage == BookImportStage.SetupConfirmation)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, BookImportJobStatus.Queued)
                .SetProperty(j => j.Stage, BookImportStage.Questions), ct);
        if (claimed == 0)
            return new(ConfirmSetupError.InvalidState);

        _jobs.Enqueue<BookImportJobRunner>(r => r.RunAsync(id));
        var job = await _db.BookImportJobs.AsNoTracking().FirstAsync(j => j.Id == id, ct);
        return new(ConfirmSetupError.None, ToDto(job));
    }

    private Task<BookImportJob?> DemoSourceAsync(CancellationToken ct) =>
        _db.BookImportJobs.AsNoTracking().FirstOrDefaultAsync(j => j.IsDemoSource && j.Kind == BookImportJobKind.Book, ct);

    /// <summary>Silinen demolar da sayılır: sınır silerek aşılmasın. Bizden kaynaklı başarısızlık hak yemez.</summary>
    private Task<int> DemoRunsAsync(BookImportOwner owner, CancellationToken ct) =>
        _db.BookImportJobs.IgnoreQueryFilters().CountAsync(
            j => j.DemoSourceJobId != null && j.CreateUserId == owner.UserId && j.Status != BookImportJobStatus.Failed, ct);

    private IQueryable<BookImportJob> ActiveDemo(BookImportOwner owner) =>
        _db.BookImportJobs.Where(j => j.DemoSourceJobId != null && j.CreateUserId == owner.UserId && ActiveDemoStatuses.Contains(j.Status))
            .OrderByDescending(j => j.CreateTime);
}
