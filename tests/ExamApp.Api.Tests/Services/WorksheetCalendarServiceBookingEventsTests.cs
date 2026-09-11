using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #96 — WorksheetCalendarService booking events: onaylanmış randevuların
/// takvime eklenmesi, Pending/Rejected'lerin filtrelenmesi, aralık kontrolü.
/// </summary>
public class WorksheetCalendarServiceBookingEventsTests : IDisposable
{
    private const int TeacherId = 10;
    private const int TeacherUserId = 100;
    private const int StudentId = 20;
    private const int StudentUserId = 200;

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    private WorksheetCalendarService NewService(AppDbContext ctx) => new(ctx, _authApi);

    private async Task SeedTeacherAsync(int teacherId, int userId)
    {
        await using var ctx = _db.NewContext();
        ctx.Teachers.Add(new Teacher { Id = teacherId, UserId = userId, ApprovalStatus = TeacherApprovalStatus.Approved, Bio = "test" });
        await ctx.SaveChangesAsync();
    }

    private async Task SeedStudentAsync(int studentId, int userId)
    {
        await using var ctx = _db.NewContext();
        ctx.Students.Add(new Student { Id = studentId, UserId = userId, StudentNumber = $"STU{studentId}" });
        await ctx.SaveChangesAsync();
    }

    private async Task<int> SeedSlotAsync(int teacherId, DateOnly date, TimeOnly startTime, TimeOnly endTime)
    {
        await using var ctx = _db.NewContext();
        ctx.SetCurrentUser(1);
        var slot = new TeacherAvailabilitySlot
        {
            TeacherId = teacherId,
            Date = date,
            StartTime = startTime,
            EndTime = endTime,
            CreatedAt = DateTime.UtcNow
        };
        ctx.TeacherAvailabilitySlots.Add(slot);
        await ctx.SaveChangesAsync();
        return slot.Id;
    }

    private async Task<int> SeedBookingAsync(
        int teacherId, int studentId, int slotId, BookingStatus status = BookingStatus.Approved)
    {
        await using var ctx = _db.NewContext();
        ctx.SetCurrentUser(1);
        var booking = new Booking
        {
            TeacherId = teacherId,
            StudentId = studentId,
            AvailabilitySlotId = slotId,
            Status = status,
            CreatedAt = DateTime.UtcNow,
            DecisionAt = status != BookingStatus.Pending ? DateTime.UtcNow : null
        };
        ctx.Bookings.Add(booking);
        await ctx.SaveChangesAsync();
        return booking.Id;
    }

    [Fact]
    public async Task GetMyCalendarAsync_ApprovedBooking_IncludesBookingEvent()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var slotId = await SeedSlotAsync(TeacherId, date, new TimeOnly(14, 0), new TimeOnly(15, 0));
        await SeedBookingAsync(TeacherId, StudentId, slotId, BookingStatus.Approved);

        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult<IReadOnlyList<UserLookupResultDto>>(new List<UserLookupResultDto>
            {
                new() { Id = TeacherUserId, FullName = "Ayşe Öğretmen", KeycloakId = "kc-teacher" },
                new() { Id = StudentUserId, FullName = "Ali Öğrenci", KeycloakId = "kc-student" }
            }));

        await using var ctx = _db.NewContext();
        var fromUtc = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var toUtc = fromUtc.AddMonths(1);

        var result = await NewService(ctx).GetMyCalendarAsync(StudentId, "kc-student", null, null, fromUtc, toUtc, CancellationToken.None);

        var evt = result.Events.SingleOrDefault(e => e.Kind == "booking");
        evt.ShouldNotBeNull();
        evt!.BookingId.HasValue.ShouldBeTrue();
        evt.BookingId!.Value.ShouldBeGreaterThan(0);
        evt.TeacherId.ShouldBe(TeacherId);
        evt.StudentId.ShouldBe(StudentId);
        evt.TeacherName.ShouldBe("Ayşe Öğretmen");
        evt.StudentName.ShouldBe("Ali Öğrenci");
        // Öğrenci görünümünde başlık karşı tarafı (öğretmeni) göstermeli.
        evt.WorksheetTitle.ShouldBe("Ayşe Öğretmen ile ders");
    }

    [Fact]
    public async Task GetMyCalendarAsync_PendingBooking_ExcludesFromCalendar()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var slotId = await SeedSlotAsync(TeacherId, date, new TimeOnly(14, 0), new TimeOnly(15, 0));
        await SeedBookingAsync(TeacherId, StudentId, slotId, BookingStatus.Pending);

        await using var ctx = _db.NewContext();
        var fromUtc = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var toUtc = fromUtc.AddMonths(1);

        var result = await NewService(ctx).GetMyCalendarAsync(StudentId, "kc-student", null, null, fromUtc, toUtc, CancellationToken.None);

        var evt = result.Events.FirstOrDefault(e => e.Kind == "booking");
        evt.ShouldBeNull();
    }

    [Fact]
    public async Task GetMyCalendarAsync_RejectedBooking_ExcludesFromCalendar()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var slotId = await SeedSlotAsync(TeacherId, date, new TimeOnly(14, 0), new TimeOnly(15, 0));
        await SeedBookingAsync(TeacherId, StudentId, slotId, BookingStatus.Rejected);

        await using var ctx = _db.NewContext();
        var fromUtc = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var toUtc = fromUtc.AddMonths(1);

        var result = await NewService(ctx).GetMyCalendarAsync(StudentId, "kc-student", null, null, fromUtc, toUtc, CancellationToken.None);

        var evt = result.Events.FirstOrDefault(e => e.Kind == "booking");
        evt.ShouldBeNull();
    }

    [Fact]
    public async Task GetTeacherCalendarAsync_ApprovedBooking_IncludesBookingEvent()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var slotId = await SeedSlotAsync(TeacherId, date, new TimeOnly(14, 0), new TimeOnly(15, 0));
        await SeedBookingAsync(TeacherId, StudentId, slotId, BookingStatus.Approved);

        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult<IReadOnlyList<UserLookupResultDto>>(new List<UserLookupResultDto>
            {
                new() { Id = TeacherUserId, FullName = "Ayşe Öğretmen", KeycloakId = "kc-teacher" },
                new() { Id = StudentUserId, FullName = "Ali Öğrenci", KeycloakId = "kc-student" }
            }));

        await using var ctx = _db.NewContext();
        var fromUtc = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var toUtc = fromUtc.AddMonths(1);

        var result = await NewService(ctx).GetTeacherCalendarAsync(TeacherUserId, fromUtc, toUtc, CancellationToken.None);

        var evt = result.Events.SingleOrDefault();
        evt.ShouldNotBeNull();
        evt!.Kind.ShouldBe("booking");
        evt.StudentId.ShouldBe(StudentId);
        evt.StudentName.ShouldBe("Ali Öğrenci");
        evt.TeacherName.ShouldBe("Ayşe Öğretmen");
        // Öğretmen görünümünde başlık karşı tarafı (öğrenciyi) göstermeli, kendi adını değil.
        evt.WorksheetTitle.ShouldBe("Ali Öğrenci ile ders");
    }

    [Fact]
    public async Task GetTeacherCalendarAsync_StudentNameUnresolved_FallsBackToGenericTitle()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var slotId = await SeedSlotAsync(TeacherId, date, new TimeOnly(14, 0), new TimeOnly(15, 0));
        await SeedBookingAsync(TeacherId, StudentId, slotId, BookingStatus.Approved);

        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult<IReadOnlyList<UserLookupResultDto>>(new List<UserLookupResultDto>
            {
                new() { Id = TeacherUserId, FullName = "Ayşe Öğretmen", KeycloakId = "kc-teacher" }
            }));

        await using var ctx = _db.NewContext();
        var fromUtc = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var toUtc = fromUtc.AddMonths(1);

        var result = await NewService(ctx).GetTeacherCalendarAsync(TeacherUserId, fromUtc, toUtc, CancellationToken.None);

        var evt = result.Events.SingleOrDefault();
        evt.ShouldNotBeNull();
        evt!.WorksheetTitle.ShouldBe("Ders randevusu");
    }

    [Fact]
    public async Task GetTeacherCalendarAsync_PendingBooking_ExcludesFromCalendar()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var slotId = await SeedSlotAsync(TeacherId, date, new TimeOnly(14, 0), new TimeOnly(15, 0));
        await SeedBookingAsync(TeacherId, StudentId, slotId, BookingStatus.Pending);

        await using var ctx = _db.NewContext();
        var fromUtc = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var toUtc = fromUtc.AddMonths(1);

        var result = await NewService(ctx).GetTeacherCalendarAsync(TeacherUserId, fromUtc, toUtc, CancellationToken.None);

        result.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetMyCalendarAsync_BookingOutsideDateRange_ExcludedFromResult()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(35)); // Outside one-month range
        var slotId = await SeedSlotAsync(TeacherId, date, new TimeOnly(14, 0), new TimeOnly(15, 0));
        await SeedBookingAsync(TeacherId, StudentId, slotId, BookingStatus.Approved);

        await using var ctx = _db.NewContext();
        var fromUtc = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var toUtc = fromUtc.AddMonths(1); // Doesn't include day 35

        var result = await NewService(ctx).GetMyCalendarAsync(StudentId, "kc-student", null, null, fromUtc, toUtc, CancellationToken.None);

        var evt = result.Events.FirstOrDefault(e => e.Kind == "booking");
        evt.ShouldBeNull();
    }

    [Fact]
    public async Task GetMyCalendarAsync_MultipleApprovedBookings_ReturnsAllInRange()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);

        var date1 = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var date2 = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));

        var slot1 = await SeedSlotAsync(TeacherId, date1, new TimeOnly(14, 0), new TimeOnly(15, 0));
        var slot2 = await SeedSlotAsync(TeacherId, date2, new TimeOnly(14, 0), new TimeOnly(15, 0));

        await SeedBookingAsync(TeacherId, StudentId, slot1, BookingStatus.Approved);
        await SeedBookingAsync(TeacherId, StudentId, slot2, BookingStatus.Approved);

        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult<IReadOnlyList<UserLookupResultDto>>(new List<UserLookupResultDto>
            {
                new() { Id = TeacherUserId, FullName = "Ayşe Öğretmen", KeycloakId = "kc-teacher" },
                new() { Id = StudentUserId, FullName = "Ali Öğrenci", KeycloakId = "kc-student" }
            }));

        await using var ctx = _db.NewContext();
        var fromUtc = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var toUtc = fromUtc.AddMonths(1);

        var result = await NewService(ctx).GetMyCalendarAsync(StudentId, "kc-student", null, null, fromUtc, toUtc, CancellationToken.None);

        var bookingEvts = result.Events.Where(e => e.Kind == "booking").ToList();
        bookingEvts.Count.ShouldBe(2);
    }

    public void Dispose() => _db.Dispose();
}
