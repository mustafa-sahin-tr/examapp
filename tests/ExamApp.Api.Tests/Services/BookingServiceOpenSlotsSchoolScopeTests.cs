using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Video;
using ExamApp.Api.Services.Bookings;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Services.Video;
using ExamApp.Api.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #190 (güvenlik incelemesi): GET booking/teachers/{id}/slots.
/// Okula bağlı öğretmenin takvimi yalnızca aynı okuldan (veya admin) görünür; farklı okul / okulsuz
/// istek sahibi 404 (mevcut teacherNotFound — var/yok sızmaz). Bağımsız onaylı tutor herkese açık.
/// </summary>
public class BookingServiceOpenSlotsSchoolScopeTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private BookingService NewService(AppDbContext ctx)
    {
        var tp = TimeProvider.System;
        var recurring = new RecurringAvailabilityService(ctx, tp, NullLogger<RecurringAvailabilityService>.Instance);
        return new BookingService(ctx, Substitute.For<IAuthApiClient>(), Substitute.For<IVideoSessionProvider>(),
            Options.Create(new VideoOptions()), tp, recurring, NullLogger<BookingService>.Instance, new SchoolAccessPolicy(ctx));
    }

    private async Task<(int schoolA, int schoolB, int schoolTeacherId, int tutorId)> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var a = new School { Name = "Okul A" };
        var b = new School { Name = "Okul B" };
        ctx.AddRange(a, b);
        await ctx.SaveChangesAsync();

        var schoolTeacher = new Teacher { UserId = 100, SchoolId = a.Id, ApprovalStatus = TeacherApprovalStatus.Approved };
        var tutor = new Teacher { UserId = 101, SchoolId = null, IsIndependentTutor = true, ApprovalStatus = TeacherApprovalStatus.Approved };
        ctx.AddRange(schoolTeacher, tutor);
        await ctx.SaveChangesAsync();

        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        foreach (var t in new[] { schoolTeacher, tutor })
        {
            ctx.SetCurrentUser(t.UserId);
            ctx.TeacherAvailabilitySlots.Add(new TeacherAvailabilitySlot
            {
                TeacherId = t.Id, Date = tomorrow, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(11, 0), CreatedAt = DateTime.UtcNow
            });
        }
        await ctx.SaveChangesAsync();

        return (a.Id, b.Id, schoolTeacher.Id, tutor.Id);
    }

    [Fact]
    public async Task SchoolTeacherSlots_SameSchoolStudent_Returned()
    {
        var (a, _, teacherId, _) = await SeedAsync();
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx).GetTeacherOpenSlotsAsync(teacherId, SchoolScope.For(200, a), 0, 50);

        r.Success.ShouldBeTrue();
        r.Items.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task SchoolTeacherSlots_DifferentSchoolStudent_NotFound()
    {
        var (_, b, teacherId, _) = await SeedAsync();
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx).GetTeacherOpenSlotsAsync(teacherId, SchoolScope.For(200, b), 0, 50);
        var missing = await NewService(ctx).GetTeacherOpenSlotsAsync(99999, SchoolScope.For(200, b), 0, 50);

        r.Success.ShouldBeFalse();
        r.NotFound.ShouldBeTrue();
        r.Message.ShouldBe(missing.Message);
    }

    [Fact]
    public async Task SchoolTeacherSlots_IndependentStudent_NotFound()
    {
        var (_, _, teacherId, _) = await SeedAsync();
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx).GetTeacherOpenSlotsAsync(teacherId, SchoolScope.For(200, null), 0, 50);

        r.NotFound.ShouldBeTrue();
    }

    [Fact]
    public async Task SchoolTeacherSlots_Admin_Returned()
    {
        var (_, _, teacherId, _) = await SeedAsync();
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx).GetTeacherOpenSlotsAsync(teacherId, SchoolScope.Unrestricted(1), 0, 50);

        r.Success.ShouldBeTrue();
        r.Items.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(true)]   // okullu öğrenci (farklı okul)
    [InlineData(false)]  // okulsuz öğrenci
    public async Task IndependentTutorSlots_VisibleToEveryone(bool requesterHasSchool)
    {
        var (_, b, _, tutorId) = await SeedAsync();
        await using var ctx = _db.NewContext();

        var scope = SchoolScope.For(200, requesterHasSchool ? b : null);
        var r = await NewService(ctx).GetTeacherOpenSlotsAsync(tutorId, scope, 0, 50);

        r.Success.ShouldBeTrue();
        r.Items.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task PendingIndependentTutor_StillNotFound()
    {
        await using (var setup = _db.NewContext())
        {
            setup.Teachers.Add(new Teacher { Id = 77, UserId = 177, IsIndependentTutor = true, ApprovalStatus = TeacherApprovalStatus.Pending });
            await setup.SaveChangesAsync();
        }
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx).GetTeacherOpenSlotsAsync(77, SchoolScope.For(200, null), 0, 50);

        r.NotFound.ShouldBeTrue();
    }
}
