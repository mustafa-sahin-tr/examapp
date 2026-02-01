using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.StudentReset;

public sealed class StudentResetJob
{
    private readonly AppDbContext _db;
    private readonly IBadgeResetApiClient _badgeResetApiClient;

    public StudentResetJob(AppDbContext db, IBadgeResetApiClient badgeResetApiClient)
    {
        _db = db;
        _badgeResetApiClient = badgeResetApiClient;
    }

    public async Task RunAsync(int userId, int studentId, string keycloakUserId)
    {
        var cancellationToken = CancellationToken.None;

        if (userId <= 0) throw new ArgumentOutOfRangeException(nameof(userId));
        if (studentId <= 0) throw new ArgumentOutOfRangeException(nameof(studentId));
        if (string.IsNullOrWhiteSpace(keycloakUserId)) throw new ArgumentException("Keycloak user id is required", nameof(keycloakUserId));

        _db.SetCurrentUser(userId);

        // 1) Reset Exam/Test progress (instances + answers)
        var instances = await _db.TestInstances
            .IgnoreQueryFilters()
            .Where(x => x.StudentId == studentId && !x.IsDeleted)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        if (instances.Count > 0)
        {
            var instanceQuestions = await _db.TestInstanceQuestions
                .IgnoreQueryFilters()
                .Where(x => instances.Contains(x.WorksheetInstanceId) && !x.IsDeleted)
                .ToListAsync(cancellationToken);

            if (instanceQuestions.Count > 0)
            {
                _db.TestInstanceQuestions.RemoveRange(instanceQuestions);
            }

            var instanceEntities = await _db.TestInstances
                .IgnoreQueryFilters()
                .Where(x => instances.Contains(x.Id) && !x.IsDeleted)
                .ToListAsync(cancellationToken);

            _db.TestInstances.RemoveRange(instanceEntities);
        }

        // 2) Reset points, badges, rewards, events, leaderboards
        await SoftDeleteByStudentIdAsync<StudentPoint>(_db.StudentPoints, studentId, cancellationToken);
        await SoftDeleteByStudentIdAsync<StudentPointHistory>(_db.StudentPointHistories, studentId, cancellationToken);
        await SoftDeleteByStudentIdAsync<StudentBadge>(_db.StudentBadges, studentId, cancellationToken);
        await SoftDeleteByStudentIdAsync<StudentReward>(_db.StudentRewards, studentId, cancellationToken);
        await SoftDeleteByStudentIdAsync<StudentSpecialEvent>(_db.StudentSpecialEvents, studentId, cancellationToken);
        await SoftDeleteByStudentIdAsync<Leaderboard>(_db.Leaderboards, studentId, cancellationToken);

        // 3) Reset personal worksheet assignments (do NOT touch grade-scoped assignments)
        var personalAssignments = await _db.WorksheetAssignments
            .IgnoreQueryFilters()
            .Where(x => x.StudentId == studentId && !x.IsDeleted)
            .ToListAsync(cancellationToken);
        if (personalAssignments.Count > 0)
        {
            _db.WorksheetAssignments.RemoveRange(personalAssignments);
        }

        // 4) Reset personal study programs (keycloak user id)
        var programIds = await _db.UserPrograms
            .IgnoreQueryFilters()
            .Where(x => x.UserId == keycloakUserId && !x.IsDeleted)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        if (programIds.Count > 0)
        {
            var schedules = await _db.UserProgramSchedules
                .IgnoreQueryFilters()
                .Where(x => programIds.Contains(x.UserProgramId) && !x.IsDeleted)
                .ToListAsync(cancellationToken);
            if (schedules.Count > 0)
            {
                _db.UserProgramSchedules.RemoveRange(schedules);
            }

            var programs = await _db.UserPrograms
                .IgnoreQueryFilters()
                .Where(x => programIds.Contains(x.Id) && !x.IsDeleted)
                .ToListAsync(cancellationToken);
            _db.UserPrograms.RemoveRange(programs);
        }

        await _db.SaveChangesAsync(cancellationToken);

        // 5) Reset BadgeService aggregates/activity/badge progress for the same userId
        await _badgeResetApiClient.ResetUserAsync(userId, cancellationToken);
    }

    private static async Task SoftDeleteByStudentIdAsync<TEntity>(DbSet<TEntity> set, int studentId, CancellationToken ct)
        where TEntity : BaseEntity
    {
        // Many entities have StudentId as a scalar; use EF.Property for generic filtering.
        var rows = await set
            .IgnoreQueryFilters()
            .Where(e => EF.Property<int>(e, "StudentId") == studentId && !e.IsDeleted)
            .ToListAsync(ct);

        if (rows.Count > 0)
        {
            set.RemoveRange(rows);
        }
    }
}
