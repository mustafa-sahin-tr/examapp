using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.MockExams;
using ExamApp.Api.Services.Parents;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.TargetGoals;

/// <summary>Issue #695: öğrencinin hedefi ve fark için gereken denemeler. Yetki çağıranın sorumluluğundadır.</summary>
public interface ITargetGoalService
{
    Task<TargetGoalDto> GetAsync(int studentId, CancellationToken ct = default);

    /// <summary>Katalogda olmayan program için <c>false</c>.</summary>
    Task<bool> SetAsync(int studentId, int targetProgramId, CancellationToken ct = default);

    Task ClearAsync(int studentId, CancellationToken ct = default);
}

public sealed class TargetGoalService(AppDbContext db, IMockExamService mockExams) : ITargetGoalService
{
    public async Task<TargetGoalDto> GetAsync(int studentId, CancellationToken ct = default)
    {
        var target = await db.StudentTargetGoals.AsNoTracking()
            .Where(g => g.StudentId == studentId)
            .Select(g => new TargetProgramDto
            {
                Id = g.TargetProgram.Id, ScoreType = g.TargetProgram.ScoreType, Name = g.TargetProgram.Name, Institution = g.TargetProgram.Institution,
                City = g.TargetProgram.City, District = g.TargetProgram.District, Year = g.TargetProgram.Year,
                MinimumScore = g.TargetProgram.MinimumScore, Percentile = g.TargetProgram.Percentile, SourceReference = g.TargetProgram.SourceReference,
            })
            .FirstOrDefaultAsync(ct);
        if (target is null)
            return new();

        return new()
        {
            Target = target,
            Results = await mockExams.GetLatestCompleteResultsAsync(studentId, TargetScoreTypes.TemplateCodes(target.ScoreType), ct),
        };
    }

    public async Task<bool> SetAsync(int studentId, int targetProgramId, CancellationToken ct = default)
    {
        if (!await db.TargetPrograms.AnyAsync(p => p.Id == targetProgramId, ct))
            return false;

        var now = DateTime.UtcNow;
        if (await UpdateAsync(studentId, targetProgramId, now, ct) > 0)
            return true;

        db.StudentTargetGoals.Add(new StudentTargetGoal { StudentId = studentId, TargetProgramId = targetProgramId, SelectedAt = now });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbUpdateExceptionClassifier.IsUniqueViolation(ex))
        {
            // Aynı öğrencinin eşzamanlı ilk seçimi: son istek kazanır.
            db.ChangeTracker.Clear();
            await UpdateAsync(studentId, targetProgramId, now, ct);
        }
        return true;
    }

    public Task ClearAsync(int studentId, CancellationToken ct = default) =>
        db.StudentTargetGoals.Where(g => g.StudentId == studentId).ExecuteDeleteAsync(ct);

    private Task<int> UpdateAsync(int studentId, int targetProgramId, DateTime now, CancellationToken ct) =>
        db.StudentTargetGoals.Where(g => g.StudentId == studentId).ExecuteUpdateAsync(s => s
            .SetProperty(g => g.TargetProgramId, targetProgramId)
            .SetProperty(g => g.SelectedAt, now), ct);
}

public interface IParentTargetGoalService
{
    /// <summary>Başka velinin ya da Active olmayan bağlantının çocuğu için <c>null</c>.</summary>
    Task<TargetGoalDto?> GetAsync(int parentUserId, int studentId, CancellationToken ct = default);
}

public sealed class ParentTargetGoalService(
    IParentChildAccess access,
    IParentAccessAuditLog audit,
    ITargetGoalService goals) : IParentTargetGoalService
{
    public async Task<TargetGoalDto?> GetAsync(int parentUserId, int studentId, CancellationToken ct = default)
    {
        var grant = await access.EnsureActiveChildAsync(parentUserId, studentId, ct);
        if (grant is null)
            return null;

        // Veri okunmadan önce: okuma hata verse de erişim denemesi kayıtta kalır.
        await audit.RecordAsync(grant.ParentId, grant.StudentId, ParentAccessEndpoints.ChildTargetGoal, ct: ct);

        var goal = await goals.GetAsync(grant.StudentId, ct);
        if (goal.Target is not null)
            goal.Target.SourceReference = null;
        return goal;
    }
}
