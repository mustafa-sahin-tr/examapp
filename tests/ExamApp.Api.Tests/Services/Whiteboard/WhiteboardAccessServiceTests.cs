using System.Security.Claims;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Bookings;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Teachers;
using ExamApp.Api.Services.Video;
using ExamApp.Api.Services.Whiteboard;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services.Whiteboard;

/// <summary>
/// issue #98 — tahta yetkisi: yalnızca booking'in öğretmeni ve öğrencisi, booking Approved, katılım penceresi
/// (başlangıç −15 dk / bitiş +30 dk, görüşme odasıyla ortak) içinde; askıdaki/onaysız öğretmen reddedilir.
/// </summary>
public class WhiteboardAccessServiceTests : IDisposable
{
    private const int TeacherId = 10, TeacherUserId = 100;
    private const int StudentId = 20, StudentUserId = 200;
    private const int OtherTeacherId = 11, OtherTeacherUserId = 101;
    private const int OtherStudentId = 21, OtherStudentUserId = 201;

    // Slot 14:00-15:00 → pencere 13:45 - 15:30.
    private static readonly DateOnly SlotDate = new(2026, 6, 15);
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 6, 15, 14, 10, 0, TimeSpan.Zero));
    private readonly TestDb _db = TestDb.Create();
    private readonly IUserProfileProvider _profiles = Substitute.For<IUserProfileProvider>();

    public void Dispose() => _db.Dispose();

    private WhiteboardAccessService NewService(AppDbContext ctx)
    {
        var bookings = new BookingService(ctx, Substitute.For<IAuthApiClient>(), Substitute.For<IVideoSessionProvider>(),
            Options.Create(new VideoOptions()), _clock,
            new RecurringAvailabilityService(ctx, _clock, NullLogger<RecurringAvailabilityService>.Instance),
            NullLogger<BookingService>.Instance);
        return new WhiteboardAccessService(bookings, new ApprovedTeacherGuard(ctx), _profiles, ctx);
    }

    private static ClaimsPrincipal Principal(string sub, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, sub) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private ClaimsPrincipal As(int userId, params string[] roles)
    {
        var sub = $"kc-{userId}";
        _profiles.GetAsync(sub, Arg.Any<CancellationToken>()).Returns(new UserProfileDto { Id = userId, KeycloakId = sub });
        return Principal(sub, roles);
    }

    private async Task<int> SeedAsync(BookingStatus status = BookingStatus.Approved, bool teacherSuspended = false,
        bool teacherApproved = true, TimeOnly? start = null, TimeOnly? end = null)
    {
        await using var ctx = _db.NewContext();
        ctx.Teachers.Add(new Teacher
        {
            Id = TeacherId, UserId = TeacherUserId, ApprovalStatus = TeacherApprovalStatus.Approved, Bio = "t",
            AccountApprovedAt = teacherApproved && !teacherSuspended ? DateTime.UtcNow : null,
            AccountSuspendedAt = teacherSuspended ? DateTime.UtcNow : null,
        });
        ctx.Teachers.Add(new Teacher
        {
            Id = OtherTeacherId, UserId = OtherTeacherUserId, ApprovalStatus = TeacherApprovalStatus.Approved, Bio = "t",
            AccountApprovedAt = DateTime.UtcNow
        });
        ctx.Students.Add(new Student { Id = StudentId, UserId = StudentUserId, StudentNumber = "S20" });
        ctx.Students.Add(new Student { Id = OtherStudentId, UserId = OtherStudentUserId, StudentNumber = "S21" });
        var booking = BookingSeed.Add(ctx, TeacherId, StudentId, status,
            start ?? new TimeOnly(14, 0), end ?? new TimeOnly(15, 0), SlotDate);
        await ctx.SaveChangesAsync();
        return booking.Id;
    }

    private async Task<WhiteboardAccessResult> AuthorizeAsync(ClaimsPrincipal user, int bookingId)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx).AuthorizeAsync(user, bookingId);
    }

    [Fact]
    public async Task Teacher_and_student_of_the_booking_are_allowed_with_their_roles()
    {
        var bookingId = await SeedAsync();

        var teacher = await AuthorizeAsync(As(TeacherUserId, "Teacher"), bookingId);
        var student = await AuthorizeAsync(As(StudentUserId, "Student"), bookingId);

        teacher.Allowed.ShouldBeTrue();
        teacher.Role.ShouldBe(WhiteboardRoles.Teacher);
        teacher.UserId.ShouldBe(TeacherUserId);
        teacher.WindowClosesAtUtc.ShouldBe(new DateTime(2026, 6, 15, 15, 30, 0, DateTimeKind.Utc));
        student.Allowed.ShouldBeTrue();
        student.Role.ShouldBe(WhiteboardRoles.Student);
    }

    [Fact]
    public async Task Approved_booking_whose_slot_was_soft_deleted_is_still_allowed()
    {
        // issue #376: slotu silinmiş onaylı randevu "BookingNotFound" değil; ders yine yapılır.
        var bookingId = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            var slotId = await ctx.Bookings.Where(b => b.Id == bookingId).Select(b => b.AvailabilitySlotId).SingleAsync();
            await ctx.TeacherAvailabilitySlots.Where(s => s.Id == slotId)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.IsDeleted, true));
        }

        var teacher = await AuthorizeAsync(As(TeacherUserId, "Teacher"), bookingId);
        var student = await AuthorizeAsync(As(StudentUserId, "Student"), bookingId);

        teacher.Allowed.ShouldBeTrue(teacher.ErrorCode);
        teacher.WindowClosesAtUtc.ShouldBe(new DateTime(2026, 6, 15, 15, 30, 0, DateTimeKind.Utc));
        student.Allowed.ShouldBeTrue(student.ErrorCode);
    }

    [Fact]
    public async Task Foreign_student_is_rejected()
    {
        var bookingId = await SeedAsync();

        (await AuthorizeAsync(As(OtherStudentUserId, "Student"), bookingId)).ErrorCode
            .ShouldBe(WhiteboardErrorCodes.NotParticipant);
    }

    [Fact]
    public async Task Foreign_teacher_is_rejected()
    {
        var bookingId = await SeedAsync();

        (await AuthorizeAsync(As(OtherTeacherUserId, "Teacher"), bookingId)).ErrorCode
            .ShouldBe(WhiteboardErrorCodes.NotParticipant);
    }

    [Fact]
    public async Task Unknown_booking_is_not_found()
    {
        await SeedAsync();

        (await AuthorizeAsync(As(TeacherUserId, "Teacher"), 9999)).ErrorCode
            .ShouldBe(WhiteboardErrorCodes.BookingNotFound);
    }

    [Theory]
    [InlineData(BookingStatus.Pending)]
    [InlineData(BookingStatus.Rejected)]
    public async Task Not_approved_booking_is_rejected(BookingStatus status)
    {
        var bookingId = await SeedAsync(status);

        (await AuthorizeAsync(As(StudentUserId, "Student"), bookingId)).ErrorCode
            .ShouldBe(WhiteboardErrorCodes.BookingNotApproved);
    }

    [Theory]
    [InlineData(13, 44, WhiteboardErrorCodes.WindowNotOpen)]
    [InlineData(13, 45, null)] // tam açılış anı dahil
    [InlineData(15, 30, null)] // tam kapanış anı dahil
    [InlineData(15, 31, WhiteboardErrorCodes.WindowClosed)]
    public async Task Join_window_is_start_minus_15_to_end_plus_30(int hour, int minute, string? expected)
    {
        var bookingId = await SeedAsync();
        _clock.Now = new DateTimeOffset(2026, 6, 15, hour, minute, 0, TimeSpan.Zero);

        (await AuthorizeAsync(As(StudentUserId, "Student"), bookingId)).ErrorCode.ShouldBe(expected);
    }

    // issue #300: 06-15 23:30 → 06-16 00:30 gün aşan randevu; pencere 23:15 → ertesi gün 01:00.
    [Theory]
    [InlineData(15, 23, 14, WhiteboardErrorCodes.WindowNotOpen)]
    [InlineData(15, 23, 15, null)]
    [InlineData(16, 0, 59, null)]
    [InlineData(16, 1, 0, null)]
    [InlineData(16, 1, 1, WhiteboardErrorCodes.WindowClosed)]
    public async Task Crossing_midnight_booking_window_closes_on_the_next_day(int day, int hour, int minute, string? expected)
    {
        var bookingId = await SeedAsync(start: new TimeOnly(23, 30), end: new TimeOnly(0, 30));
        _clock.Now = new DateTimeOffset(2026, 6, day, hour, minute, 0, TimeSpan.Zero);

        var result = await AuthorizeAsync(As(StudentUserId, "Student"), bookingId);

        result.ErrorCode.ShouldBe(expected);
        if (expected is null)
            result.WindowClosesAtUtc.ShouldBe(new DateTime(2026, 6, 16, 1, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Suspended_teacher_is_rejected()
    {
        var bookingId = await SeedAsync(teacherSuspended: true);

        // issue #298: öğretmene de öğrenciye de aynı nötr kod.
        (await AuthorizeAsync(As(TeacherUserId, "Teacher"), bookingId)).ErrorCode
            .ShouldBe(WhiteboardErrorCodes.TeacherUnavailable);
        (await AuthorizeAsync(As(StudentUserId, "Student"), bookingId)).ErrorCode
            .ShouldBe(WhiteboardErrorCodes.TeacherUnavailable);
    }

    [Fact]
    public async Task Booking_teacher_without_teacher_role_is_still_checked_for_approval()
    {
        var bookingId = await SeedAsync(teacherApproved: false);

        (await AuthorizeAsync(As(TeacherUserId), bookingId)).ErrorCode
            .ShouldBe(WhiteboardErrorCodes.TeacherUnavailable);
    }

    [Fact]
    public async Task Missing_profile_is_rejected()
    {
        var bookingId = await SeedAsync();
        var user = Principal("kc-unknown", "Student");

        (await AuthorizeAsync(user, bookingId)).ErrorCode.ShouldBe(WhiteboardErrorCodes.UserNotResolved);
    }

    [Fact]
    public async Task FindInvalidBoards_reports_cancelled_and_suspended()
    {
        var approved = await SeedAsync();
        int pending;
        await using (var ctx = _db.NewContext())
        {
            var booking = BookingSeed.Add(ctx, OtherTeacherId, StudentId, BookingStatus.Pending, 9, SlotDate);
            await ctx.SaveChangesAsync();
            pending = booking.Id;
        }

        await using (var ctx = _db.NewContext())
        {
            var invalid = await NewService(ctx).FindInvalidBoardsAsync([approved, pending, 9999]);
            invalid.Keys.ShouldBe([pending, 9999], ignoreOrder: true);
            invalid[pending].ShouldBe(WhiteboardCloseReasons.BookingCancelled);
            invalid[9999].ShouldBe(WhiteboardCloseReasons.BookingCancelled);
        }

        await using (var ctx = _db.NewContext())
        {
            var teacher = ctx.Teachers.Single(t => t.Id == TeacherId);
            teacher.AccountSuspendedAt = DateTime.UtcNow;
            teacher.AccountApprovedAt = null;
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
        {
            var invalid = await NewService(ctx).FindInvalidBoardsAsync([approved]);
            invalid[approved].ShouldBe(WhiteboardCloseReasons.TeacherUnavailable);
        }
    }
}
