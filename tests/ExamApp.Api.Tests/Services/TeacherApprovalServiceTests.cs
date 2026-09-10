using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.TeacherApprovals;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #94: admin onay paneli — bağımsız öğretmen başvurularını listeleme/onaylama/reddetme.
/// </summary>
public class TeacherApprovalServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private const int AdminUserId = 999;
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    private TeacherApprovalService NewService(AppDbContext ctx) => new(ctx, _authApi);

    private static Teacher IndependentPending(int userId) => new()
    {
        UserId = userId,
        IsIndependentTutor = true,
        ApprovalStatus = TeacherApprovalStatus.Pending
    };

    // ---- GetPendingApplicationsAsync ----

    [Fact]
    public async Task GetPendingApplicationsAsync_ReturnsOnlyIndependentPendingTeachers()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.Add(IndependentPending(1)); // eligible
            ctx.Teachers.Add(new Teacher { UserId = 2, IsIndependentTutor = true, ApprovalStatus = TeacherApprovalStatus.Approved });
            ctx.Teachers.Add(new Teacher { UserId = 3, IsIndependentTutor = true, ApprovalStatus = TeacherApprovalStatus.Rejected });
            ctx.Teachers.Add(new Teacher { UserId = 4, IsIndependentTutor = false, ApprovalStatus = TeacherApprovalStatus.Approved });
            // school-bound teacher somehow marked Pending should still be excluded (not independent)
            ctx.Teachers.Add(new Teacher { UserId = 5, IsIndependentTutor = false, ApprovalStatus = TeacherApprovalStatus.Pending });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetPendingApplicationsAsync();

        result.Count.ShouldBe(1);
        result[0].UserId.ShouldBe(1);
    }

    [Fact]
    public async Task GetPendingApplicationsAsync_NoPendingApplications_ReturnsEmptyList()
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).GetPendingApplicationsAsync();

        result.ShouldBeEmpty();
    }

    // ---- ApproveAsync ----

    [Fact]
    public async Task ApproveAsync_ValidPendingIndependentTeacher_SucceedsAndSetsApproved()
    {
        int teacherId;
        await using (var ctx = _db.NewContext())
        {
            var teacher = IndependentPending(10);
            ctx.Teachers.Add(teacher);
            await ctx.SaveChangesAsync();
            teacherId = teacher.Id;
        }

        await using var actCtx = _db.NewContext();
        var response = await NewService(actCtx).ApproveAsync(teacherId, AdminUserId);

        response.Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        var reloaded = await check.Teachers.SingleAsync(t => t.Id == teacherId);
        reloaded.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Approved);
    }

    [Fact]
    public async Task ApproveAsync_AlreadyApprovedTeacher_FailsWithConflictAndDoesNotChangeState()
    {
        int teacherId;
        await using (var ctx = _db.NewContext())
        {
            var teacher = new Teacher { UserId = 11, IsIndependentTutor = true, ApprovalStatus = TeacherApprovalStatus.Approved };
            ctx.Teachers.Add(teacher);
            await ctx.SaveChangesAsync();
            teacherId = teacher.Id;
        }

        await using var actCtx = _db.NewContext();
        var response = await NewService(actCtx).ApproveAsync(teacherId, AdminUserId);

        response.Success.ShouldBeFalse();
        response.Conflict.ShouldBeTrue();

        await using var check = _db.NewContext();
        (await check.Teachers.SingleAsync(t => t.Id == teacherId)).ApprovalStatus.ShouldBe(TeacherApprovalStatus.Approved);
    }

    [Fact]
    public async Task ApproveAsync_AlreadyRejectedTeacher_FailsWithConflictAndDoesNotChangeState()
    {
        int teacherId;
        await using (var ctx = _db.NewContext())
        {
            var teacher = new Teacher
            {
                UserId = 12, IsIndependentTutor = true,
                ApprovalStatus = TeacherApprovalStatus.Rejected, RejectionReason = "önceki neden"
            };
            ctx.Teachers.Add(teacher);
            await ctx.SaveChangesAsync();
            teacherId = teacher.Id;
        }

        await using var actCtx = _db.NewContext();
        var response = await NewService(actCtx).ApproveAsync(teacherId, AdminUserId);

        response.Success.ShouldBeFalse();
        response.Conflict.ShouldBeTrue();

        await using var check = _db.NewContext();
        var reloaded = await check.Teachers.SingleAsync(t => t.Id == teacherId);
        reloaded.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Rejected);
        reloaded.RejectionReason.ShouldBe("önceki neden");
    }

    [Fact]
    public async Task ApproveAsync_NonIndependentTeacherMarkedPending_Fails()
    {
        int teacherId;
        await using (var ctx = _db.NewContext())
        {
            var teacher = new Teacher { UserId = 13, IsIndependentTutor = false, ApprovalStatus = TeacherApprovalStatus.Pending };
            ctx.Teachers.Add(teacher);
            await ctx.SaveChangesAsync();
            teacherId = teacher.Id;
        }

        await using var actCtx = _db.NewContext();
        var response = await NewService(actCtx).ApproveAsync(teacherId, AdminUserId);

        response.Success.ShouldBeFalse();

        await using var check = _db.NewContext();
        (await check.Teachers.SingleAsync(t => t.Id == teacherId)).ApprovalStatus.ShouldBe(TeacherApprovalStatus.Pending);
    }

    [Fact]
    public async Task ApproveAsync_UnknownTeacherId_ReturnsNotFound()
    {
        await using var ctx = _db.NewContext();
        var response = await NewService(ctx).ApproveAsync(12345, AdminUserId);

        response.Success.ShouldBeFalse();
        response.NotFound.ShouldBeTrue();
    }

    // ---- RejectAsync ----

    [Fact]
    public async Task RejectAsync_ValidPendingTeacherWithReason_SucceedsAndStoresReason()
    {
        int teacherId;
        await using (var ctx = _db.NewContext())
        {
            var teacher = IndependentPending(20);
            ctx.Teachers.Add(teacher);
            await ctx.SaveChangesAsync();
            teacherId = teacher.Id;
        }

        await using var actCtx = _db.NewContext();
        var response = await NewService(actCtx).RejectAsync(teacherId, "Belge eksik", AdminUserId);

        response.Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        var reloaded = await check.Teachers.SingleAsync(t => t.Id == teacherId);
        reloaded.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Rejected);
        reloaded.RejectionReason.ShouldBe("Belge eksik");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task RejectAsync_EmptyOrWhitespaceReason_FailsValidation(string? reason)
    {
        int teacherId;
        await using (var ctx = _db.NewContext())
        {
            var teacher = IndependentPending(21);
            ctx.Teachers.Add(teacher);
            await ctx.SaveChangesAsync();
            teacherId = teacher.Id;
        }

        await using var actCtx = _db.NewContext();
        var response = await NewService(actCtx).RejectAsync(teacherId, reason!, AdminUserId);

        response.Success.ShouldBeFalse();

        await using var check = _db.NewContext();
        (await check.Teachers.SingleAsync(t => t.Id == teacherId)).ApprovalStatus.ShouldBe(TeacherApprovalStatus.Pending);
    }

    [Fact]
    public async Task RejectAsync_AlreadyDecidedTeacher_FailsWithConflict()
    {
        int teacherId;
        await using (var ctx = _db.NewContext())
        {
            var teacher = new Teacher { UserId = 22, IsIndependentTutor = true, ApprovalStatus = TeacherApprovalStatus.Approved };
            ctx.Teachers.Add(teacher);
            await ctx.SaveChangesAsync();
            teacherId = teacher.Id;
        }

        await using var actCtx = _db.NewContext();
        var response = await NewService(actCtx).RejectAsync(teacherId, "gerekçe", AdminUserId);

        response.Success.ShouldBeFalse();
        response.Conflict.ShouldBeTrue();

        await using var check = _db.NewContext();
        (await check.Teachers.SingleAsync(t => t.Id == teacherId)).ApprovalStatus.ShouldBe(TeacherApprovalStatus.Approved);
    }

    [Fact]
    public async Task RejectAsync_ReasonExceedsMaxLength_FailsValidation()
    {
        int teacherId;
        await using (var ctx = _db.NewContext())
        {
            var teacher = IndependentPending(23);
            ctx.Teachers.Add(teacher);
            await ctx.SaveChangesAsync();
            teacherId = teacher.Id;
        }

        var tooLong = new string('a', 501);

        await using var actCtx = _db.NewContext();
        var response = await NewService(actCtx).RejectAsync(teacherId, tooLong, AdminUserId);

        response.Success.ShouldBeFalse();

        await using var check = _db.NewContext();
        (await check.Teachers.SingleAsync(t => t.Id == teacherId)).ApprovalStatus.ShouldBe(TeacherApprovalStatus.Pending);
    }

    public void Dispose() => _db.Dispose();
}
