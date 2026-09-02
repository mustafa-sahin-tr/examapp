using System;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using Hangfire;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ExamApp.Api.Services.Worksheets;

/// <summary>
/// "Planla &amp; Hatırlat" — öğrenci başına worksheet başına tek plan.
/// Hangfire yalnızca tetikler; bildirim teslimi outbox event ile (event-integration-dev).
/// </summary>
public class WorksheetReminderService : IWorksheetReminderService
{
    private const int MaxRemindBeforeMinutes = 1440;

    private readonly AppDbContext _context;
    private readonly IBackgroundJobClient _jobs;

    public WorksheetReminderService(AppDbContext context, IBackgroundJobClient jobs)
    {
        _context = context;
        _jobs = jobs;
    }

    public async Task<WorksheetReminderDto?> GetAsync(int worksheetId, int studentId, CancellationToken ct)
    {
        var reminder = await _context.WorksheetReminders
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.WorksheetId == worksheetId
                && r.StudentId == studentId
                && r.Status != WorksheetReminderStatus.Cancelled, ct);

        return reminder == null ? null : MapToDto(reminder);
    }

    public async Task<WorksheetReminderDto> UpsertAsync(int worksheetId, int studentId, string? studentKeycloakId, DateTime scheduledForUtc, int remindBeforeMinutes, CancellationToken ct)
    {
        if (scheduledForUtc.Kind != DateTimeKind.Utc)
            scheduledForUtc = DateTime.SpecifyKind(scheduledForUtc, DateTimeKind.Utc);

        if (scheduledForUtc <= DateTime.UtcNow)
            throw new InvalidOperationException("Geçmiş bir tarih seçilemez");

        if (remindBeforeMinutes < 0 || remindBeforeMinutes > MaxRemindBeforeMinutes)
            throw new InvalidOperationException($"Hatırlatma süresi 0 ile {MaxRemindBeforeMinutes} dakika arasında olmalıdır");

        await EnsureStudentCanAccessWorksheetAsync(worksheetId, studentId, ct);

        // 1) Satırı yaz/güncelle. HangfireJobId eski değeriyle bırakılır; commit atomik.
        var (reminder, oldJobId) = await LoadOrCreateAndPersistAsync(worksheetId, studentId, studentKeycloakId, scheduledForUtc, remindBeforeMinutes, ct);

        // 2) Commit sonrası eski job'ı iptal et (best-effort).
        if (!string.IsNullOrWhiteSpace(oldJobId))
            TryCancelJob(oldJobId);

        // 3) Yeni job'ı schedule et.
        var triggerAt = scheduledForUtc.AddMinutes(-remindBeforeMinutes);
        var delay = triggerAt - DateTime.UtcNow;
        if (delay < TimeSpan.Zero)
            delay = TimeSpan.Zero;

        var reminderId = reminder.Id;
        var newJobId = _jobs.Schedule<IWorksheetReminderDispatcher>(
            d => d.DispatchAsync(reminderId, null, CancellationToken.None),
            delay);

        // 4) Yeni jobId'yi sabitle. Patlarsa yeni job'ı iptal etmeyi dene (best-effort).
        reminder.HangfireJobId = newJobId;
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch
        {
            TryCancelJob(newJobId);
            throw;
        }

        return MapToDto(reminder);
    }

    public async Task DeleteAsync(int worksheetId, int studentId, CancellationToken ct)
    {
        var reminder = await _context.WorksheetReminders
            .FirstOrDefaultAsync(r => r.WorksheetId == worksheetId && r.StudentId == studentId, ct);

        if (reminder == null)
            return;

        var jobId = reminder.HangfireJobId;

        reminder.Status = WorksheetReminderStatus.Cancelled;
        reminder.HangfireJobId = null;
        await _context.SaveChangesAsync(ct);

        if (!string.IsNullOrWhiteSpace(jobId))
            TryCancelJob(jobId);
    }

    /// <summary>Map için tek kaynak (WorksheetDetailService de bunu kullanır).</summary>
    public static WorksheetReminderDto MapToDto(WorksheetReminder r) => new()
    {
        WorksheetId = r.WorksheetId,
        ScheduledFor = r.ScheduledFor,
        RemindBeforeMinutes = r.RemindBeforeMinutes,
        Status = r.Status.ToString()
    };

    // --- helpers ---

    // Returns the persisted reminder plus the previous HangfireJobId (to be cancelled by the caller).
    private async Task<(WorksheetReminder Reminder, string? OldJobId)> LoadOrCreateAndPersistAsync(
        int worksheetId, int studentId, string? studentKeycloakId, DateTime scheduledForUtc, int remindBeforeMinutes, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var reminder = await _context.WorksheetReminders
                .FirstOrDefaultAsync(r => r.WorksheetId == worksheetId && r.StudentId == studentId, ct);

            var isNew = reminder == null;
            if (isNew)
            {
                reminder = new WorksheetReminder
                {
                    WorksheetId = worksheetId,
                    StudentId = studentId
                };
                _context.WorksheetReminders.Add(reminder);
            }

            var oldJobId = reminder!.HangfireJobId;

            reminder.ScheduledFor = scheduledForUtc;
            reminder.RemindBeforeMinutes = remindBeforeMinutes;
            reminder.Status = WorksheetReminderStatus.Pending;
            if (!string.IsNullOrWhiteSpace(studentKeycloakId))
                reminder.StudentKeycloakId = studentKeycloakId;
            // HangfireJobId eski değeriyle bırakılır — job henüz yeniden zamanlanmadı.

            try
            {
                await _context.SaveChangesAsync(ct);
                return (reminder, oldJobId);
            }
            catch (DbUpdateException ex) when (isNew && IsUniqueViolation(ex) && attempt == 0)
            {
                // Eşzamanlı bir PUT aynı (WorksheetId, StudentId) satırını yeni oluşturdu.
                // Eklediğimiz entity'yi detach et ve mevcut satırı yeniden yükleyip güncelle.
                _context.Entry(reminder).State = EntityState.Detached;
            }
        }

        throw new InvalidOperationException("Hatırlatma kaydedilemedi, lütfen tekrar deneyin");
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private void TryCancelJob(string jobId)
    {
        try
        {
            _jobs.ChangeState(jobId, new DeletedState());
        }
        catch
        {
            // best-effort: DispatchAsync'in Status != Pending guard'ı çift bildirimi engeller.
        }
    }

    private async Task EnsureStudentCanAccessWorksheetAsync(int worksheetId, int studentId, CancellationToken ct)
    {
        var worksheetExists = await _context.Worksheets.AnyAsync(w => w.Id == worksheetId, ct);
        if (!worksheetExists)
            throw new InvalidOperationException("Worksheet bulunamadı");

        var gradeId = await _context.Students.AsNoTracking()
            .Where(s => s.Id == studentId)
            .Select(s => s.GradeId)
            .FirstOrDefaultAsync(ct);

        var assigned = await _context.WorksheetAssignments.AsNoTracking()
            .AnyAsync(a => a.WorksheetId == worksheetId
                && (a.StudentId == studentId
                    || (a.StudentId == null && a.GradeId != null && a.GradeId == gradeId)), ct);

        if (assigned)
            return;

        var hasInstance = await _context.TestInstances.AsNoTracking()
            .AnyAsync(ti => ti.WorksheetId == worksheetId && ti.StudentId == studentId, ct);

        if (!hasInstance)
            throw new InvalidOperationException("Bu worksheet size atanmamış");
    }
}
