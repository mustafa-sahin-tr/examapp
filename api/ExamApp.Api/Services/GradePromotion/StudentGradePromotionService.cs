using ExamApp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.GradePromotion;

public enum GradePromotionRevertStatus
{
    Reverted,

    /// <summary>Öğrencinin geri alınabilir (geri alınmamış) atlaması yok.</summary>
    NotFound,

    /// <summary>Öğrencinin sınıfı atlamadan sonra değişti; eski sınıfa dönmek yeni seçimi ezerdi.</summary>
    GradeChanged,
}

/// <summary>
/// Issue #1110: admin "sınıfta kaldı" — öğrencinin son (en yeni yılın) sınıf atlamasını geri alır. Geri alınmış atlama ikinci kez
/// geri alınmaz; daha eski bir yıla da inilmez.
/// </summary>
public interface IStudentGradePromotionService
{
    Task<GradePromotionRevertStatus> RevertAsync(int studentId, int actorUserId, CancellationToken ct = default);
}

public sealed class StudentGradePromotionService(AppDbContext db, TimeProvider? clock = null) : IStudentGradePromotionService
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<GradePromotionRevertStatus> RevertAsync(int studentId, int actorUserId, CancellationToken ct = default)
    {
        var last = await db.StudentGradePromotions.AsNoTracking()
            .Where(p => p.StudentId == studentId)
            .OrderByDescending(p => p.Year)
            .FirstOrDefaultAsync(ct);
        if (last is null || last.RevertedAtUtc != null)
            return GradePromotionRevertStatus.NotFound;

        var now = _clock.GetUtcNow().UtcDateTime;
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Koşullu: öğrenci hâlâ atlamanın sınıfındaysa ve atlama başka bir istekle geri alınmadıysa.
            var marked = await db.StudentGradePromotions
                .Where(p => p.Id == last.Id && p.RevertedAtUtc == null)
                .ExecuteUpdateAsync(u => u.SetProperty(p => p.RevertedAtUtc, now).SetProperty(p => p.RevertedByUserId, actorUserId), ct);
            if (marked == 0)
            {
                await tx.RollbackAsync(ct);
                return GradePromotionRevertStatus.NotFound;
            }

            var moved = await db.Students
                .Where(s => s.Id == studentId && s.GradeId == last.ToGradeId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.GradeId, last.FromGradeId), ct);
            if (moved == 0)
            {
                await tx.RollbackAsync(ct);
                return GradePromotionRevertStatus.GradeChanged;
            }

            await tx.CommitAsync(ct);
            return GradePromotionRevertStatus.Reverted;
        });
    }
}
