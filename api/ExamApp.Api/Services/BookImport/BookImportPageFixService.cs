using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Labeling;
using ExamApp.Api.Services.Labeling;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.BookImport;

public enum BookImportPageFixError
{
    None,
    NotFound,
    /// <summary>İş incelemede değil, test onaylandı / silindi ya da sayfa testin sayfası değil.</summary>
    InvalidState,
}

public sealed record BookImportPageFixResult(BookImportPageFixError Error, BookImportPageFixDto? Page = null);

public interface IBookImportPageFixService
{
    /// <summary>
    /// Issue #1023 "İşaretlemede düzelt": incelemedeki testin bir sayfasının taslak sorularını işaretleme ekranında düzeltilen
    /// sorularla değiştirir. Görsel işin sayfa görselidir; yeni sorular taslak kaydedilip eskilerinin sırasına bağlanır, eskiler
    /// silinir. Boş liste sayfanın taslaklarını yalnız siler.
    /// </summary>
    Task<BookImportPageFixResult> ReplacePageAsync(BookImportOwner owner, Guid id, int testId, int filePage,
        IReadOnlyList<LabelingBankQuestionDto> questions, CancellationToken ct);
}

/// <summary>
/// Numara: istemcinin madde etiketi (sayıysa), yoksa sayfanın eski ilk numarasından (yoksa önceki sayfaların son numarasından)
/// sırayla. Cevap: işaretlenen doğru şık, yoksa testin cevap anahtarı, yoksa cevapsız. Sayfanın inceleme işaretleri yeniden
/// hesaplanır; test sorunsuz kalırsa Done olur. Kayıt kendi transaction'ını açtığından önce yeni taslaklar kaydedilir
/// (eskilerin kırpmaları tekrar sayılmaz), sonra tek transaction'da eskiler silinir ve sıra yeniden yazılır; kayıt başarısızsa
/// test değişmez.
/// </summary>
public sealed class BookImportPageFixService(AppDbContext db, IMinIoService minio, ILabelingBankService bank) : IBookImportPageFixService
{
    public async Task<BookImportPageFixResult> ReplacePageAsync(BookImportOwner owner, Guid id, int testId, int filePage,
        IReadOnlyList<LabelingBankQuestionDto> questions, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var job = await (owner.IsAdmin ? db.BookImportJobs : db.BookImportJobs.Where(j => j.CreateUserId == owner.UserId))
            .AsNoTracking().FirstOrDefaultAsync(j => j.Id == id && j.Kind == owner.Kind, ct);
        var test = job is null
            ? null
            : await db.BookImportJobTests.AsTracking().FirstOrDefaultAsync(t => t.Id == testId && t.JobId == id, ct);
        if (job is null || test is null)
            return new(BookImportPageFixError.NotFound);
        if (job.Status is not (BookImportJobStatus.ReadyForReview or BookImportJobStatus.Cancelled) || (job.Kind == BookImportJobKind.Book && job.BookId is null) || job.CreateUserId is not > 0
            || test.Status is not (BookImportTestStatus.Done or BookImportTestStatus.NeedsReview or BookImportTestStatus.Failed)
            || test.FileFrom is not { } from || test.FileTo is not { } to || filePage < from || filePage > to)
            return new(BookImportPageFixError.InvalidState);
        var page = await db.BookImportPages.AsNoTracking().FirstOrDefaultAsync(p => p.JobId == id && p.FilePage == filePage, ct);
        if (page is null)
            return new(BookImportPageFixError.NotFound);

        var bookId = job.BookId;
        var links = await LinksAsync(test.WorksheetId, bookId, ct);
        var old = links.Where(l => l.FilePage == filePage && l.IsDraft).ToList();
        var oldIds = old.Select(l => l.QuestionId).ToList();
        var start = old.Select(l => l.Number).Where(n => n is not null).Min()
                    ?? (links.Where(l => l.FilePage < filePage).Select(l => l.Number).Max() ?? 0) + 1;

        var label = BookQuestionImportStage.SourceLabel(job, test);
        // #1054: hızlı eklemede anahtar yalnız bulunan sayı anahtar uzunluğuna eşitse uygulanır (#1052 ile aynı kural).
        var answerKey = job.Kind == BookImportJobKind.QuickAdd && test.AnswerKey?.Length != questions.Count ? null : test.AnswerKey;
        var numbers = new List<int>();
        var toSave = questions.Select((q, i) =>
        {
            var number = int.TryParse(q.ItemLabel?.Trim(), out var n) && n > 0 ? n : start + i;
            numbers.Add(number);
            var answer = q.CorrectOptionIndex >= 0 && q.CorrectOptionIndex < q.Options.Count
                ? q.CorrectOptionIndex
                : BookQuestionImportStage.AnswerIndex(answerKey, number);
            return new LabelingBankQuestionDto
            {
                X = q.X,
                Y = q.Y,
                Width = q.Width,
                Height = q.Height,
                Options = q.Options,
                CorrectOptionIndex = answer < q.Options.Count ? answer : -1,
                ActivityLabel = label,
                ItemLabel = number.ToString(),
            };
        }).ToList();

        var saved = new LabelingImportPageResult(0, [], []);
        if (toSave.Count > 0)
        {
            var image = await LoadAsync(page);
            saved = await bank.SaveImportedPageAsync(new LabelingImportPageRequest(
                image, bookId, filePage, job.GradeId, QuestionBankSharing.Private, toSave, oldIds), job.CreateUserId.Value, ct);
        }

        // Kaydedilen (kırpması tekrar olmayan) sorular ve numaraları.
        var kept = toSave.Select((q, i) => (Question: q, Number: numbers[i], Index: i))
            .Where(k => !saved.DuplicateIndexes.Contains(k.Index)).ToList();

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var now = DateTime.UtcNow;
            if (oldIds.Count > 0)
            {
                await db.Questions.IgnoreQueryFilters([DraftQueryFilter.Key])
                    .Where(q => oldIds.Contains(q.Id) && q.IsDraft)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(q => q.IsDeleted, true)
                        .SetProperty(q => q.DeleteTime, now)
                        .SetProperty(q => q.DeleteUserId, owner.UserId), ct);
            }

            var worksheet = test.WorksheetId is { } worksheetId
                ? await db.Worksheets.IgnoreQueryFilters([DraftQueryFilter.Key]).AsTracking().Include(w => w.WorksheetQuestions)
                    .FirstOrDefaultAsync(w => w.Id == worksheetId && w.IsDraft && !w.IsDeleted, ct)
                : null;
            // Yeni taslak test işin sahibinin damgasıyla yazılır (taslağın sahibi işin sahibi), düzeltme düzeltenin.
            db.SetCurrentUser(owner.UserId);
            if (worksheet is null && saved.QuestionIds.Count > 0)
            {
                worksheet = BookQuestionImportStage.NewDraftTest(job, test);
                db.SetCurrentUser(job.CreateUserId!.Value);
                db.Worksheets.Add(worksheet);
            }
            if (worksheet is not null)
                Relink(worksheet, links, oldIds, saved.QuestionIds, filePage, owner.UserId, now);
            await db.SaveChangesAsync(ct);
            test.WorksheetId = worksheet?.Id ?? test.WorksheetId;

            var live = worksheet?.WorksheetQuestions.Count(wq => !wq.IsDeleted) ?? 0;
            var flags = ReplacePageFlags(BookImportReviewFlag.Parse(test.ReviewFlagsJson), filePage,
                old.Select(l => l.Number).OfType<int>().Concat(numbers).ToHashSet(),
                kept.Where(k => k.Question.CorrectOptionIndex < 0).Select(k => k.Number).ToList(),
                kept.Where(k => k.Question.Options.Count != BookQuestionImportStage.ExpectedOptions).Select(k => k.Number).ToList(),
                live, test.AnswerKey?.Length, from, to);
            test.QuestionsFound = Math.Max(0, test.QuestionsFound - old.Count + questions.Count);
            test.QuestionsSaved = live;
            test.DuplicatesSkipped += saved.DuplicateIndexes.Count;
            test.ReviewFlagsJson = BookImportReviewFlag.Serialize(flags);
            if (test.WorksheetId is not null)
            {
                test.Status = flags.Count == 0 ? BookImportTestStatus.Done : BookImportTestStatus.NeedsReview;
                test.Error = null;
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        return new(BookImportPageFixError.None, new BookImportPageFixDto
        {
            Saved = saved.Saved,
            Duplicates = saved.DuplicateIndexes.Count,
            Removed = oldIds.Count,
            Answerless = kept.Count(k => k.Question.CorrectOptionIndex < 0),
        });
    }

    /// <summary>Taslak testin silinmemiş bağlantıları: soru, kaynak sayfası, numarası (madde etiketi sayıysa).</summary>
    private async Task<List<Link>> LinksAsync(int? worksheetId, int? bookId, CancellationToken ct)
    {
        if (worksheetId is not { } wid)
            return [];
        var rows = await db.TestQuestions.IgnoreQueryFilters([DraftQueryFilter.Key]).AsNoTracking()
            .Where(wq => wq.TestId == wid && !wq.IsDeleted && !wq.Question.IsDeleted)
            .Select(wq => new
            {
                wq.QuestionId,
                wq.Question.IsDraft,
                Source = db.QuestionSources.Where(s => s.QuestionId == wq.QuestionId && s.BookId == bookId)
                    .Select(s => new { s.FilePage, s.ItemLabel }).FirstOrDefault(),
            })
            .ToListAsync(ct);
        return rows.Select(r => new Link(r.QuestionId, r.IsDraft, r.Source?.FilePage ?? 0,
            int.TryParse(r.Source?.ItemLabel, out var n) ? n : null)).ToList();
    }

    /// <summary>
    /// Eski taslakların bağlantıları silinir, yeniler onların yerine (eskisi yoksa önceki sayfaların sorularından sonra)
    /// girer; sıra 1..n yeniden yazılır.
    /// </summary>
    private static void Relink(Worksheet worksheet, List<Link> links, List<int> oldIds, IReadOnlyList<int> newIds, int filePage,
        int userId, DateTime now)
    {
        var pageOf = links.ToDictionary(l => l.QuestionId, l => l.FilePage);
        var ordered = worksheet.WorksheetQuestions.Where(wq => !wq.IsDeleted).OrderBy(wq => wq.Order).ThenBy(wq => wq.Id).ToList();
        var at = ordered.FindIndex(wq => oldIds.Contains(wq.QuestionId));
        if (at < 0)
        {
            at = ordered.FindLastIndex(wq => pageOf.GetValueOrDefault(wq.QuestionId) is var p && p > 0 && p < filePage) + 1;
        }
        foreach (var wq in ordered.Where(wq => oldIds.Contains(wq.QuestionId)))
        {
            wq.IsDeleted = true;
            wq.DeleteTime = now;
            wq.DeleteUserId = userId;
        }

        var kept = ordered.Where(wq => !oldIds.Contains(wq.QuestionId)).ToList();
        var removedBefore = ordered.Take(at).Count(wq => oldIds.Contains(wq.QuestionId));
        var linked = kept.Select(wq => wq.QuestionId).ToHashSet();
        var added = newIds.Where(linked.Add).Select(qid => new WorksheetQuestion { QuestionId = qid }).ToList();
        kept.InsertRange(Math.Clamp(at - removedBefore, 0, kept.Count), added);
        foreach (var wq in added)
            worksheet.WorksheetQuestions.Add(wq);
        for (var i = 0; i < kept.Count; i++)
            kept[i].Order = i + 1;
    }

    /// <summary>
    /// Sayfaya bağlı işaretlerden bu sayfa ve sayfanın numaraları çıkarılır (sayfası kalmayan işaret düşer); yeni sorular için
    /// cevapsız / şık sayısı işaretleri eklenir; sayı uyuşmazlığı testin güncel soru sayısıyla yeniden hesaplanır.
    /// </summary>
    internal static List<BookImportReviewFlag> ReplacePageFlags(IReadOnlyList<BookImportReviewFlag> flags, int filePage,
        IReadOnlySet<int> pageNumbers, IReadOnlyCollection<int> answerless, IReadOnlyCollection<int> optionCount,
        int found, int? keyLength, int from, int to)
    {
        var result = new List<BookImportReviewFlag>();
        foreach (var flag in flags)
        {
            if (flag.Code == BookImportReviewFlag.CountMismatch)
                continue;
            var pages = flag.Pages.Where(p => p != filePage).ToList();
            var numbers = flag.Numbers?.Where(n => !pageNumbers.Contains(n)).ToList();
            var add = flag.Code switch
            {
                BookImportReviewFlag.NoAnswer => answerless,
                BookImportReviewFlag.OptionCount => optionCount,
                _ => [],
            };
            if (add.Count > 0)
            {
                pages.Add(filePage);
                numbers = (numbers ?? []).Concat(add).ToList();
            }
            if (pages.Count == 0)
                continue;
            // Sayfa eşlemesi (eski kayıtlarda yok, öyle kalır): düzeltilen sayfanınki yeni sorularla değişir.
            Dictionary<int, IReadOnlyList<int>>? byPage = null;
            if (flag.ByPage is not null)
            {
                byPage = flag.ByPage.Where(p => p.Key != filePage).ToDictionary(p => p.Key, p => p.Value);
                if (add.Count > 0)
                    byPage[filePage] = add.Distinct().Order().ToList();
            }
            result.Add(flag with
            {
                Pages = pages.Distinct().Order().ToList(),
                Numbers = numbers is null || numbers.Count == 0 ? null : numbers.Distinct().Order().ToList(),
                ByPage = byPage,
            });
        }
        foreach (var (code, add) in new[] { (BookImportReviewFlag.OptionCount, optionCount), (BookImportReviewFlag.NoAnswer, answerless) })
        {
            if (add.Count > 0 && result.All(f => f.Code != code))
            {
                var numbers = add.Distinct().Order().ToList();
                result.Add(new BookImportReviewFlag(code, [filePage], numbers,
                    ByPage: new Dictionary<int, IReadOnlyList<int>> { [filePage] = numbers }));
            }
        }
        if (keyLength is { } expected && found != expected)
            result.Add(new BookImportReviewFlag(BookImportReviewFlag.CountMismatch, Enumerable.Range(from, to - from + 1).ToList(),
                Found: found, Expected: expected));
        return result;
    }

    private async Task<string> LoadAsync(BookImportPage page)
    {
        await using var stream = await minio.GetFileStreamAsync(page.ImageUrl)
                                 ?? throw new BookImportFailure($"Page {page.FilePage} image could not be read from storage.");
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, CancellationToken.None);
        return "data:image/webp;base64," + Convert.ToBase64String(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private sealed record Link(int QuestionId, bool IsDraft, int FilePage, int? Number);
}
