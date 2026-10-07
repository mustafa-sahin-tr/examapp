using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.StudentReset;

/// <summary>
/// Öğrencinin kendi verisini sıfırlar (Hangfire işi, <see cref="StudentResetScheduler"/> kuyruğa alır).
/// <para>
/// issue #396 — sıra:
/// <list type="number">
/// <item>Öğrencinin açık (Started) oturumları Expired'a çekilir (koşullu UPDATE, #367 satır kilidi): bundan sonra bu
/// oturumlara cevap/AnswerSubmittedEvent yazılamaz; uçuştaki bir SaveAnswer ya önce commit eder ya da reddedilir.</item>
/// <item>Sıfırlama zamanı (<c>resetAtUtc</c>) exam API saatinden alınır ve BadgeService'e gönderilir — AnswerSubmittedEvent'in
/// <c>SubmittedAt</c>'ı da bu saatle yazıldığından karşılaştırma tek saatle yapılır (saat kayması penceresi yok).</item>
/// <item>BadgeService sıfırlaması (<see cref="IBadgeResetApiClient"/>); yalnız başarılıysa exam verisinin soft-delete'i.
/// Başarısızsa istisna yukarı fırlar, exam verisi silinmez ve Hangfire işi yeniden dener.</item>
/// </list>
/// Retry tüm adımları yeniden çalıştırır (idempotent; sıfırlama zamanı ileri alınır — arada başlatılan oturumun cevapları
/// da yok sayılır ve oturum zaten silinir).
/// Tüm denemeler tükenip iş Failed olunca <see cref="StudentResetFailureAlertAttribute"/> Error loglar.
/// </para>
/// </summary>
[StudentResetFailureAlert]
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

        // 0a) issue #396: close the student's open sessions first — no answer (and no AnswerSubmittedEvent) can be written
        //     to them after this commits. Conditional UPDATE on the same row lock SaveAnswer takes (#367): an in-flight
        //     answer either commits before (its SubmittedAt < resetAt below) or is refused.
        var closedAt = DateTime.UtcNow;
        await _db.TestInstances
            .Where(x => x.StudentId == studentId && x.Status == WorksheetInstanceStatus.Started)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, WorksheetInstanceStatus.Expired)
                .SetProperty(x => x.EndTime, (DateTime?)closedAt)
                .SetProperty(x => x.UpdateTime, (DateTime?)closedAt)
                .SetProperty(x => x.UpdateUserId, (int?)userId), cancellationToken);

        // 0b) issue #396: BadgeService FIRST, with the reset line taken from THIS service's clock (the same clock that
        //     stamps AnswerSubmittedEvent.SubmittedAt). Throws on failure → nothing below runs, exam data stays intact and
        //     Hangfire retries the whole job.
        var resetAtUtc = DateTime.UtcNow;
        await _badgeResetApiClient.ResetUserAsync(userId, resetAtUtc, cancellationToken);

        // 0c) issue #422 (security review): sıfırlama çizgisi öğrenci kaydına yazılır — bundan önce kazanılıp geç teslim edilen
        //     StudentBadgeEarnedEvent veli projeksiyonuna yazılmaz (StudentBadgeProjectionService).
        await _db.Students.IgnoreQueryFilters()
            .Where(s => s.Id == studentId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ProgressResetAtUtc, (DateTime?)resetAtUtc), cancellationToken);

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

        // 5) issue #422: veli paneli projeksiyonları (rozet + günlük puan) — StudentPoints soft-delete'i commit edildikten SONRA
        //    silinir: arada işlenen bir puan event'i, silinmeden önce deftere satır yazıp onu bırakamasın (review: reset yarışı).
        //    BadgeService de rozetleri/puanı sıfırladığından yeniden kazanılan rozet ve yeni puanlar yeniden yazılabilir.
        await _db.StudentBadgeProjections.Where(b => b.StudentId == studentId).ExecuteDeleteAsync(cancellationToken);
        await _db.StudentDailyXps.Where(d => d.StudentId == studentId).ExecuteDeleteAsync(cancellationToken);
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
