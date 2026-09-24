using ExamApp.Api.Data;
using ExamApp.Api.Migrations;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #277 migration backfill SQL'leri (SQLite üzerinde, #287 <c>AddTeacherAccountApprovedAt</c> testiyle aynı desen):
/// (madde 7) <see cref="AddWorksheetAssignmentIsPlatformWide.BackfillSql"/> — bugün "okul kısıtı yok" sayılan sınıf
/// atamaları (SchoolId null) platform geneli işaretlenir, davranış korunur;
/// (madde 2) <see cref="AddTeacherLastRejectedAt.BackfillSql"/> — Rejected satırların ret anı audit → UpdateTime → CreateTime.
/// </summary>
public class Issue277MigrationBackfillTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task IsPlatformWide_backfill_marks_only_null_school_grade_assignments_and_is_idempotent()
    {
        int grade, school, student, ws;
        await using (var ctx = _db.NewContext())
        {
            var g = new Grade { Name = "8" };
            var s = new School { Name = "A" };
            ctx.AddRange(g, s);
            await ctx.SaveChangesAsync();
            var st = new Student { UserId = 1, StudentNumber = "n", GradeId = g.Id, SchoolId = s.Id };
            var w = new Worksheet { Name = "W", Description = "", GradeId = g.Id };
            ctx.AddRange(st, w);
            await ctx.SaveChangesAsync();
            (grade, school, student, ws) = (g.Id, s.Id, st.Id, w.Id);

            var start = DateTime.UtcNow.AddDays(-1);
            ctx.WorksheetAssignments.AddRange(
                new WorksheetAssignment { Id = 1, WorksheetId = ws, GradeId = grade, StartAt = start },                      // admin/legacy → true
                new WorksheetAssignment { Id = 2, WorksheetId = ws, GradeId = grade, SchoolId = school, StartAt = start },   // okullu → false
                new WorksheetAssignment { Id = 3, WorksheetId = ws, StudentId = student, StartAt = start },                  // öğrenci → false
                new WorksheetAssignment { Id = 4, WorksheetId = ws, GradeId = grade, StartAt = start, IsDeleted = true });   // silinmiş → true
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
        {
            (await ctx.Database.ExecuteSqlRawAsync(AddWorksheetAssignmentIsPlatformWide.BackfillSql)).ShouldBe(2);
            (await ctx.Database.ExecuteSqlRawAsync(AddWorksheetAssignmentIsPlatformWide.BackfillSql)).ShouldBe(0); // idempotent
        }

        await using var check = _db.NewContext();
        var rows = await check.WorksheetAssignments.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.IsPlatformWide);
        rows[1].ShouldBeTrue();
        rows[2].ShouldBeFalse();
        rows[3].ShouldBeFalse();
        rows[4].ShouldBeTrue();
    }

    [Fact]
    public async Task LastRejectedAt_backfill_prefers_the_latest_rejection_audit_then_update_time_then_create_time()
    {
        var auditAt = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);
        var olderAuditAt = auditAt.AddDays(-5);
        var updateTime = new DateTime(2026, 9, 22, 8, 0, 0, DateTimeKind.Utc);
        int withAudit, withUpdateTime, withCreateOnly, pending;
        await using (var ctx = _db.NewContext())
        {
            var t1 = new Teacher { UserId = 1, ApprovalStatus = TeacherApprovalStatus.Rejected };
            var t2 = new Teacher { UserId = 2, ApprovalStatus = TeacherApprovalStatus.Rejected };
            var t3 = new Teacher { UserId = 3, ApprovalStatus = TeacherApprovalStatus.Rejected };
            var t4 = new Teacher { UserId = 4, ApprovalStatus = TeacherApprovalStatus.Pending };
            ctx.Teachers.AddRange(t1, t2, t3, t4);
            await ctx.SaveChangesAsync();
            (withAudit, withUpdateTime, withCreateOnly, pending) = (t1.Id, t2.Id, t3.Id, t4.Id);

            ctx.AdminUserActionLogs.AddRange(
                Log(withAudit, AdminUserAction.TeacherRejected, AdminUserActionOutcome.Succeeded, olderAuditAt),
                Log(withAudit, AdminUserAction.TeacherRejected, AdminUserActionOutcome.Succeeded, auditAt),
                Log(withAudit, AdminUserAction.TeacherRejected, AdminUserActionOutcome.Requested, auditAt.AddDays(1)), // başarısız sayılmaz
                Log(withAudit, AdminUserAction.TeacherApproved, AdminUserActionOutcome.Succeeded, auditAt.AddDays(2))); // başka aksiyon
            await ctx.SaveChangesAsync();

            // SaveChanges audit'i UpdateTime'ı ezer; senaryo değerlerini SQL ile sabitle.
            await ctx.Teachers.Where(t => t.Id == withAudit || t.Id == withUpdateTime)
                .ExecuteUpdateAsync(set => set.SetProperty(t => t.UpdateTime, updateTime));
            await ctx.Teachers.Where(t => t.Id == withCreateOnly)
                .ExecuteUpdateAsync(set => set.SetProperty(t => t.UpdateTime, (DateTime?)null));
        }

        await using (var ctx = _db.NewContext())
        {
            await ctx.Database.ExecuteSqlRawAsync(AddTeacherLastRejectedAt.BackfillSql);
            (await ctx.Database.ExecuteSqlRawAsync(AddTeacherLastRejectedAt.BackfillSql)).ShouldBe(0); // idempotent
        }

        await using var check = _db.NewContext();
        var rows = await check.Teachers.AsNoTracking().ToDictionaryAsync(t => t.Id);
        rows[withAudit].LastRejectedAt.ShouldBe(auditAt);
        rows[withUpdateTime].LastRejectedAt.ShouldBe(updateTime);
        rows[withCreateOnly].LastRejectedAt.ShouldBe(rows[withCreateOnly].CreateTime);
        rows[pending].LastRejectedAt.ShouldBeNull();
    }

    private static AdminUserActionLog Log(int teacherId, AdminUserAction action, AdminUserActionOutcome outcome, DateTime at) => new()
    {
        ActorKeycloakId = "kc-admin",
        Action = action,
        TargetType = AdminUserTargetType.Teacher,
        TargetId = teacherId,
        Outcome = outcome,
        OccurredAtUtc = at
    };
}
