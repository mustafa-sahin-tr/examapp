using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Labeling;
using ExamApp.Api.Services.Labeling;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.BookImport;

/// <summary>Issue #1266: kurulum onayından sonra paylaşım değiştirme sonucu.</summary>
public enum SetBookImportSharingError
{
    None,
    NotFound,
    InvalidState,
    Invalid,
}

public sealed record SetBookImportSharingResult(SetBookImportSharingError Error, string? Code = null, int UpdatedDrafts = 0);

public sealed partial class BookImportService
{
    /// <summary>
    /// Issue #1266: paylaşım kitap aktarımının tamamına aittir; kurulum onayından sonra iş bitene (tamamlandı / iptal) kadar
    /// değişir. Demo ve kitapsız (hızlı ekleme, karışık sorular) işlerde yok.
    /// </summary>
    internal static bool CanChangeSharing(BookImportJob job) =>
        job.Kind == BookImportJobKind.Book && job.Source != BookImportSource.LooseImages && job.DemoSourceJobId is null
        && job.BookId is not null && job.Stage >= BookImportStage.Questions
        && job.Status is not (BookImportJobStatus.Completed or BookImportJobStatus.Cancelled);

    /// <summary>
    /// Issue #1266: işin paylaşımı değişir; onaylanmamış taslak testlere hemen, sonra alınacak testlere açılırken uygulanır.
    /// Onaylanmış testler normal testtir, paylaşımları testin ayarından değişir (dokunulmaz). Doğrulama kurulum onayıyla aynı.
    /// </summary>
    public async Task<SetBookImportSharingResult> SetSharingAsync(BookImportOwner owner, Guid id, SetBookImportSharingDto request,
        CancellationToken ct)
    {
        var job = await Visible(owner).AsTracking().FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null)
            return new(SetBookImportSharingError.NotFound);
        if (!CanChangeSharing(job))
            return new(SetBookImportSharingError.InvalidState);
        if (await ValidateSharingAsync(owner, request.TeacherSharing, request.Audience, ct) is { } code)
            return new(SetBookImportSharingError.Invalid, code);

        job.TeacherSharing = request.TeacherSharing;
        if (job.TeacherSharing == WorksheetTeacherSharing.BookAudience)
            await new BookAudienceService(_db).AddAsync(job.BookId!.Value, request.Audience ?? new BookAudienceSaveDto(), ct);
        await _db.SaveChangesAsync(ct);

        var drafts = _db.BookImportJobTests
            .Where(t => t.JobId == id && t.WorksheetId != null
                        && t.Status != BookImportTestStatus.Approved && t.Status != BookImportTestStatus.Removed)
            .Select(t => t.WorksheetId!.Value);
        var updated = await _db.Worksheets.IgnoreQueryFilters([DraftQueryFilter.Key])
            .Where(w => w.IsDraft && drafts.Contains(w.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.TeacherSharing, request.TeacherSharing), ct);
        return new(SetBookImportSharingError.None, UpdatedDrafts: updated);
    }
}
