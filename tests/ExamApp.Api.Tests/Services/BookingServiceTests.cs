using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Bookings;
using ExamApp.Api.Models.Dtos.Video;
using ExamApp.Api.Services.Bookings;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Video;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #96 — Ders planlama / randevu iş kuralları: müsaitlik slotları, booking oluşturma,
/// onay/ret, çakışma kontrolü, geçmiş tarih reddi, yetkilendirme.
/// </summary>
public class BookingServiceTests : IDisposable
{
    private const int TeacherId = 10;
    private const int TeacherUserId = 100;
    private const int StudentId = 20;
    private const int StudentUserId = 200;
    private const int OtherTeacherId = 11;
    private const int OtherTeacherUserId = 101;

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    private readonly IVideoSessionProvider _videoProvider = Substitute.For<IVideoSessionProvider>();

    /// <summary>
    /// Issue #294: testler duvar saatine değil sabit bir saate bağlıdır (varsayılan 2026-06-15 12:00 UTC).
    /// Eskiden <c>DateTime.UtcNow</c> kullanıldığı için "şimdi + 5 dk" slotu UTC 23:00 sonrası gece yarısını
    /// geçip (23:11 → 00:11) bitişi başlangıçtan önceye düşürüyor, katılım testleri düşüyordu.
    /// </summary>
    private static readonly DateTimeOffset DefaultNow = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly FixedTimeProvider _clock = new(DefaultNow);

    /// <summary>Servise verilen sabit saatin "şimdi"si; test verisi de buna göre kurulur.</summary>
    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    private void SetClock(int hour, int minute) =>
        _clock.Now = new DateTimeOffset(2026, 6, 15, hour, minute, 0, TimeSpan.Zero);

    /// <summary>Issue #97'de eklenen video bağımlılıkları; TimeProvider ile deterministik "now" kontrol ederiz.</summary>
    private BookingService NewService(AppDbContext ctx, TimeProvider? timeProvider = null)
    {
        var tp = timeProvider ?? _clock;
        // Issue #178: GetMySlotsAsync tekrarlayan kural top-up'ını tetikler; gerçek servisle bağlanır.
        var recurring = new RecurringAvailabilityService(ctx, tp,
            new Microsoft.Extensions.Logging.Abstractions.NullLogger<RecurringAvailabilityService>());
        return new BookingService(ctx, _authApi, _videoProvider, Options.Create(new VideoOptions()), tp, recurring,
            new Microsoft.Extensions.Logging.Abstractions.NullLogger<BookingService>(),
            new ExamApp.Api.Services.Tenancy.SchoolAccessPolicy(ctx));
    }

    private async Task<(int id, int userId)> SeedTeacherAsync(
        int teacherId, int userId, TeacherApprovalStatus status = TeacherApprovalStatus.Approved)
    {
        await using var ctx = _db.NewContext();
        var teacher = new Teacher
        {
            Id = teacherId,
            UserId = userId,
            ApprovalStatus = status,
            // issue #298: canlı ders erişimi randevunun öğretmeninin HESAP onayına da bakar (#287 sonrası onaylı öğretmen).
            AccountApprovedAt = status == TeacherApprovalStatus.Approved ? DateTime.UtcNow.AddDays(-30) : null,
            Bio = "test"
        };
        ctx.Teachers.Add(teacher);
        await ctx.SaveChangesAsync();
        return (teacherId, userId);
    }

    private async Task<int> SeedStudentAsync(int studentId, int userId)
    {
        await using var ctx = _db.NewContext();
        var student = new Student { Id = studentId, UserId = userId, StudentNumber = $"STU{studentId}" };
        ctx.Students.Add(student);
        await ctx.SaveChangesAsync();
        return studentId;
    }

    private async Task<int> SeedSlotAsync(
        int teacherId, DateOnly date, TimeOnly startTime, TimeOnly endTime)
    {
        await using var ctx = _db.NewContext();
        ctx.SetCurrentUser(teacherId);
        var slot = new TeacherAvailabilitySlot
        {
            TeacherId = teacherId,
            Date = date,
            StartTime = startTime,
            EndTime = endTime,
            CreatedAt = Now
        };
        ctx.TeacherAvailabilitySlots.Add(slot);
        await ctx.SaveChangesAsync();
        return slot.Id;
    }

    /// <summary>
    /// Sabit saatten 5 dk sonra başlayan (katılım penceresi içinde) slot. Bitiş başlangıç + 1 saattir; gece
    /// yarısını geçecekse 23:59'a kırpılır — üretim kodu gün aşan slotu desteklemiyor (bkz. *_CrossingMidnight_* testleri).
    /// </summary>
    private Task<int> SeedSlotStartingSoonAsync()
    {
        var start = Now.AddMinutes(5);
        var slotStart = new TimeOnly(start.Hour, start.Minute);
        var slotEnd = slotStart.AddHours(1) < slotStart ? new TimeOnly(23, 59) : slotStart.AddHours(1);
        return SeedSlotAsync(TeacherId, DateOnly.FromDateTime(start), slotStart, slotEnd);
    }

    // ------ Slot oluşturma (öğretmen) ------

    [Fact]
    public async Task CreateSlotAsync_ValidFutureSlot_SucceedsAndReturnsSlot()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);

        await using var ctx = _db.NewContext();
        var req = new CreateAvailabilitySlotDto
        {
            Date = DateOnly.FromDateTime(Now.AddDays(1)),
            StartTime = new TimeOnly(14, 0),
            EndTime = new TimeOnly(15, 0)
        };

        var result = await NewService(ctx).CreateSlotAsync(TeacherUserId, req);

        result.Success.ShouldBeTrue();
        result.Slot.ShouldNotBeNull();
        result.Slot!.TeacherId.ShouldBe(TeacherId);
    }

    [Fact]
    public async Task CreateSlotAsync_EndTimeBeforeStartTime_Fails()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);

        await using var ctx = _db.NewContext();
        var req = new CreateAvailabilitySlotDto
        {
            Date = DateOnly.FromDateTime(Now.AddDays(1)),
            StartTime = new TimeOnly(15, 0),
            EndTime = new TimeOnly(14, 0)
        };

        var result = await NewService(ctx).CreateSlotAsync(TeacherUserId, req);

        result.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task CreateSlotAsync_PastDateTime_FailsWithoutNotFound()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);

        await using var ctx = _db.NewContext();
        var pastDate = DateOnly.FromDateTime(Now.AddDays(-1));
        var req = new CreateAvailabilitySlotDto
        {
            Date = pastDate,
            StartTime = new TimeOnly(14, 0),
            EndTime = new TimeOnly(15, 0)
        };

        var result = await NewService(ctx).CreateSlotAsync(TeacherUserId, req);

        result.Success.ShouldBeFalse();
        result.NotFound.ShouldBeFalse();
    }

    [Fact]
    public async Task CreateSlotAsync_TeacherNotApproved_FailsWithForbidden()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId, TeacherApprovalStatus.Pending);

        await using var ctx = _db.NewContext();
        var req = new CreateAvailabilitySlotDto
        {
            Date = DateOnly.FromDateTime(Now.AddDays(1)),
            StartTime = new TimeOnly(14, 0),
            EndTime = new TimeOnly(15, 0)
        };

        var result = await NewService(ctx).CreateSlotAsync(TeacherUserId, req);

        result.Success.ShouldBeFalse();
        result.Forbidden.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateSlotAsync_OverlappingSlot_FailsWithConflict()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        var futureDate = DateOnly.FromDateTime(Now.AddDays(5));
        await SeedSlotAsync(TeacherId, futureDate, new TimeOnly(14, 0), new TimeOnly(15, 0));

        await using var ctx = _db.NewContext();
        var req = new CreateAvailabilitySlotDto
        {
            Date = futureDate,
            StartTime = new TimeOnly(14, 30),
            EndTime = new TimeOnly(15, 30)
        };

        var result = await NewService(ctx).CreateSlotAsync(TeacherUserId, req);

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateSlotAsync_DateBeyondMaxAdvanceWindow_Fails()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);

        await using var ctx = _db.NewContext();
        var req = new CreateAvailabilitySlotDto
        {
            // 90 günlük üst sınırın ötesi — 9999 gibi uçuk tarihler de aynı dalda reddedilir.
            Date = DateOnly.FromDateTime(Now.AddDays(91)),
            StartTime = new TimeOnly(14, 0),
            EndTime = new TimeOnly(15, 0)
        };

        var result = await NewService(ctx).CreateSlotAsync(TeacherUserId, req);

        result.Success.ShouldBeFalse();
        result.NotFound.ShouldBeFalse();
        result.Message.ShouldContain("90");
    }

    [Fact]
    public async Task CreateSlotAsync_DurationLongerThanMax_Fails()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);

        await using var ctx = _db.NewContext();
        var req = new CreateAvailabilitySlotDto
        {
            Date = DateOnly.FromDateTime(Now.AddDays(1)),
            StartTime = new TimeOnly(0, 0),
            EndTime = new TimeOnly(23, 59) // 4 saatlik üst sınırın çok üstünde
        };

        var result = await NewService(ctx).CreateSlotAsync(TeacherUserId, req);

        result.Success.ShouldBeFalse();
        result.NotFound.ShouldBeFalse();
        result.Message.ShouldContain("4");
    }

    [Fact]
    public async Task CreateSlotAsync_ExactlyAtLimits_Succeeds()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);

        await using var ctx = _db.NewContext();
        var req = new CreateAvailabilitySlotDto
        {
            Date = DateOnly.FromDateTime(Now.AddDays(90)), // tam sınır
            StartTime = new TimeOnly(10, 0),
            EndTime = new TimeOnly(14, 0) // tam 4 saat
        };

        var result = await NewService(ctx).CreateSlotAsync(TeacherUserId, req);

        result.Success.ShouldBeTrue();
        result.Slot.ShouldNotBeNull();
    }

    // ------ Slot silme ------

    [Fact]
    public async Task DeleteSlotAsync_OtherTeachersSlot_FailsWithForbidden()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedTeacherAsync(OtherTeacherId, OtherTeacherUserId);
        var slotId = await SeedSlotAsync(TeacherId, DateOnly.FromDateTime(Now.AddDays(5)),
            new TimeOnly(14, 0), new TimeOnly(15, 0));

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).DeleteSlotAsync(OtherTeacherUserId, slotId);

        result.Success.ShouldBeFalse();
        result.Forbidden.ShouldBeTrue();
    }

    [Fact]
    public async Task DeleteSlotAsync_SlotWithActiveBooking_FailsWithConflict()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);
        var slotId = await SeedSlotAsync(TeacherId, DateOnly.FromDateTime(Now.AddDays(5)),
            new TimeOnly(14, 0), new TimeOnly(15, 0));

        // Create pending booking
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(StudentUserId);
            var booking = new Booking
            {
                TeacherId = TeacherId,
                StudentId = StudentId,
                AvailabilitySlotId = slotId,
                Status = BookingStatus.Pending,
                CreatedAt = Now
            };
            ctx.Bookings.Add(booking);
            await ctx.SaveChangesAsync();
        }

        await using var ctxDelete = _db.NewContext();
        var result = await NewService(ctxDelete).DeleteSlotAsync(TeacherUserId, slotId);

        result.Success.ShouldBeFalse();
    }

    // ------ Booking oluşturma ------

    [Fact]
    public async Task CreateBookingAsync_ValidSlot_CreatesBookingInPendingStatus()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);
        var slotId = await SeedSlotAsync(TeacherId, DateOnly.FromDateTime(Now.AddDays(5)),
            new TimeOnly(14, 0), new TimeOnly(15, 0));

        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult<IReadOnlyList<UserLookupResultDto>>(new List<UserLookupResultDto>
            {
                new() { Id = StudentUserId, FullName = "Ali Öğrenci", KeycloakId = "kc-student" },
                new() { Id = TeacherUserId, FullName = "Ayşe Öğretmen", KeycloakId = "kc-teacher" }
            }));

        await using var ctx = _db.NewContext();
        var req = new CreateBookingDto { AvailabilitySlotId = slotId };
        var result = await NewService(ctx).CreateBookingAsync(StudentUserId, req, CancellationToken.None);

        result.Success.ShouldBeTrue();
        result.Booking.ShouldNotBeNull();
        result.Booking!.Status.ShouldBe(nameof(BookingStatus.Pending));
    }

    [Fact]
    public async Task CreateBookingAsync_SlotNotFound_ReturnsNotFound()
    {
        await SeedStudentAsync(StudentId, StudentUserId);

        await using var ctx = _db.NewContext();
        var req = new CreateBookingDto { AvailabilitySlotId = 999 };
        var result = await NewService(ctx).CreateBookingAsync(StudentUserId, req, CancellationToken.None);

        result.Success.ShouldBeFalse();
        result.NotFound.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateBookingAsync_SlotAlreadyBooked_FailsWithConflict()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);
        var slotId = await SeedSlotAsync(TeacherId, DateOnly.FromDateTime(Now.AddDays(5)),
            new TimeOnly(14, 0), new TimeOnly(15, 0));

        // First booking
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(StudentUserId);
            var booking = new Booking
            {
                TeacherId = TeacherId,
                StudentId = StudentId,
                AvailabilitySlotId = slotId,
                Status = BookingStatus.Pending,
                CreatedAt = Now
            };
            ctx.Bookings.Add(booking);
            await ctx.SaveChangesAsync();
        }

        // Second booking attempt
        await using var ctxSecond = _db.NewContext();
        var req = new CreateBookingDto { AvailabilitySlotId = slotId };
        var result = await NewService(ctxSecond).CreateBookingAsync(StudentUserId, req, CancellationToken.None);

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
    }

    // ------ Booking approval ------

    [Fact]
    public async Task ApproveBookingAsync_PendingBooking_SucceedsAndChangesStatus()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);
        var slotId = await SeedSlotAsync(TeacherId, DateOnly.FromDateTime(Now.AddDays(5)),
            new TimeOnly(14, 0), new TimeOnly(15, 0));

        int bookingId;
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(StudentUserId);
            var booking = new Booking
            {
                TeacherId = TeacherId,
                StudentId = StudentId,
                AvailabilitySlotId = slotId,
                Status = BookingStatus.Pending,
                CreatedAt = Now
            };
            ctx.Bookings.Add(booking);
            await ctx.SaveChangesAsync();
            bookingId = booking.Id;
        }

        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult<IReadOnlyList<UserLookupResultDto>>(new List<UserLookupResultDto>
            {
                new() { Id = TeacherUserId, FullName = "Ayşe Öğretmen", KeycloakId = "kc-teacher" },
                new() { Id = StudentUserId, FullName = "Ali Öğrenci", KeycloakId = "kc-student" }
            }));

        await using var ctxApprove = _db.NewContext();
        var result = await NewService(ctxApprove).ApproveBookingAsync(TeacherUserId, bookingId, CancellationToken.None);

        result.Success.ShouldBeTrue();
        result.Booking!.Status.ShouldBe(nameof(BookingStatus.Approved));
    }

    [Fact]
    public async Task ApproveBookingAsync_OtherTeachersBooking_FailsWithForbidden()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedTeacherAsync(OtherTeacherId, OtherTeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);
        var slotId = await SeedSlotAsync(TeacherId, DateOnly.FromDateTime(Now.AddDays(5)),
            new TimeOnly(14, 0), new TimeOnly(15, 0));

        int bookingId;
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(StudentUserId);
            var booking = new Booking
            {
                TeacherId = TeacherId,
                StudentId = StudentId,
                AvailabilitySlotId = slotId,
                Status = BookingStatus.Pending,
                CreatedAt = Now
            };
            ctx.Bookings.Add(booking);
            await ctx.SaveChangesAsync();
            bookingId = booking.Id;
        }

        await using var ctxApprove = _db.NewContext();
        var result = await NewService(ctxApprove).ApproveBookingAsync(OtherTeacherUserId, bookingId, CancellationToken.None);

        result.Success.ShouldBeFalse();
        result.Forbidden.ShouldBeTrue();
    }

    [Fact]
    public async Task ApproveBookingAsync_AlreadyApprovedBooking_FailsWithStateError()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);
        var slotId = await SeedSlotAsync(TeacherId, DateOnly.FromDateTime(Now.AddDays(5)),
            new TimeOnly(14, 0), new TimeOnly(15, 0));

        int bookingId;
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(StudentUserId);
            var booking = new Booking
            {
                TeacherId = TeacherId,
                StudentId = StudentId,
                AvailabilitySlotId = slotId,
                Status = BookingStatus.Approved,
                CreatedAt = Now,
                DecisionAt = Now
            };
            ctx.Bookings.Add(booking);
            await ctx.SaveChangesAsync();
            bookingId = booking.Id;
        }

        await using var ctxApprove = _db.NewContext();
        var result = await NewService(ctxApprove).ApproveBookingAsync(TeacherUserId, bookingId, CancellationToken.None);

        result.Success.ShouldBeFalse();
    }

    // ------ Booking rejection ------

    [Fact]
    public async Task RejectBookingAsync_PendingBooking_SucceedsWithReason()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);
        var slotId = await SeedSlotAsync(TeacherId, DateOnly.FromDateTime(Now.AddDays(5)),
            new TimeOnly(14, 0), new TimeOnly(15, 0));

        int bookingId;
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(StudentUserId);
            var booking = new Booking
            {
                TeacherId = TeacherId,
                StudentId = StudentId,
                AvailabilitySlotId = slotId,
                Status = BookingStatus.Pending,
                CreatedAt = Now
            };
            ctx.Bookings.Add(booking);
            await ctx.SaveChangesAsync();
            bookingId = booking.Id;
        }

        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult<IReadOnlyList<UserLookupResultDto>>(new List<UserLookupResultDto>
            {
                new() { Id = TeacherUserId, FullName = "Ayşe Öğretmen", KeycloakId = "kc-teacher" },
                new() { Id = StudentUserId, FullName = "Ali Öğrenci", KeycloakId = "kc-student" }
            }));

        await using var ctxReject = _db.NewContext();
        var result = await NewService(ctxReject).RejectBookingAsync(TeacherUserId, bookingId, "Çakışma var.", CancellationToken.None);

        result.Success.ShouldBeTrue();
        result.Booking!.Status.ShouldBe(nameof(BookingStatus.Rejected));
        result.Booking.RejectionReason.ShouldBe("Çakışma var.");
    }

    [Fact]
    public async Task RejectBookingAsync_WithoutReason_SucceedsWithoutReason()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);
        var slotId = await SeedSlotAsync(TeacherId, DateOnly.FromDateTime(Now.AddDays(5)),
            new TimeOnly(14, 0), new TimeOnly(15, 0));

        int bookingId;
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(StudentUserId);
            var booking = new Booking
            {
                TeacherId = TeacherId,
                StudentId = StudentId,
                AvailabilitySlotId = slotId,
                Status = BookingStatus.Pending,
                CreatedAt = Now
            };
            ctx.Bookings.Add(booking);
            await ctx.SaveChangesAsync();
            bookingId = booking.Id;
        }

        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult<IReadOnlyList<UserLookupResultDto>>(new List<UserLookupResultDto>
            {
                new() { Id = TeacherUserId, FullName = "Ayşe Öğretmen", KeycloakId = "kc-teacher" },
                new() { Id = StudentUserId, FullName = "Ali Öğrenci", KeycloakId = "kc-student" }
            }));

        await using var ctxReject = _db.NewContext();
        var result = await NewService(ctxReject).RejectBookingAsync(TeacherUserId, bookingId, null, CancellationToken.None);

        result.Success.ShouldBeTrue();
        result.Booking!.RejectionReason.ShouldBeNull();
    }

    // ------ Video session (issue #97) ------

    [Fact]
    public async Task GetVideoSessionAsync_BookingNotFound_ReturnsNotFound()
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).GetVideoSessionAsync(TeacherUserId, 9999, CancellationToken.None);

        result.Success.ShouldBeFalse();
        result.NotFound.ShouldBeTrue();
    }

    [Fact]
    public async Task GetVideoSessionAsync_CallerNotTeacherOrStudent_ReturnsForbidden()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedTeacherAsync(OtherTeacherId, OtherTeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);
        var slotId = await SeedSlotAsync(TeacherId, DateOnly.FromDateTime(Now.AddDays(5)),
            new TimeOnly(14, 0), new TimeOnly(15, 0));

        int bookingId;
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(StudentUserId);
            var booking = new Booking
            {
                TeacherId = TeacherId,
                StudentId = StudentId,
                AvailabilitySlotId = slotId,
                Status = BookingStatus.Approved,
                CreatedAt = Now,
                DecisionAt = Now
            };
            ctx.Bookings.Add(booking);
            await ctx.SaveChangesAsync();
            bookingId = booking.Id;
        }

        _videoProvider.CreateOrJoinSessionAsync(Arg.Any<VideoSessionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new VideoSessionDto { Provider = "Jitsi", RoomName = "test", Domain = "localhost", BaseUrl = "http://localhost", JoinUrl = "http://localhost/test?jwt=x", Token = "x", ExpiresAt = Now.AddMinutes(180), IsModerator = false }));

        await using var ctxVideo = _db.NewContext();
        var result = await NewService(ctxVideo).GetVideoSessionAsync(OtherTeacherUserId, bookingId, CancellationToken.None);

        result.Success.ShouldBeFalse();
        result.Forbidden.ShouldBeTrue();
    }

    [Fact]
    public async Task GetVideoSessionAsync_BookingNotApproved_ReturnsConflict()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);
        var slotId = await SeedSlotAsync(TeacherId, DateOnly.FromDateTime(Now.AddDays(5)),
            new TimeOnly(14, 0), new TimeOnly(15, 0));

        int bookingId;
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(StudentUserId);
            var booking = new Booking
            {
                TeacherId = TeacherId,
                StudentId = StudentId,
                AvailabilitySlotId = slotId,
                Status = BookingStatus.Pending, // Not approved
                CreatedAt = Now
            };
            ctx.Bookings.Add(booking);
            await ctx.SaveChangesAsync();
            bookingId = booking.Id;
        }

        await using var ctxVideo = _db.NewContext();
        var result = await NewService(ctxVideo).GetVideoSessionAsync(TeacherUserId, bookingId, CancellationToken.None);

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
    }

    [Fact]
    public async Task GetVideoSessionAsync_BeforeJoinWindow_ReturnsConflict()
    {
        // Slot: 2026-09-15 10:30-11:30 UTC
        // Window opens: 10:15 UTC (start - 15 min)
        // Window closes: 12:00 UTC (end + 30 min)
        // Test: now = 10:14 → before window opens → Conflict

        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);

        var slotDate = new DateOnly(2026, 9, 15);
        var slotStart = new TimeOnly(10, 30);
        var slotEnd = new TimeOnly(11, 30);
        var slotId = await SeedSlotAsync(TeacherId, slotDate, slotStart, slotEnd);

        int bookingId;
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(StudentUserId);
            var booking = new Booking
            {
                TeacherId = TeacherId,
                StudentId = StudentId,
                AvailabilitySlotId = slotId,
                Status = BookingStatus.Approved,
                CreatedAt = Now,
                DecisionAt = Now
            };
            ctx.Bookings.Add(booking);
            await ctx.SaveChangesAsync();
            bookingId = booking.Id;
        }

        var fakeNow = new DateTime(2026, 9, 15, 10, 14, 0, DateTimeKind.Utc);
        await using var ctxVideo = _db.NewContext();
        var result = await NewService(ctxVideo, new FixedTimeProvider(new DateTimeOffset(fakeNow))).GetVideoSessionAsync(TeacherUserId, bookingId, CancellationToken.None);

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
        result.Message.ShouldContain("15");
    }

    [Fact]
    public async Task GetVideoSessionAsync_AtJoinWindowStart_Succeeds()
    {
        // Slot: 2026-09-15 10:30-11:30 UTC
        // Window opens: 10:15 UTC
        // Test: now = 10:15 → exactly at window open → Success

        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);

        var slotDate = new DateOnly(2026, 9, 15);
        var slotStart = new TimeOnly(10, 30);
        var slotEnd = new TimeOnly(11, 30);
        var slotId = await SeedSlotAsync(TeacherId, slotDate, slotStart, slotEnd);

        int bookingId;
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(StudentUserId);
            var booking = new Booking
            {
                TeacherId = TeacherId,
                StudentId = StudentId,
                AvailabilitySlotId = slotId,
                Status = BookingStatus.Approved,
                CreatedAt = Now,
                DecisionAt = Now
            };
            ctx.Bookings.Add(booking);
            await ctx.SaveChangesAsync();
            bookingId = booking.Id;
        }

        _videoProvider.CreateOrJoinSessionAsync(Arg.Any<VideoSessionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new VideoSessionDto { Provider = "Jitsi", RoomName = "test", Domain = "localhost", BaseUrl = "http://localhost", JoinUrl = "http://localhost/test?jwt=x", Token = "x", ExpiresAt = Now.AddMinutes(180), IsModerator = true }));

        var fakeNow = new DateTime(2026, 9, 15, 10, 15, 0, DateTimeKind.Utc);
        await using var ctxVideo = _db.NewContext();
        var result = await NewService(ctxVideo, new FixedTimeProvider(new DateTimeOffset(fakeNow))).GetVideoSessionAsync(TeacherUserId, bookingId, CancellationToken.None);

        result.Success.ShouldBeTrue();
        result.Session.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetVideoSessionAsync_DuringJoinWindow_Succeeds()
    {
        // Slot: 2026-09-15 10:30-11:30 UTC
        // Window opens: 10:15 UTC
        // Test: now = 10:16 → during window (before slot start) → Success

        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);

        var slotDate = new DateOnly(2026, 9, 15);
        var slotStart = new TimeOnly(10, 30);
        var slotEnd = new TimeOnly(11, 30);
        var slotId = await SeedSlotAsync(TeacherId, slotDate, slotStart, slotEnd);

        int bookingId;
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(StudentUserId);
            var booking = new Booking
            {
                TeacherId = TeacherId,
                StudentId = StudentId,
                AvailabilitySlotId = slotId,
                Status = BookingStatus.Approved,
                CreatedAt = Now,
                DecisionAt = Now
            };
            ctx.Bookings.Add(booking);
            await ctx.SaveChangesAsync();
            bookingId = booking.Id;
        }

        _videoProvider.CreateOrJoinSessionAsync(Arg.Any<VideoSessionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new VideoSessionDto { Provider = "Jitsi", RoomName = "test", Domain = "localhost", BaseUrl = "http://localhost", JoinUrl = "http://localhost/test?jwt=x", Token = "x", ExpiresAt = Now.AddMinutes(180), IsModerator = true }));

        var fakeNow = new DateTime(2026, 9, 15, 10, 16, 0, DateTimeKind.Utc);
        await using var ctxVideo = _db.NewContext();
        var result = await NewService(ctxVideo, new FixedTimeProvider(new DateTimeOffset(fakeNow))).GetVideoSessionAsync(TeacherUserId, bookingId, CancellationToken.None);

        result.Success.ShouldBeTrue();
        result.Session.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetVideoSessionAsync_AtJoinWindowEnd_Succeeds()
    {
        // Slot: 2026-09-15 10:30-11:30 UTC
        // Window closes: 12:00 UTC (end + 30 min)
        // Test: now = 12:00 → exactly at window close → Success

        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);

        var slotDate = new DateOnly(2026, 9, 15);
        var slotStart = new TimeOnly(10, 30);
        var slotEnd = new TimeOnly(11, 30);
        var slotId = await SeedSlotAsync(TeacherId, slotDate, slotStart, slotEnd);

        int bookingId;
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(StudentUserId);
            var booking = new Booking
            {
                TeacherId = TeacherId,
                StudentId = StudentId,
                AvailabilitySlotId = slotId,
                Status = BookingStatus.Approved,
                CreatedAt = Now,
                DecisionAt = Now
            };
            ctx.Bookings.Add(booking);
            await ctx.SaveChangesAsync();
            bookingId = booking.Id;
        }

        _videoProvider.CreateOrJoinSessionAsync(Arg.Any<VideoSessionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new VideoSessionDto { Provider = "Jitsi", RoomName = "test", Domain = "localhost", BaseUrl = "http://localhost", JoinUrl = "http://localhost/test?jwt=x", Token = "x", ExpiresAt = Now.AddMinutes(180), IsModerator = true }));

        var fakeNow = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);
        await using var ctxVideo = _db.NewContext();
        var result = await NewService(ctxVideo, new FixedTimeProvider(new DateTimeOffset(fakeNow))).GetVideoSessionAsync(TeacherUserId, bookingId, CancellationToken.None);

        result.Success.ShouldBeTrue();
        result.Session.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetVideoSessionAsync_AfterJoinWindow_ReturnsConflict()
    {
        // Slot: 2026-09-15 10:30-11:30 UTC
        // Window closes: 12:00 UTC (end + 30 min)
        // Test: now = 12:01 → after window closes → Conflict

        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);

        var slotDate = new DateOnly(2026, 9, 15);
        var slotStart = new TimeOnly(10, 30);
        var slotEnd = new TimeOnly(11, 30);
        var slotId = await SeedSlotAsync(TeacherId, slotDate, slotStart, slotEnd);

        int bookingId;
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(StudentUserId);
            var booking = new Booking
            {
                TeacherId = TeacherId,
                StudentId = StudentId,
                AvailabilitySlotId = slotId,
                Status = BookingStatus.Approved,
                CreatedAt = Now,
                DecisionAt = Now
            };
            ctx.Bookings.Add(booking);
            await ctx.SaveChangesAsync();
            bookingId = booking.Id;
        }

        var fakeNow = new DateTime(2026, 9, 15, 12, 1, 0, DateTimeKind.Utc);
        await using var ctxVideo = _db.NewContext();
        var result = await NewService(ctxVideo, new FixedTimeProvider(new DateTimeOffset(fakeNow))).GetVideoSessionAsync(TeacherUserId, bookingId, CancellationToken.None);

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
        result.Message.ShouldContain("30");
    }

    // Issue #294: öğle ve gece yarısına yakın saatlerde aynı sonuç (23:58 → slot ertesi gün 00:03'te başlar).
    [Theory]
    [InlineData(12, 0)]
    [InlineData(23, 30)]
    [InlineData(23, 58)]
    public async Task GetVideoSessionAsync_TeacherAccess_Succeeds(int hour, int minute)
    {
        SetClock(hour, minute);
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);

        var slotId = await SeedSlotStartingSoonAsync();

        int bookingId;
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(StudentUserId);
            var booking = new Booking
            {
                TeacherId = TeacherId,
                StudentId = StudentId,
                AvailabilitySlotId = slotId,
                Status = BookingStatus.Approved,
                CreatedAt = Now,
                DecisionAt = Now
            };
            ctx.Bookings.Add(booking);
            await ctx.SaveChangesAsync();
            bookingId = booking.Id;
        }

        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult<IReadOnlyList<UserLookupResultDto>>(new List<UserLookupResultDto>
            {
                new() { Id = TeacherUserId, FullName = "Teacher Name", KeycloakId = "kc-teacher" }
            }));

        _videoProvider.CreateOrJoinSessionAsync(Arg.Any<VideoSessionRequest>(), Arg.Any<CancellationToken>())
            .Returns(x =>
            {
                var req = (VideoSessionRequest)x[0];
                return Task.FromResult(new VideoSessionDto
                {
                    Provider = "Jitsi",
                    RoomName = $"booking-{req.BookingId}-xxx",
                    Domain = "localhost",
                    BaseUrl = "http://localhost",
                    JoinUrl = "http://localhost/test?jwt=x",
                    Token = "x",
                    ExpiresAt = Now.AddMinutes(180),
                    IsModerator = true
                });
            });

        await using var ctxVideo = _db.NewContext();
        var result = await NewService(ctxVideo).GetVideoSessionAsync(TeacherUserId, bookingId, CancellationToken.None);

        result.Success.ShouldBeTrue();
        result.Session.ShouldNotBeNull();
        result.Session!.IsModerator.ShouldBeTrue();
    }

    // Issue #294: öğle ve gece yarısına yakın saatlerde aynı sonuç (23:58 → slot ertesi gün 00:03'te başlar).
    [Theory]
    [InlineData(12, 0)]
    [InlineData(23, 30)]
    [InlineData(23, 58)]
    public async Task GetVideoSessionAsync_StudentAccess_Succeeds(int hour, int minute)
    {
        SetClock(hour, minute);
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);

        var slotId = await SeedSlotStartingSoonAsync();

        int bookingId;
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(StudentUserId);
            var booking = new Booking
            {
                TeacherId = TeacherId,
                StudentId = StudentId,
                AvailabilitySlotId = slotId,
                Status = BookingStatus.Approved,
                CreatedAt = Now,
                DecisionAt = Now
            };
            ctx.Bookings.Add(booking);
            await ctx.SaveChangesAsync();
            bookingId = booking.Id;
        }

        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult<IReadOnlyList<UserLookupResultDto>>(new List<UserLookupResultDto>
            {
                new() { Id = StudentUserId, FullName = "Student Name", KeycloakId = "kc-student" }
            }));

        _videoProvider.CreateOrJoinSessionAsync(Arg.Any<VideoSessionRequest>(), Arg.Any<CancellationToken>())
            .Returns(x =>
            {
                var req = (VideoSessionRequest)x[0];
                return Task.FromResult(new VideoSessionDto
                {
                    Provider = "Jitsi",
                    RoomName = $"booking-{req.BookingId}-xxx",
                    Domain = "localhost",
                    BaseUrl = "http://localhost",
                    JoinUrl = "http://localhost/test?jwt=x",
                    Token = "x",
                    ExpiresAt = Now.AddMinutes(180),
                    IsModerator = false
                });
            });

        await using var ctxVideo = _db.NewContext();
        var result = await NewService(ctxVideo).GetVideoSessionAsync(StudentUserId, bookingId, CancellationToken.None);

        result.Success.ShouldBeTrue();
        result.Session.ShouldNotBeNull();
        result.Session!.IsModerator.ShouldBeFalse();
    }

    // Issue #294: öğle ve gece yarısına yakın saatlerde aynı sonuç (23:58 → slot ertesi gün 00:03'te başlar).
    [Theory]
    [InlineData(12, 0)]
    [InlineData(23, 30)]
    [InlineData(23, 58)]
    public async Task GetVideoSessionAsync_PassesCorrectRoleToProvider(int hour, int minute)
    {
        SetClock(hour, minute);
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);

        var slotId = await SeedSlotStartingSoonAsync();

        int bookingId;
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(StudentUserId);
            var booking = new Booking
            {
                TeacherId = TeacherId,
                StudentId = StudentId,
                AvailabilitySlotId = slotId,
                Status = BookingStatus.Approved,
                CreatedAt = Now,
                DecisionAt = Now
            };
            ctx.Bookings.Add(booking);
            await ctx.SaveChangesAsync();
            bookingId = booking.Id;
        }

        var capturedRequest = (VideoSessionRequest?)null;
        _videoProvider.CreateOrJoinSessionAsync(Arg.Any<VideoSessionRequest>(), Arg.Any<CancellationToken>())
            .Returns(x =>
            {
                capturedRequest = (VideoSessionRequest)x[0];
                return Task.FromResult(new VideoSessionDto
                {
                    Provider = "Jitsi",
                    RoomName = "test",
                    Domain = "localhost",
                    BaseUrl = "http://localhost",
                    JoinUrl = "http://localhost/test?jwt=x",
                    Token = "x",
                    ExpiresAt = Now.AddMinutes(180),
                    IsModerator = true
                });
            });

        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult<IReadOnlyList<UserLookupResultDto>>(new List<UserLookupResultDto>
            {
                new() { Id = TeacherUserId, FullName = "Teacher Name", KeycloakId = "kc-teacher" }
            }));

        await using var ctxVideo = _db.NewContext();
        var result = await NewService(ctxVideo).GetVideoSessionAsync(TeacherUserId, bookingId, CancellationToken.None);

        result.Success.ShouldBeTrue();
        capturedRequest.ShouldNotBeNull();
        capturedRequest!.BookingId.ShouldBe(bookingId);
        capturedRequest.ParticipantRole.ShouldBe(VideoParticipantRoles.Teacher);
        capturedRequest.ParticipantUserId.ShouldBe(TeacherUserId);
    }

    // Issue #294: öğle ve gece yarısına yakın saatlerde aynı sonuç (23:58 → slot ertesi gün 00:03'te başlar).
    [Theory]
    [InlineData(12, 0)]
    [InlineData(23, 30)]
    [InlineData(23, 58)]
    public async Task GetVideoSessionAsync_PassesCorrectBookingIdToProvider(int hour, int minute)
    {
        SetClock(hour, minute);
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);

        var slotId = await SeedSlotStartingSoonAsync();

        int bookingId;
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(StudentUserId);
            var booking = new Booking
            {
                TeacherId = TeacherId,
                StudentId = StudentId,
                AvailabilitySlotId = slotId,
                Status = BookingStatus.Approved,
                CreatedAt = Now,
                DecisionAt = Now
            };
            ctx.Bookings.Add(booking);
            await ctx.SaveChangesAsync();
            bookingId = booking.Id;
        }

        var capturedRequest = (VideoSessionRequest?)null;
        _videoProvider.CreateOrJoinSessionAsync(Arg.Any<VideoSessionRequest>(), Arg.Any<CancellationToken>())
            .Returns(x =>
            {
                capturedRequest = (VideoSessionRequest)x[0];
                return Task.FromResult(new VideoSessionDto
                {
                    Provider = "Jitsi",
                    RoomName = "test",
                    Domain = "localhost",
                    BaseUrl = "http://localhost",
                    JoinUrl = "http://localhost/test?jwt=x",
                    Token = "x",
                    ExpiresAt = Now.AddMinutes(180),
                    IsModerator = false
                });
            });

        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult<IReadOnlyList<UserLookupResultDto>>(new List<UserLookupResultDto>
            {
                new() { Id = StudentUserId, FullName = "Student Name", KeycloakId = "kc-student" }
            }));

        await using var ctxVideo = _db.NewContext();
        var result = await NewService(ctxVideo).GetVideoSessionAsync(StudentUserId, bookingId, CancellationToken.None);

        result.Success.ShouldBeTrue();
        capturedRequest.ShouldNotBeNull();
        capturedRequest!.BookingId.ShouldBe(bookingId);
    }

    // Issue #294: öğle ve gece yarısına yakın saatlerde aynı sonuç (23:58 → slot ertesi gün 00:03'te başlar).
    [Theory]
    [InlineData(12, 0)]
    [InlineData(23, 30)]
    [InlineData(23, 58)]
    public async Task GetVideoSessionAsync_ProviderThrowsException_ReturnsConflict(int hour, int minute)
    {
        SetClock(hour, minute);
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);

        var slotId = await SeedSlotStartingSoonAsync();

        int bookingId;
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(StudentUserId);
            var booking = new Booking
            {
                TeacherId = TeacherId,
                StudentId = StudentId,
                AvailabilitySlotId = slotId,
                Status = BookingStatus.Approved,
                CreatedAt = Now,
                DecisionAt = Now
            };
            ctx.Bookings.Add(booking);
            await ctx.SaveChangesAsync();
            bookingId = booking.Id;
        }

        // Provider throws exception
        _videoProvider.CreateOrJoinSessionAsync(Arg.Any<VideoSessionRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<VideoSessionDto>(new InvalidOperationException("Provider error")));

        await using var ctxVideo = _db.NewContext();
        var result = await NewService(ctxVideo).GetVideoSessionAsync(TeacherUserId, bookingId, CancellationToken.None);

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
    }

    // ------ Gece yarısını geçen slot (issue #294) ------
    // BUG #300: gün aşan slot desteklenmiyor. Bu testler HATALI mevcut davranışı belgeler; #300 düzeltilince
    // beklentiler tersine çevrilmeli (Success=true / EndUtc > StartUtc / pencere açılır).
    // Üretim kodu gün aşan slotu desteklemiyor: slot tek bir Date + TimeOnly başlangıç/bitiş taşıyor ve bitiş
    // başlangıçla aynı güne yazılıyor. Aşağıdaki testler MEVCUT davranışı belgeler; destek eklenirse güncellenmeli.

    [Fact]
    public async Task CreateSlotAsync_CrossingMidnight_IsRejected_CurrentBehavior()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);

        await using var ctx = _db.NewContext();
        var req = new CreateAvailabilitySlotDto
        {
            Date = DateOnly.FromDateTime(Now.AddDays(1)),
            StartTime = new TimeOnly(23, 30),
            EndTime = new TimeOnly(0, 30)
        };

        var result = await NewService(ctx).CreateSlotAsync(TeacherUserId, req);

        // "Bitiş başlangıçtan önce" kuralına takılır (EndTime <= StartTime). BUG #300: düzeltilince Success=true olmalı.
        result.Success.ShouldBeFalse();
        result.Slot.ShouldBeNull();
        await using var verify = _db.NewContext();
        (await verify.TeacherAvailabilitySlots.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task CreateBookingAsync_CrossingMidnightSlotSeededDirectly_IsAccepted_CurrentBehavior()
    {
        // Slot oluşturma ucu reddettiği için böyle bir satır ancak doğrudan veriyle oluşur; rezervasyon yalnızca
        // başlangıcı kontrol ettiğinden kabul edilir (katılım ise hiç açılmaz, bkz. sonraki test).
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);
        var slotId = await SeedSlotAsync(TeacherId, DateOnly.FromDateTime(Now.AddDays(1)),
            new TimeOnly(23, 30), new TimeOnly(0, 30));

        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult<IReadOnlyList<UserLookupResultDto>>(new List<UserLookupResultDto>()));

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateBookingAsync(
            StudentUserId, new CreateBookingDto { AvailabilitySlotId = slotId }, CancellationToken.None);

        result.Success.ShouldBeTrue();
        // Bitiş, başlangıçla aynı güne yazıldığı için başlangıçtan ÖNCE görünür (23:30 → aynı gün 00:30).
        result.Booking!.EndUtc.ShouldBeLessThan(result.Booking.StartUtc); // #300 ile tersine döner (EndUtc > StartUtc)
    }

    [Theory]
    [InlineData(15, 23, 20)] // pencere açılışı (başlangıç - 15 dk = 23:15) sonrası
    [InlineData(15, 23, 45)] // ders sırasında
    [InlineData(16, 0, 10)]  // gece yarısından sonra, ders hâlâ sürüyor
    public async Task GetVideoSessionAsync_CrossingMidnightSlot_JoinNeverOpens_CurrentBehavior(
        int day, int hour, int minute)
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);
        var slotId = await SeedSlotAsync(TeacherId, new DateOnly(2026, 6, 15),
            new TimeOnly(23, 30), new TimeOnly(0, 30));

        int bookingId;
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(StudentUserId);
            var booking = new Booking
            {
                TeacherId = TeacherId,
                StudentId = StudentId,
                AvailabilitySlotId = slotId,
                Status = BookingStatus.Approved,
                CreatedAt = Now,
                DecisionAt = Now
            };
            ctx.Bookings.Add(booking);
            await ctx.SaveChangesAsync();
            bookingId = booking.Id;
        }

        _clock.Now = new DateTimeOffset(2026, 6, day, hour, minute, 0, TimeSpan.Zero);
        await using var ctxVideo = _db.NewContext();
        var result = await NewService(ctxVideo).GetVideoSessionAsync(TeacherUserId, bookingId, CancellationToken.None);

        // Beklenen (destek olsaydı): Success. Gerçek: kapanış = aynı gün 00:30 + 30 dk = 01:00 < şimdi → "pencere kapandı".
        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
        await _videoProvider.DidNotReceiveWithAnyArgs().CreateOrJoinSessionAsync(default!, default);
    }

    public void Dispose() => _db.Dispose();
}
