using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using Hangfire;
using ExamApp.Api.Models.Dtos;
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
            })
            .ToListAsync(ct);
    }

    public async Task<BookImportReviewResult> SetAnswerAsync(BookImportOwner owner, Guid id, int testId, int questionId, int optionIndex,
        CancellationToken ct)
    {
        var test = await ReviewTestAsync(owner, id, testId, ct);
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
        return BookImportReviewResult.Ok;
    }

    public async Task<BookImportReviewResult> ApproveTestAsync(BookImportOwner owner, Guid id, int testId, CancellationToken ct)
    {
        var test = await ReviewTestAsync(owner, id, testId, ct, tracking: true);
        if (test is null)
            return BookImportReviewResult.NotFound;
        if (!IsReviewable(test) || !await InReviewAsync(id, ct))
            return BookImportReviewResult.InvalidState;
        if (await DraftQuestions(test.WorksheetId!.Value).AnyAsync(q => q.CorrectAnswerId == null, ct))
            return BookImportReviewResult.Answerless;

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
            if (!IsReviewable(test))
            {
                result.Skipped.Add(new BookImportSkippedTestDto(test.Id, SkipFailed));
                continue;
            }
            if (await DraftQuestions(test.WorksheetId!.Value).AnyAsync(q => q.CorrectAnswerId == null, ct))
            {
                result.Skipped.Add(new BookImportSkippedTestDto(test.Id, SkipAnswerless));
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
            TeacherSharing = WorksheetTeacherSharing.Private,
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
                         && wq.Question.CorrectAnswerId == null)
            .GroupBy(wq => wq.TestId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);
    }

    private async Task<BookImportJobTest?> ReviewTestAsync(BookImportOwner owner, Guid id, int testId, CancellationToken ct, bool tracking = false)
    {
        var query = _db.BookImportJobTests.Where(t => t.Id == testId && t.JobId == id && Visible(owner).Any(j => j.Id == id));
        return await (tracking ? query.AsTracking() : query.AsNoTracking()).FirstOrDefaultAsync(ct);
    }

    /// <summary>Onaylanabilir: aktarımı bitmiş (sorunsuz ya da kontrol gerekli) ve taslak testi olan.</summary>
    private static bool IsReviewable(BookImportJobTest test) =>
        test.Status is BookImportTestStatus.Done or BookImportTestStatus.NeedsReview && test.WorksheetId is not null;

    /// <summary>İncelemede ya da iptal edilmiş (taslakları kalan, #1025) iş: testler onaylanıp silinebilir.</summary>
    private Task<bool> InReviewAsync(Guid id, CancellationToken ct) =>
        _db.BookImportJobs.AnyAsync(j => j.Id == id && (j.Status == BookImportJobStatus.ReadyForReview || j.Status == BookImportJobStatus.Cancelled), ct);

    /// <summary>Testin taslak soruları (silinmemiş bağlantılar).</summary>
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
