using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Labeling;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Labeling;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.BookImport;

public enum ConfirmSetupError
{
    None,
    NotFound,
    InvalidState,
    Invalid,
    NameTaken,
    QuotaReached,
}

/// <param name="Code">Geçersiz istekte yerelleştirme anahtarı (<c>bookImport.*</c> ya da kitap kaydının <c>labeling.books.*</c>).</param>
public sealed record ConfirmSetupResult(
    ConfirmSetupError Error,
    BookImportJobDto? Job = null,
    string? Code = null,
    BookImportBookRefDto? ExistingBook = null,
    EntitlementCheck? Quota = null);

public enum CancelBookImportResult
{
    Cancelled,
    NotFound,
    InvalidState,
}

/// <summary>Issue #1021: kurulum onay ekranı, onay ve onayda iptal.</summary>
public sealed partial class BookImportService
{
    private const int MaxMinutes = 600;

    [GeneratedRegex("^[A-E]+$")]
    private static partial Regex AnswerKeyPattern();

    public async Task<BookImportSetupDto?> GetSetupAsync(BookImportOwner owner, Guid id, CancellationToken ct)
    {
        var job = await Visible(owner).AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null || BookSetupSummary.Parse(job.SetupJson) is not { } summary)
            return null;

        var tests = await _db.BookImportJobTests.AsNoTracking().Where(t => t.JobId == id).OrderBy(t => t.Order).ToListAsync(ct);
        var topicIds = summary.Units.Where(u => u.TopicId is not null).Select(u => u.TopicId!.Value).ToList();
        var topicNames = await _db.Topics.AsNoTracking().Where(t => topicIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        return new BookImportSetupDto
        {
            JobId = job.Id,
            BookName = job.BookName,
            PageCount = job.PageCount,
            Offset = summary.Offset,
            AnswerKeyRanges = summary.AnswerKeyRanges.Select(r => new BookImportPageRangeDto(r.FileFrom, r.FileTo)).ToList(),
            SelfAssessmentPages = summary.SelfAssessmentPages,
            Units = summary.Units.Select(u => new BookImportSetupUnitDto
            {
                Unit = u.Unit, Title = u.Title, FileFrom = u.FileFrom, FileTo = u.FileTo, TopicId = u.TopicId,
                TopicName = u.TopicId is { } topicId ? topicNames.GetValueOrDefault(topicId) : null,
            }).ToList(),
            OrphanKeys = summary.OrphanKeys,
            NoAnswerKey = summary.NoAnswerKey,
            AiCostUsd = job.AiCostUsd,
            SchemaRevision = summary.SchemaRevision,
            ExistingBook = job.BookId is null ? await FindBookByNameAsync(job.BookName, ct) : null,
            Tests = tests.Select(t => new BookImportSetupTestDto
            {
                Order = t.Order, Unit = t.Unit, TestNo = t.TestNo, Title = t.Title, Minutes = t.Minutes, FileFrom = t.FileFrom,
                FileTo = t.FileTo, ExpectedQuestions = t.ExpectedQuestions, AnswerKey = t.AnswerKey, KeyConflicts = t.KeyConflicts,
                Flags = ParseFlags(t.FlagsJson),
            }).ToList(),
        };
    }

    public async Task<ConfirmSetupResult> ConfirmSetupAsync(BookImportOwner owner, Guid id, ConfirmBookImportSetupDto request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(request);

        var job = await Visible(owner).AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null)
            return new(ConfirmSetupError.NotFound);
        if (!IsAwaitingSetup(job) || BookSetupSummary.Parse(job.SetupJson) is not { } summary)
            return new(ConfirmSetupError.InvalidState);

        var jobTests = await _db.BookImportJobTests.AsNoTracking().Where(t => t.JobId == id).ToDictionaryAsync(t => t.Order, ct);
        var bookName = request.BookName?.Trim() ?? string.Empty;
        if (Validate(request, bookName, jobTests, job.PageCount ?? 0) is { } invalid)
            return new(ConfirmSetupError.Invalid, Code: invalid);

        BookImportBookRefDto? reuse = null;
        if (request.ExistingBookId is { } existingId)
        {
            reuse = await _db.Books.AsNoTracking().Where(b => b.Id == existingId)
                .Select(b => new BookImportBookRefDto(b.Id, b.Name)).FirstOrDefaultAsync(ct);
            if (reuse is null)
                return new(ConfirmSetupError.Invalid, Code: "bookImport.bookNotFound");
        }
        else if (await FindBookByNameAsync(bookName, ct) is { } taken)
        {
            return new(ConfirmSetupError.NameTaken, ExistingBook: taken);
        }

        var quota = await _entitlements.CheckAsync(owner.Subject, EntitlementFeatures.BookImport, ct);
        if (!quota.Allowed)
            return new(ConfirmSetupError.QuotaReached, Quota: quota);

        // İki eşzamanlı onaydan yalnız biri geçer: durum koşullu güncellenir.
        var claimed = await _db.BookImportJobs
            .Where(j => j.Id == id && j.Status == BookImportJobStatus.AwaitingConfirmation && j.Stage == BookImportStage.SetupConfirmation)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, BookImportJobStatus.Running), ct);
        if (claimed == 0)
            return new(ConfirmSetupError.InvalidState);

        try
        {
            var bookId = await WriteBookAsync(owner, job, summary, bookName, reuse, ct);
            var updated = await ApplyConfirmationAsync(owner, id, bookId, bookName, request, ct);
            _jobs.Enqueue<BookImportJobRunner>(r => r.RunAsync(id));
            return new(ConfirmSetupError.None, ToDto(updated));
        }
        catch (BookRegistryConflictException)
        {
            await ReleaseClaimAsync(id);
            return new(ConfirmSetupError.NameTaken, ExistingBook: await FindBookByNameAsync(bookName, ct));
        }
        catch (LabelingValidationException validation)
        {
            await ReleaseClaimAsync(id);
            return new(ConfirmSetupError.Invalid, Code: validation.Message);
        }
        catch
        {
            await ReleaseClaimAsync(id);
            throw;
        }
    }

    /// <summary>Onay yarıda kaldı: iş yeniden onaylanabilsin (kitap yazıldıysa yeniden onayda "mevcut kitabı kullan" çıkar).</summary>
    private Task ReleaseClaimAsync(Guid id) =>
        _db.BookImportJobs.Where(j => j.Id == id && j.Status == BookImportJobStatus.Running && j.Stage == BookImportStage.SetupConfirmation)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, BookImportJobStatus.AwaitingConfirmation), CancellationToken.None);

    private static bool IsAwaitingSetup(BookImportJob job) =>
        job.Status == BookImportJobStatus.AwaitingConfirmation && job.Stage == BookImportStage.SetupConfirmation;

    private static string? Validate(ConfirmBookImportSetupDto request, string bookName,
        IReadOnlyDictionary<int, BookImportJobTest> jobTests, int pageCount)
    {
        if (bookName.Length == 0 || bookName.Length > BookImportJob.MaxBookNameLength)
            return "bookImport.invalidBookName";
        var tests = request.Tests ?? [];
        if (tests.Count == 0)
            return "bookImport.noTests";
        if (tests.Any(t => t is null || !jobTests.ContainsKey(t.Order)) || tests.Select(t => t.Order).Distinct().Count() != tests.Count)
            return "bookImport.unknownTest";
        foreach (var test in tests)
        {
            if (test.FileFrom < 1 || test.FileTo < test.FileFrom || test.FileTo > pageCount)
                return "bookImport.invalidPages";
            if (test.Title is { Length: > BookImportJobTest.MaxTitleLength })
                return "bookImport.titleTooLong";
            if (test.Minutes is < 1 or > MaxMinutes)
                return "bookImport.invalidMinutes";
            if (!string.IsNullOrEmpty(test.AnswerKey) &&
                (test.AnswerKey.Length > BookImportJobTest.MaxKeyLength || !AnswerKeyPattern().IsMatch(test.AnswerKey)))
                return "bookImport.invalidAnswerKey";
        }

        return null;
    }

    /// <summary>
    /// Kitap kaydını yazar (#577): ofset, cevap anahtarı aralıkları, içindekiler (ders seçiliyse). Mevcut kitap kullanılırsa
    /// kümeleri boşsa doldurulur, doluysa olduğu gibi kalır.
    /// </summary>
    private async Task<int> WriteBookAsync(BookImportOwner owner, BookImportJob job, BookSetupSummary summary, string bookName,
        BookImportBookRefDto? reuse, CancellationToken ct)
    {
        var registry = new LabelingBookSaveRequestDto
        {
            Name = reuse?.Name ?? bookName,
            // Negatif ofsette baştaki sayfaların basılı numarası olmaz (kapak): aralık basılı 1'den başlar.
            Offsets = summary.Offset is { } offset && job.PageCount is { } pages && Math.Max(1, 1 - offset) <= pages
                ? [new LabelingBookOffsetDto { FileFrom = Math.Max(1, 1 - offset), FileTo = pages, Offset = offset }]
                : [],
            AnswerKeyRanges = summary.AnswerKeyRanges
                .Select(r => new LabelingBookPageRangeDto { FileFrom = r.FileFrom, FileTo = r.FileTo }).ToList(),
            Toc = job.SubjectId is { } subjectId
                ? summary.Units.Select(u => new LabelingBookTocInputDto
                {
                    FileFrom = u.FileFrom, FileTo = u.FileTo, GradeId = job.GradeId, SubjectId = subjectId, TopicId = u.TopicId,
                }).ToList()
                : [],
        };

        if (reuse is null)
            return (await _books.CreateAsync(registry, ct)).Id;

        var hasSets = await _db.BookPageOffsets.AnyAsync(o => o.BookId == reuse.Id, ct)
                      || await _db.BookAnswerKeyRanges.AnyAsync(r => r.BookId == reuse.Id, ct)
                      || await _db.BookTocEntries.AnyAsync(t => t.BookId == reuse.Id, ct);
        if (!hasSets)
            await _books.UpdateAsync(reuse.Id, registry, new BookEditor(owner.UserId, owner.IsAdmin), ct);
        return reuse.Id;
    }

    private async Task<BookImportJob> ApplyConfirmationAsync(BookImportOwner owner, Guid id, int bookId, string bookName,
        ConfirmBookImportSetupDto request, CancellationToken ct)
    {
        // Kitap kaydı servisi izleyiciyi temizler: iş ve testler yeniden yüklenir.
        var job = await _db.BookImportJobs.AsTracking().FirstAsync(j => j.Id == id, ct);
        var tests = await _db.BookImportJobTests.AsTracking().Where(t => t.JobId == id).ToListAsync(ct);
        var confirmed = request.Tests.ToDictionary(t => t.Order);

        var bookTests = await _db.BookTests.AsTracking().Where(t => t.BookId == bookId).ToListAsync(ct);
        var created = new List<(BookImportJobTest Row, BookTest Test)>();
        foreach (var row in tests)
        {
            if (!confirmed.TryGetValue(row.Order, out var input))
            {
                _db.BookImportJobTests.Remove(row);
                continue;
            }

            var key = string.IsNullOrEmpty(input.AnswerKey) ? null : input.AnswerKey;
            row.Title = string.IsNullOrWhiteSpace(input.Title) ? null : input.Title.Trim();
            row.Minutes = input.Minutes;
            row.FileFrom = input.FileFrom;
            row.FileTo = input.FileTo;
            row.AnswerKey = key;
            row.KeyConflicts = null;
            row.KeyAnswers = key?.Length ?? 0;
            row.ExpectedQuestions = key?.Length ?? row.ExpectedQuestions;
            var flags = ParseFlags(row.FlagsJson).Where(f => f != BookSetupPlanner.FlagKeyConflicts).ToList();
            row.FlagsJson = flags.Count == 0 ? null : JsonSerializer.Serialize(flags);

            var name = BookTestName(row);
            var bookTest = bookTests.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (bookTest is null)
            {
                bookTest = new BookTest { BookId = bookId, Name = name };
                _db.BookTests.Add(bookTest);
                bookTests.Add(bookTest);
            }

            created.Add((row, bookTest));
        }

        job.BookId = bookId;
        job.BookName = bookName.Length > 0 ? bookName : job.BookName;
        job.Stage = BookImportStage.Questions;
        job.Status = BookImportJobStatus.Queued;
        job.StageTotal = 0;
        job.StageProcessed = 0;
        _db.SetCurrentUser(owner.UserId);
        await _db.SaveChangesAsync(ct);

        foreach (var (row, bookTest) in created)
            row.BookTestId = bookTest.Id;
        await _db.SaveChangesAsync(ct);

        // Hak, kurulum onayında düşer (iş başına bir kez; yeniden onay aynı referansı sayar).
        await _entitlements.RecordUsageAsync(owner.UserId, EntitlementFeatures.BookImport, id.ToString(), ct);
        return job;
    }

    /// <summary>Kitap testinin adı: "1-01 · Milyonlar - I" (ünite-test no · başlık).</summary>
    /// <summary>Issue #1052: hızlı eklemede test satırı bir fotoğraftır, adı "Fotoğraf n" (ünite / test no yok).</summary>
    internal static string BookTestName(BookImportJob job, BookImportJobTest test) =>
        job.Kind == BookImportJobKind.QuickAdd ? test.Title ?? $"Fotoğraf {test.Order}" : BookTestName(test);

    internal static string BookTestName(BookImportJobTest test)
    {
        var code = $"{test.Unit}-{test.TestNo:D2}";
        return string.IsNullOrWhiteSpace(test.Title) ? code : $"{code} · {test.Title}";
    }

    private async Task<BookImportBookRefDto?> FindBookByNameAsync(string name, CancellationToken ct)
    {
        var trimmed = name.Trim();
        var books = await _db.Books.AsNoTracking().Select(b => new BookImportBookRefDto(b.Id, b.Name)).ToListAsync(ct);
        return books.FirstOrDefault(b => string.Equals(b.Name?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
    }
}
