using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using Hangfire;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interactions;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.BookImport;

public enum BookImportReviewResult
{
    Ok,
    NotFound,
    /// <summary>İş incelemede değil ya da test onaylanabilir / silinebilir durumda değil.</summary>
    InvalidState,
    /// <summary>Testte cevapsız soru var.</summary>
    Answerless,
    InvalidOption,
    /// <summary>#1169: testin son sorusu çıkarılamaz (testi silmek gerekir).</summary>
    LastQuestion,
    /// <summary>#1269: test işaretli; işaretlemede düzeltilip işaretleri kalkınca onaylanır.</summary>
    Flagged,
}

/// <summary>
/// Issue #1023: inceleme ve onay. Aktarımın oluşturduğu test ve sorular taslaktır (<see cref="DraftQueryFilter"/>); onay
/// taslak bayrağını kaldırır (öğretmen paylaşımı Private ve soru bankası paylaşımı olduğu gibi kalır), silme yalnız o testin
/// taslak testini ve taslak sorularını siler. İşlemler yalnız sahibi / Admin için ve iş incelemedeyken. #1054: hızlı ekleme test
/// değil soru üretir; onayda sorular etkinleşir, iç taslak test bağlantılarıyla silinir. #1144 karışık sorular da öyle; hepsini
/// onaylarken sorular isteğe bağlı olarak yeni bir teste toplanır.
/// </summary>
public sealed partial class BookImportService
{
    internal const string SkipAnswerless = "answerless";
    internal const string SkipFailed = "failed";
    internal const string SkipFlagged = "flagged";

    public async Task<List<BookImportReviewQuestionDto>?> GetReviewQuestionsAsync(BookImportOwner owner, Guid id, int testId, CancellationToken ct)
    {
        var test = await ReviewTestAsync(owner, id, testId, ct);
        if (test is null)
            return null;
        if (test.WorksheetId is not { } worksheetId)
            return [];

        return await _db.TestQuestions.IgnoreQueryFilters([DraftQueryFilter.Key]).AsNoTracking()
            .Where(wq => wq.TestId == worksheetId && !wq.IsDeleted && !wq.Question.IsDeleted)
            .OrderBy(wq => wq.Order)
            .Select(wq => new
            {
                wq.QuestionId,
                Source = _db.QuestionSources.Where(s => s.QuestionId == wq.QuestionId).Select(s => new { s.ItemLabel, s.FilePage }).FirstOrDefault(),
                Answers = wq.Question.Answers.Where(a => !a.IsDeleted).OrderBy(a => a.Order).ThenBy(a => a.Id).Select(a => a.Id).ToList(),
                wq.Question.CorrectAnswerId,
                wq.Question.ImageUrl,
                wq.Question.InteractionType,
                PassageImageUrl = wq.Question.Passage != null ? wq.Question.Passage.ImageUrl : null,
            })
            .AsAsyncEnumerable()
            .Select(q => new BookImportReviewQuestionDto
            {
                Id = q.QuestionId,
                Number = q.Source?.ItemLabel,
                FilePage = q.Source?.FilePage ?? 0,
                OptionCount = q.Answers.Count,
                ImageUrl = q.ImageUrl,
                PassageImageUrl = q.PassageImageUrl,
                AnswerIndex = q.CorrectAnswerId is { } correct && q.Answers.IndexOf(correct) is var index and >= 0 ? index : null,
                InteractionType = q.InteractionType is null or McqGrader.Type ? null : q.InteractionType,
            })
            .ToListAsync(ct);
    }

    public async Task<BookImportReviewResult> SetAnswerAsync(BookImportOwner owner, Guid id, int testId, int questionId, int optionIndex,
        CancellationToken ct)
    {
        var test = await ReviewTestAsync(owner, id, testId, ct, tracking: true);
        if (test is null)
            return BookImportReviewResult.NotFound;
        if (!IsReviewable(test) || !await InReviewAsync(id, ct))
            return BookImportReviewResult.InvalidState;

        var question = await DraftQuestions(test.WorksheetId!.Value).AsTracking()
            .Include(q => q.Answers)
            .FirstOrDefaultAsync(q => q.Id == questionId, ct);
        if (question is null)
            return BookImportReviewResult.NotFound;
        var answers = question.Answers.Where(a => !a.IsDeleted).OrderBy(a => a.Order).ThenBy(a => a.Id).ToList();
        if (optionIndex < 0 || optionIndex >= answers.Count)
            return BookImportReviewResult.InvalidOption;

        question.CorrectAnswerId = answers[optionIndex].Id;
        _db.SetCurrentUser(owner.UserId);
        await _db.SaveChangesAsync(ct);
        // #1269: cevapsız soru kalmadıysa "cevapsız" işareti kalkar.
        if (!await DraftQuestions(test.WorksheetId!.Value).AnyAsync(AnswerlessQuestion, ct)
            && ApplyReviewFix(test, f => f.Code == BookImportReviewFlag.NoAnswer ? null : f))
            await _db.SaveChangesAsync(ct);
        return BookImportReviewResult.Ok;
    }

    /// <summary>
    /// #1169: hatalı tespit edilmiş taslak soruyu testten çıkarır (soru ve test bağlantısı silinir; testin kalan soruları ve
    /// sırası değişmez). Son soru çıkarılamaz.
    /// </summary>
    public async Task<BookImportReviewResult> RemoveQuestionAsync(BookImportOwner owner, Guid id, int testId, int questionId,
        CancellationToken ct)
    {
        var test = await ReviewTestAsync(owner, id, testId, ct, tracking: true);
        if (test is null)
            return BookImportReviewResult.NotFound;
        if (!IsReviewable(test) || !await InReviewAsync(id, ct))
            return BookImportReviewResult.InvalidState;

        var worksheetId = test.WorksheetId!.Value;
        var links = await _db.TestQuestions.IgnoreQueryFilters([DraftQueryFilter.Key]).AsTracking()
            .Where(wq => wq.TestId == worksheetId && !wq.IsDeleted && !wq.Question.IsDeleted)
            .Include(wq => wq.Question)
            .ToListAsync(ct);
        var link = links.FirstOrDefault(wq => wq.QuestionId == questionId && wq.Question.IsDraft);
        if (link is null)
            return BookImportReviewResult.NotFound;
        if (links.Count == 1)
            return BookImportReviewResult.LastQuestion;

        var now = DateTime.UtcNow;
        foreach (var deleted in new BaseEntity[] { link, link.Question })
        {
            deleted.IsDeleted = true;
            deleted.DeleteTime = now;
            deleted.DeleteUserId = owner.UserId;
        }

        // #1269: çıkan sorunun cevapsız / şık sayısı işareti kalkar; soru sayısı anahtarla tutarsa sayı uyuşmazlığı da.
        var label = await _db.QuestionSources.Where(qs => qs.QuestionId == questionId).Select(qs => qs.ItemLabel).FirstOrDefaultAsync(ct);
        int? number = int.TryParse(label, out var n) ? n : null;
        var liveQuestions = links.Count(wq => wq != link && (wq.Question.InteractionType == null || wq.Question.InteractionType == McqGrader.Type));
        var noneAnswerless = !links.Any(wq => wq != link && wq.Question.CorrectAnswerId == null
                                              && (wq.Question.InteractionType == null || wq.Question.InteractionType == McqGrader.Type));
        ApplyReviewFix(test, f => f.Code switch
        {
            BookImportReviewFlag.NoAnswer when noneAnswerless => null,
            BookImportReviewFlag.NoAnswer or BookImportReviewFlag.OptionCount when number is { } removed => WithoutNumber(f, removed),
            BookImportReviewFlag.CountMismatch when f.Expected == liveQuestions => null,
            BookImportReviewFlag.CountMismatch => f with { Found = liveQuestions },
            _ => f,
        });

        _db.SetCurrentUser(owner.UserId);
        await _db.SaveChangesAsync(ct);
        return BookImportReviewResult.Ok;
    }

    public async Task<BookImportReviewResult> ApproveTestAsync(BookImportOwner owner, Guid id, int testId, CancellationToken ct)
    {
        var test = await ReviewTestAsync(owner, id, testId, ct, tracking: true);
        if (test is null)
            return BookImportReviewResult.NotFound;
        if (!IsReviewable(test) || !await InReviewAsync(id, ct))
            return BookImportReviewResult.InvalidState;
        if (await DraftQuestions(test.WorksheetId!.Value).AnyAsync(AnswerlessQuestion, ct))
            return BookImportReviewResult.Answerless;
        // #1269: işaretli test onaylanmaz; işaretlemede düzeltilip işaretleri kalkınca (Done) onaylanır.
        if (test.Status == BookImportTestStatus.NeedsReview)
            return BookImportReviewResult.Flagged;

        await ApproveAsync(owner, test, await HasLooseQuestionsAsync(owner, id, ct), ct);
        await CompleteIfReviewedAsync(id, ct);
        return BookImportReviewResult.Ok;
    }

    /// <param name="collectTestName">#1144: karışık sorularda doluysa bu çağrıda onaylanan sorular bu adla yeni bir teste (özel,
    /// yayımlanmamış) sırasıyla toplanır; kitap ve hızlı eklemede yok sayılır.</param>
    public async Task<ApproveAllBookImportResultDto?> ApproveAllAsync(BookImportOwner owner, Guid id, CancellationToken ct,
        string? collectTestName = null)
    {
        var job = await Visible(owner).AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null)
            return null;
        if (!await InReviewAsync(id, ct))
            return new ApproveAllBookImportResultDto();

        var tests = await _db.BookImportJobTests.AsTracking()
            .Where(t => t.JobId == id && t.Status != BookImportTestStatus.Approved && t.Status != BookImportTestStatus.Removed)
            .OrderBy(t => t.Order)
            .ToListAsync(ct);
        var result = new ApproveAllBookImportResultDto();
        var loose = job.HasLooseQuestions;
        var collectName = job.Source == BookImportSource.LooseImages ? collectTestName?.Trim() : null;
        var collected = new List<int>();
        foreach (var test in tests)
        {
            // #1267: aktarım sürerken bekleyen / alınan test atlanır (hata sayılmaz, sonra onaylanır).
            if (IsUnfinished(test))
                continue;
            if (!IsReviewable(test))
            {
                result.Skipped.Add(new BookImportSkippedTestDto(test.Id, SkipFailed));
                continue;
            }
            if (await DraftQuestions(test.WorksheetId!.Value).AnyAsync(AnswerlessQuestion, ct))
            {
                result.Skipped.Add(new BookImportSkippedTestDto(test.Id, SkipAnswerless));
                continue;
            }
            // İşaretli test (eksik / fazla soru, şık sayısı …) onaylanmaz: işaretlemede düzeltilince (Done) onaylanır (#1269).
            if (test.Status == BookImportTestStatus.NeedsReview)
            {
                result.Skipped.Add(new BookImportSkippedTestDto(test.Id, SkipFlagged));
                continue;
            }
            if (!string.IsNullOrEmpty(collectName))
            {
                collected.AddRange(await _db.TestQuestions.IgnoreQueryFilters([DraftQueryFilter.Key])
                    .Where(wq => wq.TestId == test.WorksheetId!.Value && !wq.IsDeleted)
                    .OrderBy(wq => wq.Order).ThenBy(wq => wq.Id)
                    .Select(wq => wq.QuestionId)
                    .ToListAsync(ct));
            }
            await ApproveAsync(owner, test, loose, ct);
            result.Approved.Add(test.Id);
        }
        if (!string.IsNullOrEmpty(collectName) && collected.Count > 0)
            result.CollectedTestId = await CollectIntoTestAsync(owner, job, collectName, collected, ct);
        await CompleteIfReviewedAsync(id, ct);
        return result;
    }

    /// <summary>#1144: onaylanan karışık soruları yeni bir teste toplar (taslak değil, öğretmen paylaşımı özel).</summary>
    private async Task<int> CollectIntoTestAsync(BookImportOwner owner, BookImportJob job, string name, List<int> questionIds,
        CancellationToken ct)
    {
        var worksheet = new Worksheet
        {
            Name = name.Length <= CollectedTestNameLength ? name : name[..CollectedTestNameLength],
            Description = $"{job.BookName}. Görsellerden eklenen sorular.",
            GradeId = job.GradeId,
            SubjectId = job.SubjectId,
            TeacherSharing = job.TeacherSharing, // #1259
        };
        var order = 0;
        foreach (var questionId in questionIds.Distinct())
            worksheet.WorksheetQuestions.Add(new WorksheetQuestion { QuestionId = questionId, Order = ++order });
        _db.SetCurrentUser(owner.UserId);
        _db.Worksheets.Add(worksheet);
        await _db.SaveChangesAsync(ct);
        return worksheet.Id;
    }

    internal const int CollectedTestNameLength = 200;

    private async Task<bool> HasLooseQuestionsAsync(BookImportOwner owner, Guid id, CancellationToken ct) =>
        owner.Kind == BookImportJobKind.QuickAdd
        || await _db.BookImportJobs.AnyAsync(j => j.Id == id && j.Source == BookImportSource.LooseImages, ct);

    public async Task<BookImportReviewResult> RemoveTestAsync(BookImportOwner owner, Guid id, int testId, CancellationToken ct)
    {
        var test = await ReviewTestAsync(owner, id, testId, ct, tracking: true);
        if (test is null)
            return BookImportReviewResult.NotFound;
        if (test.Status is BookImportTestStatus.Approved or BookImportTestStatus.Removed || !await InReviewAsync(id, ct))
            return BookImportReviewResult.InvalidState;
        // #1267: aktarım sürerken bekleyen / alınan test kaldırılamaz (aktarım durumunu yeniden yazar).
        if (IsUnfinished(test) && await _db.BookImportJobs.AnyAsync(j => j.Id == id && ImportingStatuses.Contains(j.Status), ct))
            return BookImportReviewResult.InvalidState;

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            await DeleteTestDraftsAsync(test, owner.UserId, ct);
            test.Status = BookImportTestStatus.Removed;
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
        await CompleteIfReviewedAsync(id, ct);
        return BookImportReviewResult.Ok;
    }

    public async Task<BookImportReviewResult> SetDeferredAsync(BookImportOwner owner, Guid id, bool deferred, CancellationToken ct)
    {
        var job = await Visible(owner).AsTracking().FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null)
            return BookImportReviewResult.NotFound;
        if (job.Status != BookImportJobStatus.ReadyForReview)
            return BookImportReviewResult.InvalidState;

        job.DeferredAt = deferred ? job.DeferredAt ?? DateTime.UtcNow : null;
        await _db.SaveChangesAsync(ct);
        return BookImportReviewResult.Ok;
    }

    public async Task<List<BookImportDeferredJobDto>> ListDeferredAsync(BookImportOwner owner, CancellationToken ct)
    {
        var jobs = await Visible(owner).AsNoTracking()
            .Where(j => j.DeferredAt != null && j.Status == BookImportJobStatus.ReadyForReview)
            .OrderBy(j => j.DeferredAt)
            .Select(j => new BookImportDeferredJobDto
            {
                Id = j.Id, BookName = j.BookName, PageCount = j.PageCount, DeferredAt = j.DeferredAt!.Value, OwnerId = j.CreateUserId,
            })
            .ToListAsync(ct);
        var ids = jobs.Select(j => j.Id).ToList();
        var tests = await _db.BookImportJobTests.AsNoTracking()
            .Where(t => ids.Contains(t.JobId) && t.Status != BookImportTestStatus.Approved && t.Status != BookImportTestStatus.Removed)
            .OrderBy(t => t.Order)
            .ToListAsync(ct);
        var byJob = tests.ToLookup(t => t.JobId);
        foreach (var job in jobs)
        {
            job.Tests = byJob[job.Id].Select(t => new BookImportDeferredTestDto
            {
                Id = t.Id,
                Order = t.Order,
                Unit = t.Unit,
                TestNo = t.TestNo,
                Title = t.Title,
                Status = t.Status.ToString(),
                FileFrom = t.FileFrom,
                FileTo = t.FileTo,
                Flags = ParseFlags(t.FlagsJson),
                ReviewFlags = BookImportReviewFlag.Parse(t.ReviewFlagsJson),
                Error = t.Error,
            }).ToList();
        }

        return jobs;
    }

    public async Task<RetryBookImportResult> ReimportTestAsync(BookImportOwner owner, Guid id, int testId, CancellationToken ct)
    {
        var job = await Visible(owner).AsTracking().FirstOrDefaultAsync(j => j.Id == id, ct);
        var test = job is null ? null : await ReviewTestAsync(owner, id, testId, ct, tracking: true);
        if (job is null || test is null)
            return RetryBookImportResult.NotFound;
        if (job.Status != BookImportJobStatus.ReadyForReview || !IsReviewable(test))
            return RetryBookImportResult.InvalidState;

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            // Eski taslaklar silinir: kalırlarsa devam (ResumeDrafts) onları eski numara ve cevaplarıyla yeniden kullanırdı.
            await DeleteTestDraftsAsync(test, owner.UserId, ct);
            test.WorksheetId = null;
            test.Status = BookImportTestStatus.Pending;
            test.Error = null;
            test.QuestionsFound = 0;
            test.QuestionsSaved = 0;
            test.DuplicatesSkipped = 0;
            test.ReviewFlagsJson = null;
            test.FixedAt = null;
            Requeue(job);
            job.Stage = BookImportStage.Questions;
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
        _jobs.Enqueue<BookImportJobRunner>(r => r.RunAsync(id));
        return RetryBookImportResult.Queued;
    }

    /// <summary>Yalnız bu testin taslakları: onaylanmış (etkin) bir soru başka bir yerde kullanılıyor olabilir.</summary>
    private async Task DeleteTestDraftsAsync(BookImportJobTest test, int userId, CancellationToken ct)
    {
        if (test.WorksheetId is not { } worksheetId)
            return;
        var now = DateTime.UtcNow;
        await DraftQuestions(worksheetId).ExecuteUpdateAsync(s => s
            .SetProperty(q => q.IsDeleted, true)
            .SetProperty(q => q.DeleteTime, now)
            .SetProperty(q => q.DeleteUserId, userId), ct);
        await _db.Worksheets.IgnoreQueryFilters([DraftQueryFilter.Key])
            .Where(w => w.Id == worksheetId && w.IsDraft)
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.IsDeleted, true)
                .SetProperty(w => w.DeleteTime, now)
                .SetProperty(w => w.DeleteUserId, userId), ct);
    }

    /// <summary>Taslak test başına cevapsız taslak soru sayısı (takip ekranı; onayı engeller).</summary>
    private async Task<Dictionary<int, int>> AnswerlessCountsAsync(List<int> worksheetIds, CancellationToken ct)
    {
        if (worksheetIds.Count == 0)
            return [];
        return await _db.TestQuestions.IgnoreQueryFilters([DraftQueryFilter.Key]).AsNoTracking()
            .Where(wq => worksheetIds.Contains(wq.TestId) && !wq.IsDeleted && wq.Question.IsDraft && !wq.Question.IsDeleted
                         && wq.Question.CorrectAnswerId == null
                         && (wq.Question.InteractionType == null || wq.Question.InteractionType == McqGrader.Type))
            .GroupBy(wq => wq.TestId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);
    }

    private async Task<BookImportJobTest?> ReviewTestAsync(BookImportOwner owner, Guid id, int testId, CancellationToken ct, bool tracking = false)
    {
        var query = _db.BookImportJobTests.Where(t => t.Id == testId && t.JobId == id && Visible(owner).Any(j => j.Id == id));
        return await (tracking ? query.AsTracking() : query.AsNoTracking()).FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// #1269: incelemedeki düzeltmenin (cevap seçme, soru çıkarma) çözdüğü işaretler değişir; işaret kalmazsa test hazır
    /// (onaylanabilir) olur ve elle düzeltildi sayılır (#1270). Değişiklik olduysa true.
    /// </summary>
    private static bool ApplyReviewFix(BookImportJobTest test, Func<BookImportReviewFlag, BookImportReviewFlag?> change)
    {
        var before = BookImportReviewFlag.Parse(test.ReviewFlagsJson);
        var after = before.Select(change).OfType<BookImportReviewFlag>().ToList();
        if (after.SequenceEqual(before))
            return false;
        test.ReviewFlagsJson = BookImportReviewFlag.Serialize(after);
        test.FixedAt ??= DateTime.UtcNow;
        if (after.Count == 0 && test.Status == BookImportTestStatus.NeedsReview)
            test.Status = BookImportTestStatus.Done;
        return true;
    }

    /// <summary>İşaretten bir soru numarası çıkar; numarası kalmayan işaret düşer.</summary>
    private static BookImportReviewFlag? WithoutNumber(BookImportReviewFlag flag, int number)
    {
        if (flag.Numbers is null || !flag.Numbers.Contains(number))
            return flag;
        var numbers = flag.Numbers.Where(x => x != number).ToList();
        if (numbers.Count == 0)
            return null;
        var byPage = flag.ByPage?
            .Select(p => (p.Key, Numbers: (IReadOnlyList<int>)p.Value.Where(x => x != number).ToList()))
            .Where(p => p.Numbers.Count > 0)
            .ToDictionary(p => p.Key, p => p.Numbers);
        return flag with { Numbers = numbers, ByPage = byPage, Pages = byPage is null ? flag.Pages : byPage.Keys.Order().ToList() };
    }

    /// <summary>Onaylanabilir: aktarımı bitmiş (sorunsuz ya da kontrol gerekli) ve taslak testi olan.</summary>
    private static bool IsReviewable(BookImportJobTest test) =>
        test.Status is BookImportTestStatus.Done or BookImportTestStatus.NeedsReview && test.WorksheetId is not null;

    /// <summary>
    /// İncelemede ya da iptal edilmiş (taslakları kalan, #1025) iş: testler onaylanıp silinebilir. #1267: sorular alınırken de;
    /// yalnız bitmiş testler (aktarım bitmiş teste bir daha dokunmaz; bekleyen / alınan test engellenir).
    /// </summary>
    private Task<bool> InReviewAsync(Guid id, CancellationToken ct) =>
        _db.BookImportJobs.AnyAsync(j => j.Id == id && (j.Status == BookImportJobStatus.ReadyForReview
                                                        || j.Status == BookImportJobStatus.Cancelled
                                                        || (j.Stage == BookImportStage.Questions && ImportingStatuses.Contains(j.Status))), ct);

    /// <summary>#1267: soru aktarımı süren işin durumları (çalışıyor, sırada, bütçe / maliyet onayı bekliyor).</summary>
    internal static readonly BookImportJobStatus[] ImportingStatuses =
    [
        BookImportJobStatus.Queued, BookImportJobStatus.Running, BookImportJobStatus.AwaitingBudget,
        BookImportJobStatus.AwaitingCostApproval,
    ];

    /// <summary>#1267: aktarım bu teste hâlâ dokunabilir (bekliyor ya da alınıyor).</summary>
    internal static bool IsUnfinished(BookImportJobTest test) =>
        test.Status is BookImportTestStatus.Pending or BookImportTestStatus.Running;

    /// <summary>Testin taslak soruları (silinmemiş bağlantılar).</summary>
    /// <summary>
    /// Onayı engelleyen cevapsız soru: yalnız çoktan seçmeli. #1134: etkinlik taslağı çözümsüz aktarılır ve öyle onaylanır
    /// (cevaplar sonradan etkinlik editöründe elle girilir).
    /// </summary>
    private static readonly System.Linq.Expressions.Expression<Func<Question, bool>> AnswerlessQuestion =
        q => q.CorrectAnswerId == null && (q.InteractionType == null || q.InteractionType == McqGrader.Type);

    private IQueryable<Question> DraftQuestions(int worksheetId) =>
        _db.Questions.IgnoreQueryFilters([DraftQueryFilter.Key])
            .Where(q => q.IsDraft && _db.TestQuestions.Any(wq => wq.TestId == worksheetId && wq.QuestionId == q.Id && !wq.IsDeleted));

    private async Task ApproveAsync(BookImportOwner owner, BookImportJobTest test, bool loose, CancellationToken ct)
    {
        var worksheetId = test.WorksheetId!.Value;
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            await DraftQuestions(worksheetId).ExecuteUpdateAsync(s => s.SetProperty(q => q.IsDraft, false), ct);
            if (loose)
            {
                var now = DateTime.UtcNow;
                await _db.TestQuestions.IgnoreQueryFilters([DraftQueryFilter.Key])
                    .Where(wq => wq.TestId == worksheetId && !wq.IsDeleted)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(wq => wq.IsDeleted, true)
                        .SetProperty(wq => wq.DeleteTime, now)
                        .SetProperty(wq => wq.DeleteUserId, owner.UserId), ct);
                await _db.Worksheets.IgnoreQueryFilters([DraftQueryFilter.Key])
                    .Where(w => w.Id == worksheetId && w.IsDraft)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(w => w.IsDeleted, true)
                        .SetProperty(w => w.DeleteTime, now)
                        .SetProperty(w => w.DeleteUserId, owner.UserId), ct);
            }
            else
            {
                await _db.Worksheets.IgnoreQueryFilters([DraftQueryFilter.Key])
                    .Where(w => w.Id == worksheetId)
                    .ExecuteUpdateAsync(s => s.SetProperty(w => w.IsDraft, false), ct);
            }
            test.Status = BookImportTestStatus.Approved;
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }

    /// <summary>Onaylanacak ya da silinecek test kalmadıysa iş tamamlanır (Onay aşaması).</summary>
    private async Task CompleteIfReviewedAsync(Guid id, CancellationToken ct)
    {
        var open = await _db.BookImportJobTests.AnyAsync(t => t.JobId == id &&
            (t.Status == BookImportTestStatus.Done || t.Status == BookImportTestStatus.NeedsReview) && t.WorksheetId != null, ct);
        if (open)
            return;
        var now = DateTime.UtcNow;
        await _db.BookImportJobs.Where(j => j.Id == id && j.Status == BookImportJobStatus.ReadyForReview)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, BookImportJobStatus.Completed)
                .SetProperty(j => j.Stage, BookImportStage.Approval)
                .SetProperty(j => j.FinishedAt, now), ct);
    }
}
