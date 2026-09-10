using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #94: TeacherService.Save, bağımsız öğretmen başvurusu Pending olduğunda
/// TeacherApplicationSubmittedEvent'i outbox'a yazar — okula bağlı kayıtlarda ya da
/// zaten karara bağlanmış/pending kayıtların ilgisiz güncellemelerinde yazmaz.
/// </summary>
public class TeacherServiceOutboxTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    private TeacherService NewService(AppDbContext ctx) => new(ctx, _authApi);

    private static string EventType => OutboxEventRegistry.NameFor<TeacherApplicationSubmittedEvent>();

    [Fact]
    public async Task Save_NewIndependentTutorRegistration_CreatesExactlyOneOutboxRow()
    {
        await using var ctx = _db.NewContext();
        var response = await NewService(ctx).Save(1, new RegisterTeacherDto { IsIndependentTutor = true });

        response.Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        (await check.OutboxMessages.CountAsync(m => m.Type == EventType)).ShouldBe(1);
    }

    [Fact]
    public async Task Save_NewSchoolBoundRegistration_DoesNotCreateOutboxRow()
    {
        await using var ctx = _db.NewContext();
        var response = await NewService(ctx).Save(2, new RegisterTeacherDto { IsIndependentTutor = false });

        response.Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        (await check.OutboxMessages.CountAsync(m => m.Type == EventType)).ShouldBe(0);
    }

    [Fact]
    public async Task Save_ExistingTeacherTransitionsToIndependentPending_CreatesOutboxRow()
    {
        const int userId = 3;
        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.Add(new Teacher
            {
                UserId = userId,
                IsIndependentTutor = false,
                ApprovalStatus = TeacherApprovalStatus.Approved
            });
            await ctx.SaveChangesAsync();
        }

        await using var actCtx = _db.NewContext();
        var response = await NewService(actCtx).Save(userId, new RegisterTeacherDto { IsIndependentTutor = true });

        response.Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        (await check.OutboxMessages.CountAsync(m => m.Type == EventType)).ShouldBe(1);
        var teacher = await check.Teachers.SingleAsync(t => t.UserId == userId);
        teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Pending);
    }

    [Fact]
    public async Task Save_UnrelatedUpdateToAlreadyPendingIndependentTeacher_DoesNotCreateDuplicateOutboxRow()
    {
        const int userId = 4;
        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.Add(new Teacher
            {
                UserId = userId,
                IsIndependentTutor = true,
                ApprovalStatus = TeacherApprovalStatus.Pending
            });
            await ctx.SaveChangesAsync();
        }

        // Unrelated update: still IsIndependentTutor=true (no false->true transition), just re-saving.
        await using var actCtx = _db.NewContext();
        var response = await NewService(actCtx).Save(userId, new RegisterTeacherDto { IsIndependentTutor = true, SchoolId = null });

        response.Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        (await check.OutboxMessages.CountAsync(m => m.Type == EventType)).ShouldBe(0);
    }

    [Fact]
    public async Task Save_UnrelatedUpdateToAlreadyApprovedTeacher_DoesNotCreateOutboxRow()
    {
        const int userId = 5;
        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.Add(new Teacher
            {
                UserId = userId,
                IsIndependentTutor = true,
                ApprovalStatus = TeacherApprovalStatus.Approved
            });
            await ctx.SaveChangesAsync();
        }

        await using var actCtx = _db.NewContext();
        var response = await NewService(actCtx).Save(userId, new RegisterTeacherDto { IsIndependentTutor = true });

        response.Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        (await check.OutboxMessages.CountAsync(m => m.Type == EventType)).ShouldBe(0);
        var teacher = await check.Teachers.SingleAsync(t => t.UserId == userId);
        teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Approved); // unchanged, still no-transition
    }

    public void Dispose() => _db.Dispose();
}
