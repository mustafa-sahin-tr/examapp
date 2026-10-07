using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Bookings;
using ExamApp.Api.Models.Dtos.Tutors;
using ExamApp.Api.Models.Dtos.Video;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Bookings;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Video;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #289 (code review Orta-1): askıdaki bağımsız tutor pazar yerinden düşer — aramada listelenmez; public profil,
/// açık slot listesi ve yeni randevu talebi onaysız tutor ile aynı NotFound'u döner. Her testte aynı tutor askıdan
/// önce görünür olduğu için filtre yalnızca askıdan kaynaklanır.
/// </summary>
public class SuspendedTutorVisibilityTests : IDisposable
{
    private const int TutorUserId = 300;
    private const int StudentUserId = 400;

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    public SuspendedTutorVisibilityTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<UserLookupResultDto>>(new List<UserLookupResultDto>
            {
                new() { Id = TutorUserId, FullName = "Tutor", KeycloakId = "kc-tutor" },
                new() { Id = StudentUserId, FullName = "Öğrenci", KeycloakId = "kc-student" }
            }));
    }

    public void Dispose() => _db.Dispose();

    private async Task<(int TutorId, int SlotId)> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var subject = new Subject { Name = "Matematik" };
        ctx.Subjects.Add(subject);
        var tutor = new Teacher
        {
            UserId = TutorUserId, IsIndependentTutor = true, ApprovalStatus = TeacherApprovalStatus.Approved,
            AccountApprovedAt = DateTime.UtcNow, HourlyRate = 100, TeachesOnline = true, Bio = "bio"
        };
        ctx.Teachers.Add(tutor);
        ctx.Students.Add(new Student { UserId = StudentUserId, StudentNumber = "S400" });
        await ctx.SaveChangesAsync();

        tutor.TeacherSubjects.Add(new TeacherSubject { TeacherId = tutor.Id, SubjectId = subject.Id });
        ctx.SetCurrentUser(TutorUserId);
        var slot = new TeacherAvailabilitySlot
        {
            TeacherId = tutor.Id, Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
            StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(11, 0), CreatedAt = DateTime.UtcNow
        };
        ctx.TeacherAvailabilitySlots.Add(slot);
        await ctx.SaveChangesAsync();
        return (tutor.Id, slot.Id);
    }

    private async Task SuspendAsync(int tutorId)
    {
        await using var ctx = _db.NewContext();
        await ctx.Teachers.Where(t => t.Id == tutorId).ExecuteUpdateAsync(s => s
            .SetProperty(t => t.AccountApprovedAt, (DateTime?)null)
            .SetProperty(t => t.AccountSuspendedAt, DateTime.UtcNow)
            .SetProperty(t => t.AccountSuspensionReason, "neden"));
    }

    private TeacherService Teachers(AppDbContext ctx) => new(ctx, _authApi);

    private BookingService Bookings(AppDbContext ctx)
    {
        var tp = TimeProvider.System;
        var recurring = new RecurringAvailabilityService(ctx, tp, NullLogger<RecurringAvailabilityService>.Instance);
        return new BookingService(ctx, _authApi, Substitute.For<IVideoSessionProvider>(), Options.Create(new VideoOptions()),
            tp, recurring, NullLogger<BookingService>.Instance);
    }

    [Fact]
    public async Task Search_does_not_list_a_suspended_tutor()
    {
        var (tutorId, _) = await SeedAsync();
        await using (var ctx = _db.NewContext())
            (await Teachers(ctx).SearchTutorsAsync(new TeacherSearchFilterDto())).Select(r => r.TeacherId).ShouldBe([tutorId]);

        await SuspendAsync(tutorId);

        await using var after = _db.NewContext();
        (await Teachers(after).SearchTutorsAsync(new TeacherSearchFilterDto())).ShouldBeEmpty();
    }

    [Fact]
    public async Task Public_profile_of_a_suspended_tutor_is_not_found()
    {
        var (tutorId, _) = await SeedAsync();
        await using (var ctx = _db.NewContext())
            (await Teachers(ctx).GetPublicProfileAsync(tutorId)).ShouldNotBeNull();

        await SuspendAsync(tutorId);

        await using var after = _db.NewContext();
        (await Teachers(after).GetPublicProfileAsync(tutorId)).ShouldBeNull(); // controller → 404
    }

    [Fact]
    public async Task Open_slots_of_a_suspended_tutor_are_not_found_like_an_unknown_teacher()
    {
        var (tutorId, _) = await SeedAsync();
        await using (var ctx = _db.NewContext())
            (await Bookings(ctx).GetTeacherOpenSlotsAsync(tutorId, 0, 50)).Items.ShouldHaveSingleItem();

        await SuspendAsync(tutorId);

        await using var after = _db.NewContext();
        var result = await Bookings(after).GetTeacherOpenSlotsAsync(tutorId, 0, 50);
        var unknown = await Bookings(after).GetTeacherOpenSlotsAsync(987654, 0, 50);
        result.Success.ShouldBeFalse();
        result.NotFound.ShouldBeTrue();
        result.Message.ShouldBe(unknown.Message); // var/yok ayrımı sızmaz
    }

    [Fact]
    public async Task New_booking_request_on_a_suspended_tutors_slot_is_not_found()
    {
        var (tutorId, slotId) = await SeedAsync();
        await SuspendAsync(tutorId);

        await using (var ctx = _db.NewContext())
        {
            var result = await Bookings(ctx).CreateBookingAsync(StudentUserId, new CreateBookingDto { AvailabilitySlotId = slotId });
            result.Success.ShouldBeFalse();
            result.NotFound.ShouldBeTrue();
        }

        await using var check = _db.NewContext();
        (await check.Bookings.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task New_booking_request_succeeds_for_the_same_tutor_when_not_suspended()
    {
        var (_, slotId) = await SeedAsync();

        await using var ctx = _db.NewContext();
        (await Bookings(ctx).CreateBookingAsync(StudentUserId, new CreateBookingDto { AvailabilitySlotId = slotId }))
            .Success.ShouldBeTrue();
    }
}
