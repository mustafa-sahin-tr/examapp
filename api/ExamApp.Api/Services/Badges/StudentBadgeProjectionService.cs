using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Services.StudentPoints;
using ExamApp.Foundation.Badges;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.Badges;

public enum StudentBadgeProjectionResult
{
    /// <summary>Projeksiyon satırı yazıldı.</summary>
    Applied,
    /// <summary>(Öğrenci, rozet) zaten var — tekrar teslim; no-op.</summary>
    Duplicate,
    /// <summary>UserId'ye karşılık gelen (silinmemiş) öğrenci yok; no-op (loglanır).</summary>
    StudentNotFound,
    /// <summary>Payload geçersiz (boş rozet id'si / ad); no-op (loglanır).</summary>
    Invalid,
    /// <summary>Rozet öğrencinin son sıfırlamasından önce kazanılmış (geç teslim); no-op (loglanır).</summary>
    BeforeReset,
}

public interface IStudentBadgeProjectionService
{
    Task<StudentBadgeProjectionResult> ApplyAsync(StudentBadgeEarnedEvent evt, CancellationToken ct = default);
}

/// <summary>
/// Issue #422: <see cref="StudentBadgeEarnedEvent"/> → <c>StudentBadgeProjections</c>. Idempotent: (StudentId,
/// BadgeDefinitionId) UNIQUE — var olan satır no-op; eşzamanlı ikinci INSERT UNIQUE ihlaline düşer → Duplicate. İlk yazılan
/// kazanma kaydı kalıcıdır (BadgeService'teki BadgeEarned ile aynı anlam: bir rozet bir kez kazanılır).
/// Security review: ikon yalnızca Foundation izin listesindeyse saklanır (<see cref="BadgeIconAllowlist"/>, aksi halde null);
/// öğrencinin son sıfırlamasından (<c>Student.ProgressResetAtUtc</c>, <see cref="ResetTolerance"/> payıyla) önce kazanılmış
/// rozetin geç gelen event'i atılır.
/// </summary>
public sealed class StudentBadgeProjectionService : IStudentBadgeProjectionService
{
    internal const int MaxNameLength = 200;

    /// <summary>Saat farkı payı: sıfırlama çizgisinden bu kadar önceki kazanımlar da kabul edilir.</summary>
    internal static readonly TimeSpan ResetTolerance = TimeSpan.FromMinutes(5);

    private readonly AppDbContext _db;
    private readonly ILogger<StudentBadgeProjectionService> _logger;

    public StudentBadgeProjectionService(AppDbContext db, ILogger<StudentBadgeProjectionService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<StudentBadgeProjectionResult> ApplyAsync(StudentBadgeEarnedEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        if (evt.BadgeDefinitionId == Guid.Empty || string.IsNullOrWhiteSpace(evt.Name))
        {
            _logger.LogWarning("[StudentBadgeProjection] Geçersiz payload atlandı (UserId={UserId}, BadgeDefinitionId={BadgeId}).",
                evt.UserId, evt.BadgeDefinitionId);
            return StudentBadgeProjectionResult.Invalid;
        }

        var student = await _db.Students.AsNoTracking()
            .Where(s => s.UserId == evt.UserId)
            .OrderBy(s => s.Id)
            .Select(s => new { s.Id, s.ProgressResetAtUtc })
            .FirstOrDefaultAsync(ct);
        if (student is null)
        {
            _logger.LogWarning("[StudentBadgeProjection] UserId={UserId} için öğrenci kaydı yok; event atlandı.", evt.UserId);
            return StudentBadgeProjectionResult.StudentNotFound;
        }

        var earnedAt = AsUtc(evt.EarnedAtUtc);
        if (student.ProgressResetAtUtc is { } resetAt && earnedAt < AsUtc(resetAt) - ResetTolerance)
        {
            _logger.LogWarning(
                "[StudentBadgeProjection] Sıfırlamadan önce kazanılmış rozetin geç event'i atlandı (UserId={UserId}, BadgeId={BadgeId}, EarnedAt={EarnedAt:o}, ResetAt={ResetAt:o}).",
                evt.UserId, evt.BadgeDefinitionId, earnedAt, resetAt);
            return StudentBadgeProjectionResult.BeforeReset;
        }

        var studentId = (int?)student.Id;

        if (await _db.StudentBadgeProjections.AnyAsync(
                b => b.StudentId == studentId.Value && b.BadgeDefinitionId == evt.BadgeDefinitionId, ct))
            return StudentBadgeProjectionResult.Duplicate;

        _db.StudentBadgeProjections.Add(new StudentBadgeProjection
        {
            StudentId = studentId.Value,
            BadgeDefinitionId = evt.BadgeDefinitionId,
            Name = Truncate(evt.Name.Trim(), MaxNameLength)!,
            Icon = BadgeIconAllowlist.IsAllowed(evt.Icon) ? evt.Icon : null,
            EarnedAtUtc = earnedAt,
            ReceivedAtUtc = DateTime.UtcNow,
        });

        try
        {
            await _db.SaveChangesAsync(ct);
            return StudentBadgeProjectionResult.Applied;
        }
        catch (DbUpdateException ex) when (StudentPointsSyncService.IsUniqueViolation(ex))
        {
            _db.ChangeTracker.Clear();
            return StudentBadgeProjectionResult.Duplicate;
        }
    }

    private static string? Truncate(string? value, int max) => value == null || value.Length <= max ? value : value[..max];

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
