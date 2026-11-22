using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Labeling;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Services.Labeling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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
    /// silinir. Boş liste sayfanın taslaklarını yalnız siler. <paramref name="activities"/> (#1293): tuvalde kalan etkinlik
    /// kutuları; verilirse sayfanın bunlarla örtüşmeyen etkinlik taslakları (silinen ya da soruya çevrilen) da silinir, null ise
    /// etkinlik taslaklarına dokunulmaz (hızlı ekleme).
    /// </summary>
    Task<BookImportPageFixResult> ReplacePageAsync(BookImportOwner owner, Guid id, int testId, int filePage,
        IReadOnlyList<LabelingBankQuestionDto> questions, CancellationToken ct, IReadOnlyList<LayoutBox>? activities = null);

    /// <summary>
    /// Issue #1173: sayfanın yaşayan taslakları (numara, sayfadaki kutu, doğru şık); düzeltme tuvali kutularına numara ve cevabı
    /// bunlardan işaretler. Kutusu bilinmeyen taslak listede kutusuz döner.
    /// </summary>
    Task<(BookImportPageFixError Error, IReadOnlyList<BookImportPageDraftDto> Drafts)> GetPageDraftsAsync(BookImportOwner owner,
        Guid id, int testId, int filePage, CancellationToken ct);
}

/// <summary>
/// Numara (#1171): istemcinin madde etiketi (sayıysa); yoksa kutunun örtüştüğü eski taslağın numarası (eski kutular kaynakta,
/// kutusuz eski taslaklar için önbellekteki tespitten aktarımla aynı sıralamayla); o da yoksa okuma sırasında önceki kutudan
/// sonraki boş numara (ilk kutu için sayfanın eski ilk numarası, yoksa önceki sayfaların son numarasından sonrası). Cevap: işaretlenen doğru şık, yoksa testin cevap anahtarı, yoksa cevapsız. Sayfanın inceleme işaretleri yeniden
/// hesaplanır; test sorunsuz kalırsa Done olur. Kayıt kendi transaction'ını açtığından önce yeni taslaklar kaydedilir
/// (eskilerin kırpmaları tekrar sayılmaz), sonra tek transaction'da eskiler silinir ve sıra yeniden yazılır; kayıt başarısızsa
/// test değişmez.
/// </summary>
public sealed class BookImportPageFixService(
    AppDbContext db, IMinIoService minio, ILabelingBankService bank, ILabelingService labeling, ILogger<BookImportPageFixService> logger)
    : IBookImportPageFixService
{
    public async Task<BookImportPageFixResult> ReplacePageAsync(BookImportOwner owner, Guid id, int testId, int filePage,
        IReadOnlyList<LabelingBankQuestionDto> questions, CancellationToken ct, IReadOnlyList<LayoutBox>? activities = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var (error, job, test, page) = await TargetAsync(owner, id, testId, filePage, ct);
        if (error != BookImportPageFixError.None)
            return new(error);
        var (from, to) = (test!.FileFrom!.Value, test.FileTo!.Value);

        var bookId = job!.BookId;
        var links = await LinksAsync(test.WorksheetId, bookId, ct);
        // #1134: tuval etkinlikleri soru olarak kaydetmez; sayfanın etkinlik taslakları tuvalde kaldıkça (#1293) yerinde kalır.
        var old = links.Where(l => l.FilePage == filePage && l is { IsDraft: true, IsActivity: false }).ToList();
        var (keptActivities, removedActivities) = SplitActivities(
            links.Where(l => l.FilePage == filePage && l is { IsDraft: true, IsActivity: true }).ToList(), activities);
        var oldIds = old.Select(l => l.QuestionId).ToList();
        var replacedIds = oldIds.Concat(removedActivities.Select(l => l.QuestionId)).ToList();
        var start = old.Concat(removedActivities).Select(l => l.Number).Where(n => n is not null).Min()
                    ?? (links.Where(l => l.FilePage < filePage).Select(l => l.Number).Max() ?? 0) + 1;

        string? image = null;
        var reference = new List<NumberedBox>();
        if (questions.Any(q => !int.TryParse(q.ItemLabel?.Trim(), out var n) || n <= 0))
        {
            (reference, image) = await ReferenceAsync(page!, links, old, filePage, ct);
            // #1293: silinen etkinliğin yerine çizilen soru etkinliğin tuttuğu numarayı alır.
            reference.AddRange(removedActivities.Where(l => l is { Number: not null, Box: not null })
                .Select(l => new NumberedBox(l.Box!.Value, l.Number!.Value)));
        }
        // Kalan etkinliklerin numaraları ayrılır (okuma sırasında yer tutarlar, sorular o numarayı almaz).
        var slots = keptActivities.Where(l => l is { Number: not null, Box: not null }).ToList();
        var numbered = NumberBoxes(
                questions.Select(q => new LayoutBox(q.X, q.Y, q.Width, q.Height)).Concat(slots.Select(l => l.Box!.Value)).ToList(),
                questions.Select(q => q.ItemLabel).Concat(slots.Select(l => (string?)l.Number!.Value.ToString(CultureInfo.InvariantCulture))).ToList(),
                reference, start, page.Width)
            .Where(n => n.Index < questions.Count).ToList();

        var label = BookQuestionImportStage.SourceLabel(job, test);
        // #1054: hızlı eklemede anahtar yalnız bulunan sayı anahtar uzunluğuna eşitse uygulanır (#1052 ile aynı kural).
        var answerKey = job.HasLooseQuestions && test.AnswerKey?.Length != questions.Count ? null : test.AnswerKey;
        var numbers = numbered.Select(n => n.Number).ToList();
        var toSave = numbered.Select(item =>
        {
            var (q, number) = (questions[item.Index], item.Number);
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
            image ??= await LoadAsync(page!);
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
            if (replacedIds.Count > 0)
            {
                await db.Questions.IgnoreQueryFilters([DraftQueryFilter.Key])
                    .Where(q => replacedIds.Contains(q.Id) && q.IsDraft)
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
                Relink(worksheet, links, replacedIds, saved.QuestionIds, filePage, owner.UserId, now,
                    kept.Select((k, i) => (k.Number, i)).Where(k => k.i < saved.QuestionIds.Count)
                        .ToDictionary(k => saved.QuestionIds[k.i], k => k.Number),
                    slots.ToDictionary(l => l.QuestionId, l => l.Number!.Value));
            await db.SaveChangesAsync(ct);
            test.WorksheetId = worksheet?.Id ?? test.WorksheetId;

            var live = worksheet?.WorksheetQuestions.Count(wq => !wq.IsDeleted) ?? 0;
            // Anahtarla karşılaştırma yalnız çoktan seçmeli sorularla (etkinlikler anahtarda yok).
            var liveQuestions = live - (links.Count(l => l.IsActivity) - removedActivities.Count);
            var flags = ReplacePageFlags(BookImportReviewFlag.Parse(test.ReviewFlagsJson), filePage,
                old.Concat(removedActivities).Select(l => l.Number).OfType<int>().Concat(numbers).ToHashSet(),
                kept.Where(k => k.Question.CorrectOptionIndex < 0).Select(k => k.Number).ToList(),
                kept.Where(k => k.Question.Options.Count != BookQuestionImportStage.ExpectedOptions).Select(k => k.Number).ToList(),
                liveQuestions, test.AnswerKey?.Length, from, to);
            test.QuestionsFound = Math.Max(0, test.QuestionsFound - old.Count - removedActivities.Count + questions.Count);
            test.QuestionsSaved = live;
            test.DuplicatesSkipped += saved.DuplicateIndexes.Count;
            test.ReviewFlagsJson = BookImportReviewFlag.Serialize(flags);
            test.FixedAt ??= DateTime.UtcNow;
            if (test.WorksheetId is not null)
            {
                test.Status = flags.Count == 0 ? BookImportTestStatus.Done : BookImportTestStatus.NeedsReview;
                test.Error = null;
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        await RecordManualAsync(page!, image, questions, activities, owner, ct);

        return new(BookImportPageFixError.None, new BookImportPageFixDto
        {
            Saved = saved.Saved,
            Duplicates = saved.DuplicateIndexes.Count,
            Removed = replacedIds.Count,
            Answerless = kept.Count(k => k.Question.CorrectOptionIndex < 0),
        });
    }

    public async Task<(BookImportPageFixError Error, IReadOnlyList<BookImportPageDraftDto> Drafts)> GetPageDraftsAsync(
        BookImportOwner owner, Guid id, int testId, int filePage, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var (error, job, test, page) = await TargetAsync(owner, id, testId, filePage, ct);
        if (error != BookImportPageFixError.None)
            return (error, []);
        var links = await LinksAsync(test!.WorksheetId, job!.BookId, ct);
        var old = links.Where(l => l.FilePage == filePage && l is { IsDraft: true, IsActivity: false }).ToList();
        if (old.Count == 0)
            return (error, []);

        var (reference, _) = await ReferenceAsync(page!, links, old, filePage, ct);
        var boxes = reference.ToDictionary(r => r.Number, r => r.Box);
        var ids = old.Select(l => l.QuestionId).ToList();
        var answers = await db.Questions.IgnoreQueryFilters([DraftQueryFilter.Key]).AsNoTracking()
            .Where(q => ids.Contains(q.Id) && q.CorrectAnswerId != null)
            // İnceleme ekranıyla aynı şık sırası: aktarımın şıklarında Order boş olabilir, sıra Id'den gelir.
            .Select(q => new
            {
                q.Id, q.CorrectAnswerId,
                Answers = q.Answers.Where(a => !a.IsDeleted).OrderBy(a => a.Order).ThenBy(a => a.Id).Select(a => a.Id).ToList(),
            })
            .ToListAsync(ct);
        var correct = answers.ToDictionary(a => a.Id, a => a.Answers.IndexOf(a.CorrectAnswerId!.Value));
        return (error, old.Where(l => l.Number is not null).OrderBy(l => l.Number).Select(l =>
        {
            var box = boxes.TryGetValue(l.Number!.Value, out var b) ? b : (LayoutBox?)null;
            return new BookImportPageDraftDto
            {
                QuestionId = l.QuestionId,
                Number = l.Number.Value,
                X = box?.X, Y = box?.Y, Width = box?.Width, Height = box?.Height,
                CorrectOptionIndex = correct.GetValueOrDefault(l.QuestionId, -1),
            };
        }).ToList());
    }

    /// <summary>İş, test ve sayfa; düzeltilebilir değilse hata (#1023 kuralları).</summary>
    private async Task<(BookImportPageFixError Error, BookImportJob? Job, BookImportJobTest? Test, BookImportPage? Page)> TargetAsync(
        BookImportOwner owner, Guid id, int testId, int filePage, CancellationToken ct)
    {
        var job = await (owner.IsAdmin ? db.BookImportJobs : db.BookImportJobs.Where(j => j.CreateUserId == owner.UserId))
            .AsNoTracking().FirstOrDefaultAsync(j => j.Id == id && j.Kind == owner.Kind, ct);
        var test = job is null
            ? null
            : await db.BookImportJobTests.AsTracking().FirstOrDefaultAsync(t => t.Id == testId && t.JobId == id, ct);
        if (job is null || test is null)
            return (BookImportPageFixError.NotFound, null, null, null);
        // #1267: sorular alınırken de bitmiş test düzeltilir (aşağıdaki test durumu denetimi bekleyen / alınanı dışarıda tutar).
        var reviewable = job.Status is BookImportJobStatus.ReadyForReview or BookImportJobStatus.Cancelled
                         || job.Stage == BookImportStage.Questions && BookImportService.ImportingStatuses.Contains(job.Status);
        if (!reviewable || (!job.HasLooseQuestions && job.BookId is null) || job.CreateUserId is not > 0
            || test.Status is not (BookImportTestStatus.Done or BookImportTestStatus.NeedsReview or BookImportTestStatus.Failed)
            || test.FileFrom is not { } from || test.FileTo is not { } to || filePage < from || filePage > to)
            return (BookImportPageFixError.InvalidState, null, null, null);
        var page = await db.BookImportPages.AsNoTracking().FirstOrDefaultAsync(p => p.JobId == id && p.FilePage == filePage, ct);
        return page is null ? (BookImportPageFixError.NotFound, null, null, null) : (BookImportPageFixError.None, job, test, page);
    }

    /// <summary>
    /// #1171: sayfanın eski taslaklarının kutuları ve numaraları. Kaynakta kutusu olmayanlar önbellekteki tespitten, aktarımla
    /// aynı sıralama ve numarayla (görsel o zaman okunur ve döner).
    /// </summary>
    private async Task<(List<NumberedBox> Reference, string? Image)> ReferenceAsync(BookImportPage page, List<Link> links,
        List<Link> old, int filePage, CancellationToken ct)
    {
        var reference = old.Where(l => l is { Number: not null, Box: not null })
            .Select(l => new NumberedBox(l.Box!.Value, l.Number!.Value)).ToList();
        var boxless = old.Where(l => l is { Number: not null, Box: null }).Select(l => l.Number!.Value).ToHashSet();
        if (boxless.Count == 0)
            return (reference, null);
        var image = await LoadAsync(page);
        var previousMax = links.Where(l => l.FilePage < filePage).Select(l => l.Number).Max() ?? 0;
        reference.AddRange(ImportNumbering(await DetectedBoxesAsync(page, image, ct), boxless, previousMax));
        return (reference, image);
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
                wq.Question.InteractionType,
                Source = db.QuestionSources.Where(s => s.QuestionId == wq.QuestionId && s.BookId == bookId)
                    .Select(s => new { s.FilePage, s.ItemLabel, s.BoxX, s.BoxY, s.BoxWidth, s.BoxHeight }).FirstOrDefault(),
            })
            .ToListAsync(ct);
        return rows.Select(r => new Link(r.QuestionId, r.IsDraft, r.Source?.FilePage ?? 0,
            int.TryParse(r.Source?.ItemLabel, out var n) ? n : null,
            r.Source is { BoxX: { } x, BoxY: { } y, BoxWidth: { } w, BoxHeight: { } h } ? new LayoutBox(x, y, w, h) : null,
            IsActivity: r.InteractionType is not (null or McqGrader.Type))).ToList();
    }

    /// <summary>
    /// Eski taslakların bağlantıları silinir, yeniler onların yerine (eskisi yoksa önceki sayfaların sorularından sonra)
    /// girer; sıra 1..n yeniden yazılır. #1293: sayfanın numaralı etkinlikleri (<paramref name="slots"/>) yeni sorularla numara
    /// sırasına dizilir (soruya çevrilen etkinliğin yanındaki etkinlik sona düşmesin); numarasız etkinlik ("E1") yerinde kalır.
    /// </summary>
    private static void Relink(Worksheet worksheet, List<Link> links, List<int> oldIds, IReadOnlyList<int> newIds, int filePage,
        int userId, DateTime now, IReadOnlyDictionary<int, int> newNumbers, IReadOnlyDictionary<int, int> slots)
    {
        var pageOf = links.ToDictionary(l => l.QuestionId, l => l.FilePage);
        var ordered = worksheet.WorksheetQuestions.Where(wq => !wq.IsDeleted).OrderBy(wq => wq.Order).ThenBy(wq => wq.Id).ToList();
        var moved = oldIds.Concat(slots.Keys).ToHashSet();
        var at = ordered.FindIndex(wq => moved.Contains(wq.QuestionId));
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

        var rest = ordered.Where(wq => !moved.Contains(wq.QuestionId)).ToList();
        var insertAt = ordered.Take(at).Count(wq => !moved.Contains(wq.QuestionId));
        var linked = rest.Select(wq => wq.QuestionId).ToHashSet();
        var activities = ordered.Where(wq => slots.ContainsKey(wq.QuestionId)).ToList();
        linked.UnionWith(activities.Select(wq => wq.QuestionId));
        var added = newIds.Where(linked.Add).Select(qid => new WorksheetQuestion { QuestionId = qid }).ToList();
        var group = added.Concat(activities)
            .OrderBy(wq => slots.TryGetValue(wq.QuestionId, out var n) ? n : newNumbers.GetValueOrDefault(wq.QuestionId, int.MaxValue))
            .ToList();
        rest.InsertRange(Math.Clamp(insertAt, 0, rest.Count), group);
        foreach (var wq in added)
            worksheet.WorksheetQuestions.Add(wq);
        for (var i = 0; i < rest.Count; i++)
            rest[i].Order = i + 1;
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

    /// <summary>
    /// Issue #1133: düzeltilen kutular sayfanın elle doğrulanmış sonucu (<see cref="LabelingSource.Manual"/>) olarak da yazılır;
    /// eğitim dışa aktarımı Claude sonucu yerine bunu kullanır. Düzeltme yalnız çoktan seçmeli soruları taşır: Claude
    /// sonucundaki diğer bloklar (etkinlik, bilgi kutusu …) korunur. Yazılamazsa düzeltme geçerli kalır (loglanır).
    /// </summary>
    private async Task RecordManualAsync(BookImportPage page, string? image, IReadOnlyList<LabelingBankQuestionDto> questions,
        IReadOnlyList<LayoutBox>? activities, BookImportOwner owner, CancellationToken ct)
    {
        try
        {
            image ??= await LoadAsync(page);
            var ai = await AiBoxesJsonAsync(image, ct);
            var others = ai is null
                ? []
                : (JsonSerializer.Deserialize<PredictionListDto>(ai)?.Predictions ?? [])
                    .Where(p => (p.Kind ?? LabelingBlocks.McqQuestion) != LabelingBlocks.McqQuestion).ToList();
            if (activities is not null)
                others = KeepActivities(others, activities);
            var predictions = questions.Select(q => new PredictionDto
            {
                X = q.X,
                Y = q.Y,
                Width = q.Width,
                Height = q.Height,
                Kind = LabelingBlocks.McqQuestion,
                Subpredictions = q.Options.Select(o => new PredictionDto { X = o.X, Y = o.Y, Width = o.Width, Height = o.Height }).ToList(),
            }).Concat(others).ToList();
            await labeling.SaveManualAsync(new LabelingSaveManualRequestDto
            {
                ImageBase64 = image,
                Predictions = new PredictionListDto { Predictions = predictions },
                BaseSource = ai is null ? null : nameof(LabelingSource.Ai),
            }, new LabelingActor(null, null, owner.IsAdmin), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Book import fix: manual labeling result for page {FilePage} of job {JobId} could not be saved",
                page.FilePage, page.JobId);
        }
    }

    /// <summary>Sayfanın en son Claude tespiti (önbellek; aktarım bunu kullandı).</summary>
    private async Task<string?> AiBoxesJsonAsync(string image, CancellationToken ct)
    {
        var sha = Convert.ToHexStringLower(SHA256.HashData(Convert.FromBase64String(image[(image.IndexOf(',') + 1)..])));
        return await db.LabelingResults.AsNoTracking()
            .Where(r => r.Page.ImageSha256 == sha && r.Source == LabelingSource.Ai)
            .OrderByDescending(r => r.UpdatedAt).Select(r => r.BoxesJson).FirstOrDefaultAsync(ct);
    }

    /// <summary>#1171: aktarımın kaydettiği kutular, aktarımdaki sırayla (okuma sırası, bölünmüş soru birleşik, şıklarla büyümüş).</summary>
    private async Task<List<LayoutBox>> DetectedBoxesAsync(BookImportPage page, string image, CancellationToken ct)
    {
        var ai = await AiBoxesJsonAsync(image, ct);
        if (ai is null || JsonSerializer.Deserialize<PredictionListDto>(ai) is not { } predictions)
            return [];
        return BookQuestionLayout.MergeSplit(
                BookQuestionLayout.Order(BookQuestionImportStage.ToLayout(predictions), page.Width), page.Width, page.Height)
            .Select(q => BookQuestionLayout.Prepare(q, page.Width, page.Height).Box).ToList();
    }

    internal sealed record NumberedBox(LayoutBox Box, int Number);

    /// <summary>
    /// Issue #1293: sayfanın etkinlik taslakları tuvalde kalan etkinlik kutularıyla eşlenir (<see cref="MinOverlap"/>); eşleşmeyen
    /// (tuvalde silinen ya da soruya çevrilen) taslak silinir. Kutusu bilinmeyen taslak kalır; kutu listesi yoksa hepsi kalır.
    /// </summary>
    private static (List<Link> Kept, List<Link> Removed) SplitActivities(List<Link> drafts, IReadOnlyList<LayoutBox>? boxes)
    {
        if (boxes is null)
            return (drafts, []);
        var removed = drafts.Where(l => l.Box is { } box && !boxes.Any(b => Overlap(b, box) >= MinOverlap)).ToList();
        return (drafts.Except(removed).ToList(), removed);
    }

    /// <summary>
    /// Issue #1293: elle doğrulanan sonuçta Claude'un etkinlik blokları tuvalde kalanlarla sınırlanır (tuval yeniden açılınca
    /// silinen etkinlik geri gelmesin); eşleşen blok tipiyle kalır, Claude'da olmayan kutu tipsiz etkinlik olarak eklenir.
    /// </summary>
    internal static List<PredictionDto> KeepActivities(IReadOnlyList<PredictionDto> others, IReadOnlyList<LayoutBox> activities)
    {
        static LayoutBox BoxOf(PredictionDto p) => new(p.X, p.Y, p.Width, p.Height);
        var result = others.Where(p => p.Kind != LabelingBlocks.Activity
                                       || activities.Any(a => Overlap(a, BoxOf(p)) >= MinOverlap)).ToList();
        result.AddRange(activities
            .Where(a => !others.Any(p => p.Kind == LabelingBlocks.Activity && Overlap(a, BoxOf(p)) >= MinOverlap))
            .Select(a => new PredictionDto { X = a.X, Y = a.Y, Width = a.Width, Height = a.Height, Kind = LabelingBlocks.Activity }));
        return result;
    }

    /// <summary>Kutunun eski taslakla eşlenmesi için örtüşme alt sınırı (kesişim / küçük kutunun alanı).</summary>
    internal const double MinOverlap = 0.6;

    /// <summary>
    /// #1171: kutusu kaydedilmemiş eski taslakların kutuları. Aktarım sayfanın soru kutularını sırayla <c>ilk + i</c> diye
    /// numaraladı; <paramref name="numbers"/> (sayfanın yaşayan kutusuz taslakları) bu aralığa sığacak ilk numara seçilir, önceki
    /// sayfaların son numarasının ardına en yakını. Sığmazsa (tespit değişmiş) eşleme yok.
    /// </summary>
    internal static List<NumberedBox> ImportNumbering(IReadOnlyList<LayoutBox> ordered, IReadOnlySet<int> numbers, int previousMax)
    {
        if (ordered.Count == 0 || numbers.Count == 0)
            return [];
        var (lo, hi) = (numbers.Max() - ordered.Count + 1, numbers.Min());
        if (lo > hi)
            return [];
        var first = Math.Clamp(previousMax + 1, lo, hi);
        return ordered.Select((box, i) => new NumberedBox(box, first + i)).Where(b => numbers.Contains(b.Number)).ToList();
    }

    /// <summary>
    /// #1171: kutular okuma sırasına dizilir ve numaralanır. Madde etiketi (sayı) önce; etiketsiz kutu en çok örtüştüğü eski
    /// kutunun numarasını alır (<see cref="MinOverlap"/>); kalan kutu okuma sırasında önceki kutunun numarasından sonraki boş
    /// numarayı alır. Sonuç numara sırasıyla (kutunun istekteki sırası + numarası).
    /// </summary>
    internal static List<(int Index, int Number)> NumberBoxes(IReadOnlyList<LayoutBox> boxes, IReadOnlyList<string?> labels,
        IReadOnlyList<NumberedBox> reference, int start, int pageWidth)
    {
        var items = boxes.Select(b => new LayoutQuestion(b, [])).ToList();
        var order = BookQuestionLayout.Order(items, pageWidth).Select(q => items.FindIndex(x => ReferenceEquals(x, q))).ToList();
        var number = new int?[boxes.Count];
        for (var i = 0; i < boxes.Count; i++)
        {
            if (int.TryParse(labels[i]?.Trim(), out var n) && n > 0)
                number[i] = n;
        }
        var used = number.OfType<int>().ToHashSet();
        var matches = (from i in order
                       where number[i] is null
                       from r in reference
                       where !used.Contains(r.Number)
                       let score = Overlap(boxes[i], r.Box)
                       where score >= MinOverlap
                       orderby score descending
                       select (Index: i, r.Number)).ToList();
        foreach (var (i, n) in matches)
        {
            if (number[i] is null && used.Add(n))
                number[i] = n;
        }

        var previous = start - 1;
        foreach (var i in order)
        {
            if (number[i] is null)
            {
                var n = previous + 1;
                while (used.Contains(n))
                    n++;
                number[i] = n;
                used.Add(n);
            }
            previous = number[i]!.Value;
        }
        return order.Select(i => (Index: i, Number: number[i]!.Value)).OrderBy(x => x.Number).ToList();
    }

    private static double Overlap(LayoutBox a, LayoutBox b)
    {
        var w = Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
        var h = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
        var smaller = Math.Min((double)a.Width * a.Height, (double)b.Width * b.Height);
        return w <= 0 || h <= 0 || smaller <= 0 ? 0 : w * (double)h / smaller;
    }

    private async Task<string> LoadAsync(BookImportPage page)
    {
        await using var stream = await minio.GetFileStreamAsync(page.ImageUrl)
                                 ?? throw new BookImportFailure($"Page {page.FilePage} image could not be read from storage.");
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, CancellationToken.None);
        return "data:image/webp;base64," + Convert.ToBase64String(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <param name="IsActivity">#1134: etkinlik taslağı (numarası ve şıkkı yok; sayfa düzeltmesi dokunmaz).</param>
    private sealed record Link(int QuestionId, bool IsDraft, int FilePage, int? Number, LayoutBox? Box, bool IsActivity = false);
}
