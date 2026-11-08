using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.BookImport;

/// <summary>Issue #1025: yeniden deneme sonucu.</summary>
public enum RetryBookImportResult
{
    Queued,
    NotFound,
    InvalidState,
}

/// <summary>
/// Issue #1025: aktarımın kurtarma işlemleri — iptal (taslaklar kalır ya da silinir), takılmış / hatalı işi ve hatalı testleri
/// yeniden deneme. Çalışma kaldığı yerden devam eder (<see cref="BookImportJobRunner"/>: bitmiş sayfa / test atlanır).
/// </summary>
public sealed partial class BookImportService
{
    private static readonly BookImportJobStatus[] CancellableStatuses =
    [
        BookImportJobStatus.Queued, BookImportJobStatus.Running, BookImportJobStatus.AwaitingConfirmation,
        BookImportJobStatus.AwaitingBudget, BookImportJobStatus.AwaitingCostApproval, BookImportJobStatus.Failed,
    ];

    public async Task<CancelBookImportResult> CancelAsync(BookImportOwner owner, Guid id, bool deleteDrafts, CancellationToken ct)
    {
        var job = await Visible(owner).AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null)
            return CancelBookImportResult.NotFound;
        var now = DateTime.UtcNow;
        // Koşullu yazım: çalışan iş aynı anda bitirmiş olabilir. Çalışma iptali test / sayfa arasında görür.
        var rows = await _db.BookImportJobs
            .Where(j => j.Id == id && CancellableStatuses.Contains(j.Status))
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, BookImportJobStatus.Cancelled)
                .SetProperty(j => j.ResumeAt, (DateTime?)null)
                .SetProperty(j => j.FinishedAt, now), ct);
        if (rows == 0)
            return CancelBookImportResult.InvalidState;
        if (deleteDrafts)
            await DeleteDraftsAsync(job, owner.UserId, now, ct);
        return CancelBookImportResult.Cancelled;
    }

    public async Task<RetryBookImportResult> RetryAsync(BookImportOwner owner, Guid id, CancellationToken ct)
    {
        var job = await Visible(owner).AsTracking().FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null)
            return RetryBookImportResult.NotFound;
        if (job.Status != BookImportJobStatus.Failed && !IsStalled(job))
            return RetryBookImportResult.InvalidState;

        Requeue(job);
        await _db.SaveChangesAsync(ct);
        _jobs.Enqueue<BookImportJobRunner>(r => r.RunAsync(id));
        return RetryBookImportResult.Queued;
    }

    public async Task<RetryBookImportResult> RetryTestsAsync(BookImportOwner owner, Guid id, int? testId, CancellationToken ct)
    {
        var job = await Visible(owner).AsTracking().FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null)
            return RetryBookImportResult.NotFound;
        var tests = await _db.BookImportJobTests.AsTracking()
            .Where(t => t.JobId == id && t.Status == BookImportTestStatus.Failed && (testId == null || t.Id == testId))
            .ToListAsync(ct);
        if (testId is not null && tests.Count == 0 &&
            !await _db.BookImportJobTests.AnyAsync(t => t.JobId == id && t.Id == testId, ct))
            return RetryBookImportResult.NotFound;
        // Yalnız soru aktarımı bittikten sonra (incelemede, kalan testler onaylanıp tamamlandıysa ya da iş hatayla düştüyse);
        // çalışan işte bekleyen test zaten alınır.
        var retryable = job.Status is BookImportJobStatus.ReadyForReview or BookImportJobStatus.Completed
                        || (job.Status == BookImportJobStatus.Failed && job.Stage >= BookImportStage.Questions);
        if (!retryable || tests.Count == 0)
            return RetryBookImportResult.InvalidState;

        foreach (var test in tests)
        {
            test.Status = BookImportTestStatus.Pending;
            test.Error = null;
        }
        Requeue(job);
        job.Stage = BookImportStage.Questions;
        await _db.SaveChangesAsync(ct);
        _jobs.Enqueue<BookImportJobRunner>(r => r.RunAsync(id));
        return RetryBookImportResult.Queued;
    }

    /// <summary>
    /// Issue #1025: kuyrukta / çalışıyor görünen ama uzun süredir ilerlemeyen iş (her sayfa / test kaydı satırı günceller), ya da
    /// zamanı çoktan geçmiş bütçe beklemesi (zamanlanmış Hangfire işi kaybolmuş).
    /// </summary>
    internal bool IsStalled(BookImportJob job)
    {
        var limit = DateTime.UtcNow - _stallAfter;
        return job.Status switch
        {
            BookImportJobStatus.Queued or BookImportJobStatus.Running => (job.UpdateTime ?? job.CreateTime) < limit,
            BookImportJobStatus.AwaitingBudget => job.ResumeAt < limit,
            _ => false,
        };
    }

    private static void Requeue(BookImportJob job)
    {
        job.Status = BookImportJobStatus.Queued;
        job.Error = null;
        job.FinishedAt = null;
        job.ResumeAt = null;
    }

    /// <summary>
    /// İptalde "taslakları da sil": onaylanmamış testlerin taslak testleri ve taslak soruları, ayrıca yarıda kalan testin henüz
    /// teste bağlanmamış taslakları (bu kitapta, sahibin, hiçbir teste bağlı olmayan).
    /// </summary>
    private async Task DeleteDraftsAsync(BookImportJob job, int userId, DateTime now, CancellationToken ct)
    {
        var tests = await _db.BookImportJobTests.AsTracking()
            .Where(t => t.JobId == job.Id && t.WorksheetId != null
                        && t.Status != BookImportTestStatus.Approved && t.Status != BookImportTestStatus.Removed)
            .ToListAsync(ct);
        var worksheetIds = tests.Select(t => t.WorksheetId!.Value).ToList();
        var bookId = job.BookId;
        var ownerId = job.CreateUserId;

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            var drafts = _db.Questions.IgnoreQueryFilters([DraftQueryFilter.Key]).Where(q => q.IsDraft && !q.IsDeleted && (
                _db.TestQuestions.Any(wq => worksheetIds.Contains(wq.TestId) && wq.QuestionId == q.Id && !wq.IsDeleted)
                || (bookId != null && q.CreateUserId == ownerId
                    && _db.QuestionSources.IgnoreQueryFilters().Any(s => s.QuestionId == q.Id && s.BookId == bookId)
                    && !_db.TestQuestions.IgnoreQueryFilters().Any(wq => wq.QuestionId == q.Id && !wq.IsDeleted))));
            await drafts.ExecuteUpdateAsync(s => s
                .SetProperty(q => q.IsDeleted, true)
                .SetProperty(q => q.DeleteTime, now)
                .SetProperty(q => q.DeleteUserId, userId), ct);
            await _db.Worksheets.IgnoreQueryFilters([DraftQueryFilter.Key])
                .Where(w => worksheetIds.Contains(w.Id) && w.IsDraft)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(w => w.IsDeleted, true)
                    .SetProperty(w => w.DeleteTime, now)
                    .SetProperty(w => w.DeleteUserId, userId), ct);
            foreach (var test in tests)
                test.Status = BookImportTestStatus.Removed;
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }
}
