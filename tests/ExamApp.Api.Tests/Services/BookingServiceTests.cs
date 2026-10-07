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
            new Microsoft.Extensions.Logging.Abstractions.NullLogger<BookingService>());
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
            // issue #418: randevu/müsaitlik bağımsız öğretmen özelliği.
            IsIndependentTutor = true,
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
    /// Sabit saatten 5 dk sonra başlayan (katılım penceresi içinde) 1 saatlik slot. Gece yarısını geçerse bitiş
    /// ertesi gündür (issue #300, gün aşan slot; bkz. *_CrossingMidnight_* testleri).
    /// </summary>
    private Task<int> SeedSlotStartingSoonAsync()
    {
        var start = Now.AddMinutes(5);
        var slotStart = new TimeOnly(start.Hour, start.Minute);
        return SeedSlotAsync(TeacherId, DateOnly.FromDateTime(start), slotStart, slotStart.AddHours(1));
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
    public async Task CreateSlotAsync_EndBeforeStart_IsNextDayAndRejectedAsTooLong()
    {
        // issue #300: 15:00–14:00 artık "ertesi gün 14:00" demektir (23 saat) → süre üst sınırına takılır.
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
        // code review D4: ertesi gün sayıldığı açıkça söylenir (tooLongNextDay).
        result.Message.ShouldBe("Bitiş başlangıçtan önce olduğu için ertesi gün sayıldı; aralık 4 saati aşıyor.");
    }

    [Fact]
    public async Task CreateSlotAsync_SameDayTooLong_UsesPlainTooLongMessage()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateSlotAsync(TeacherUserId, new CreateAvailabilitySlotDto
        {
            Date = DateOnly.FromDateTime(Now.AddDays(1)),
            StartTime = new TimeOnly(10, 0),
            EndTime = new TimeOnly(14, 1)
        });

        result.Success.ShouldBeFalse();
        result.Message.ShouldBe("Bir müsaitlik aralığı en fazla 4 saat sürebilir.");
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

    // ------ Gece yarısını (UTC) geçen slot (issue #300) ------
    // Kural: EndTime <= StartTime ise bitiş ertesi gün (SlotTimeRange). Sıfır süre reddedilir.

    private static readonly DateOnly MidnightDate = new(2026, 6, 16); // DefaultNow'dan (06-15 12:00) bir gün sonra

    [Fact]
    public async Task CreateSlotAsync_CrossingMidnight_SucceedsWithEndOnNextDay()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateSlotAsync(TeacherUserId, new CreateAvailabilitySlotDto
        {
            Date = MidnightDate,
            StartTime = new TimeOnly(23, 30),
            EndTime = new TimeOnly(0, 30)
        });

        result.Success.ShouldBeTrue(result.Message);
        result.Slot!.StartUtc.ShouldBe(new DateTime(2026, 6, 16, 23, 30, 0, DateTimeKind.Utc));
        result.Slot.EndUtc.ShouldBe(new DateTime(2026, 6, 17, 0, 30, 0, DateTimeKind.Utc));
        result.Slot.EndUtc.ShouldBeGreaterThan(result.Slot.StartUtc);

        // Saklama modeli değişmez: tek Date + ham saatler (ayrı EndDate kolonu yok).
        await using var verify = _db.NewContext();
        var stored = await verify.TeacherAvailabilitySlots.SingleAsync();
        stored.Date.ShouldBe(MidnightDate);
        stored.StartTime.ShouldBe(new TimeOnly(23, 30));
        stored.EndTime.ShouldBe(new TimeOnly(0, 30));
    }

    [Theory]
    [InlineData(14, 0)]
    [InlineData(0, 0)]
    [InlineData(23, 30)]
    public async Task CreateSlotAsync_ZeroDuration_IsRejected(int hour, int minute)
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);

        await using var ctx = _db.NewContext();
        var time = new TimeOnly(hour, minute);
        var result = await NewService(ctx).CreateSlotAsync(TeacherUserId, new CreateAvailabilitySlotDto
        {
            Date = MidnightDate,
            StartTime = time,
            EndTime = time // 24 saatlik slot sayılmaz (PO kararı)
        });

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeFalse();
        result.Message.ShouldBe("Bitiş saati başlangıç saatiyle aynı olamaz."); // booking.slot.zeroLength (code review D5)
        await using var verify = _db.NewContext();
        (await verify.TeacherAvailabilitySlots.CountAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData(22, 0, 2, 0, true)]   // tam 4 saat, gece yarısını geçiyor
    [InlineData(22, 0, 2, 1, false)]  // 4 saat 1 dk
    [InlineData(23, 59, 0, 0, true)]  // 1 dk
    [InlineData(20, 0, 19, 59, false)] // 23 saat 59 dk (ertesi gün)
    public async Task CreateSlotAsync_CrossingMidnight_DurationLimitUsesNextDayEnd(
        int startHour, int startMinute, int endHour, int endMinute, bool expectedSuccess)
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateSlotAsync(TeacherUserId, new CreateAvailabilitySlotDto
        {
            Date = MidnightDate,
            StartTime = new TimeOnly(startHour, startMinute),
            EndTime = new TimeOnly(endHour, endMinute)
        });

        result.Success.ShouldBe(expectedSuccess, result.Message);
        if (!expectedSuccess)
            result.Message.ShouldContain(BookingService.MaxSlotDurationHours.ToString());
    }

    [Fact]
    public async Task CreateSlotAsync_CrossingMidnightStartingInPast_IsRejected()
    {
        // Başlangıç geçmişte (dün 23:30); bitiş (bugün 00:30) gelecekte olsa da geçmiş kuralı başlangıca bakar.
        SetClock(0, 10);
        await SeedTeacherAsync(TeacherId, TeacherUserId);

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateSlotAsync(TeacherUserId, new CreateAvailabilitySlotDto
        {
            Date = new DateOnly(2026, 6, 14),
            StartTime = new TimeOnly(23, 30),
            EndTime = new TimeOnly(0, 30)
        });

        result.Success.ShouldBeFalse();
        result.NotFound.ShouldBeFalse();
    }

    /// <summary>
    /// Çakışma UTC [Start, End) aralıklarıyla: önceki günün gün aşan slotu ertesi güne, yeni gün aşan slot ertesi günün
    /// slotuna taşar. Bitişik aralıklar çakışmaz. Gün ofsetleri <see cref="MidnightDate"/>'e göre.
    /// </summary>
    [Theory]
    // issue örneği: D 23:30–00:30 mevcut, D+1 00:00–00:45 yeni → önceki gün adayı yakalanmalı.
    [InlineData(0, 23, 30, 0, 30, 1, 0, 0, 0, 45, true)]
    // tersi: D+1 00:00–00:45 mevcut, D 23:30–00:30 yeni → ertesi gün adayı.
    [InlineData(1, 0, 0, 0, 45, 0, 23, 30, 0, 30, true)]
    // iki gün aşan slot aynı gün, kesişiyor.
    [InlineData(0, 23, 0, 0, 15, 0, 23, 30, 1, 0, true)]
    // bitişik: D 23:30–00:30 mevcut, D+1 00:30–01:00 yeni → çakışmaz.
    [InlineData(0, 23, 30, 0, 30, 1, 0, 30, 1, 0, false)]
    // bitişik: D 22:00–23:30 mevcut, D 23:30–00:30 yeni → çakışmaz.
    [InlineData(0, 22, 0, 23, 30, 0, 23, 30, 0, 30, false)]
    // D+1 01:00–02:00 mevcut, D 23:30–00:30 yeni → çakışmaz.
    [InlineData(1, 1, 0, 2, 0, 0, 23, 30, 0, 30, false)]
    // D-1'in gün aşan slotu D'nin sabahına taşmaz (D 00:30'a kadar), D 01:00–02:00 yeni → çakışmaz.
    [InlineData(-1, 23, 30, 0, 30, 0, 1, 0, 2, 0, false)]
    public async Task CreateSlotAsync_CrossingMidnightOverlap_IsDetectedAcrossDays(
        int existingDayOffset, int esH, int esM, int eeH, int eeM,
        int newDayOffset, int nsH, int nsM, int neH, int neM, bool expectedConflict)
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedSlotAsync(TeacherId, MidnightDate.AddDays(existingDayOffset), new TimeOnly(esH, esM), new TimeOnly(eeH, eeM));

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateSlotAsync(TeacherUserId, new CreateAvailabilitySlotDto
        {
            Date = MidnightDate.AddDays(newDayOffset),
            StartTime = new TimeOnly(nsH, nsM),
            EndTime = new TimeOnly(neH, neM)
        });

        result.Success.ShouldBe(!expectedConflict, result.Message);
        result.Conflict.ShouldBe(expectedConflict);
    }

    [Fact]
    public async Task CreateSlotAsync_OtherTeachersCrossingMidnightSlot_DoesNotConflict()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedTeacherAsync(OtherTeacherId, OtherTeacherUserId);
        await SeedSlotAsync(OtherTeacherId, MidnightDate, new TimeOnly(23, 30), new TimeOnly(0, 30));

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateSlotAsync(TeacherUserId, new CreateAvailabilitySlotDto
        {
            Date = MidnightDate.AddDays(1),
            StartTime = new TimeOnly(0, 0),
            EndTime = new TimeOnly(0, 45)
        });

        result.Success.ShouldBeTrue(result.Message);
    }

    [Fact]
    public async Task CreateBookingAsync_CrossingMidnightSlot_ReturnsEndUtcOnNextDay()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);
        var slotId = await SeedSlotAsync(TeacherId, MidnightDate, new TimeOnly(23, 30), new TimeOnly(0, 30));

        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult<IReadOnlyList<UserLookupResultDto>>(new List<UserLookupResultDto>()));

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateBookingAsync(
            StudentUserId, new CreateBookingDto { AvailabilitySlotId = slotId }, CancellationToken.None);

        result.Success.ShouldBeTrue();
        result.Booking!.StartUtc.ShouldBe(new DateTime(2026, 6, 16, 23, 30, 0, DateTimeKind.Utc));
        result.Booking.EndUtc.ShouldBe(new DateTime(2026, 6, 17, 0, 30, 0, DateTimeKind.Utc));
        result.Booking.EndUtc.ShouldBeGreaterThan(result.Booking.StartUtc);
    }

    [Fact]
    public async Task Listings_CrossingMidnightSlot_ReturnEndUtcAfterStartUtc()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);
        var bookedSlotId = await SeedSlotAsync(TeacherId, MidnightDate, new TimeOnly(23, 30), new TimeOnly(0, 30));
        await SeedSlotAsync(TeacherId, MidnightDate.AddDays(2), new TimeOnly(23, 0), new TimeOnly(1, 0)); // açık slot

        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(StudentUserId);
            ctx.Bookings.Add(new Booking
            {
                TeacherId = TeacherId, StudentId = StudentId, AvailabilitySlotId = bookedSlotId,
                Status = BookingStatus.Approved, CreatedAt = Now, DecisionAt = Now
            });
            await ctx.SaveChangesAsync();
        }

        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult<IReadOnlyList<UserLookupResultDto>>(new List<UserLookupResultDto>()));

        await using var read = _db.NewContext();
        var service = NewService(read);

        var mine = await service.GetMySlotsAsync(TeacherUserId, 0, 50);
        mine.Items.Count.ShouldBe(2);
        mine.Items.ShouldAllBe(s => s.EndUtc > s.StartUtc);
        mine.Items.Single(s => s.Id == bookedSlotId).EndUtc.ShouldBe(new DateTime(2026, 6, 17, 0, 30, 0, DateTimeKind.Utc));

        var open = await service.GetTeacherOpenSlotsAsync(TeacherId, 0, 50);
        open.Items.Count.ShouldBe(1);
        open.Items[0].EndUtc.ShouldBe(new DateTime(2026, 6, 19, 1, 0, 0, DateTimeKind.Utc));

        var teacherBookings = await service.GetTeacherBookingsAsync(TeacherUserId, 0, 50);
        var studentBookings = await service.GetStudentBookingsAsync(StudentUserId, 0, 50);
        foreach (var b in teacherBookings.Items.Concat(studentBookings.Items))
        {
            b.EndUtc.ShouldBe(new DateTime(2026, 6, 17, 0, 30, 0, DateTimeKind.Utc));
            b.EndUtc.ShouldBeGreaterThan(b.StartUtc);
        }
    }

    private async Task<int> SeedApprovedCrossingMidnightBookingAsync()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);
        // 2026-06-15 23:30 → 2026-06-16 00:30; pencere 23:15 → 06-16 01:00.
        var slotId = await SeedSlotAsync(TeacherId, new DateOnly(2026, 6, 15), new TimeOnly(23, 30), new TimeOnly(0, 30));

        await using var ctx = _db.NewContext();
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
        return booking.Id;
    }

    [Theory]
    [InlineData(15, 23, 14, false)] // açılıştan 1 dk önce
    [InlineData(15, 23, 15, true)]  // açılış anı (başlangıç - 15 dk)
    [InlineData(15, 23, 45, true)]  // ders sırasında
    [InlineData(16, 0, 10, true)]   // gece yarısından sonra, ders sürüyor
    [InlineData(16, 0, 59, true)]   // ertesi gün, bitiş + 30 dk içinde
    [InlineData(16, 1, 0, true)]    // kapanış anı dahil (ertesi gün 00:30 + 30 dk)
    [InlineData(16, 1, 1, false)]   // kapandı
    public async Task GetVideoSessionAsync_CrossingMidnightSlot_WindowSpansIntoNextDay(
        int day, int hour, int minute, bool expectedOpen)
    {
        var bookingId = await SeedApprovedCrossingMidnightBookingAsync();

        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult<IReadOnlyList<UserLookupResultDto>>(new List<UserLookupResultDto>()));
        VideoSessionRequest? captured = null;
        _videoProvider.CreateOrJoinSessionAsync(Arg.Any<VideoSessionRequest>(), Arg.Any<CancellationToken>())
            .Returns(x =>
            {
                captured = (VideoSessionRequest)x[0];
                return Task.FromResult(new VideoSessionDto { Provider = "Jitsi", RoomName = "r", Token = "x" });
            });

        _clock.Now = new DateTimeOffset(2026, 6, day, hour, minute, 0, TimeSpan.Zero);
        await using var ctxVideo = _db.NewContext();
        var result = await NewService(ctxVideo).GetVideoSessionAsync(TeacherUserId, bookingId, CancellationToken.None);

        result.Success.ShouldBe(expectedOpen, result.Message);
        if (expectedOpen)
        {
            captured.ShouldNotBeNull();
            captured!.StartUtc.ShouldBe(new DateTime(2026, 6, 15, 23, 30, 0, DateTimeKind.Utc));
            captured.EndUtc.ShouldBe(new DateTime(2026, 6, 16, 0, 30, 0, DateTimeKind.Utc));
            captured.WindowClosesAtUtc.ShouldBe(new DateTime(2026, 6, 16, 1, 0, 0, DateTimeKind.Utc));
        }
        else
        {
            result.Conflict.ShouldBeTrue();
            await _videoProvider.DidNotReceiveWithAnyArgs().CreateOrJoinSessionAsync(default!, default);
        }
    }

    [Fact]
    public async Task GetLiveSessionAccessAsync_CrossingMidnightSlot_WindowUsedByWhiteboardClosesNextDay()
    {
        var bookingId = await SeedApprovedCrossingMidnightBookingAsync();
        _clock.Now = new DateTimeOffset(2026, 6, 16, 0, 59, 0, TimeSpan.Zero);

        await using var ctx = _db.NewContext();
        var access = await NewService(ctx).GetLiveSessionAccessAsync(StudentUserId, bookingId);

        access.Allowed.ShouldBeTrue();
        access.Window.OpensAtUtc.ShouldBe(new DateTime(2026, 6, 15, 23, 15, 0, DateTimeKind.Utc));
        access.Window.ClosesAtUtc.ShouldBe(new DateTime(2026, 6, 16, 1, 0, 0, DateTimeKind.Utc));
    }

    // ------ Savunma derinliği: hatalı/uzun/sıfır süreli satır (security review O1, L3) ------

    private async Task<int> SeedApprovedBookingAsync(DateOnly date, TimeOnly start, TimeOnly end)
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);
        var slotId = await SeedSlotAsync(TeacherId, date, start, end);

        await using var ctx = _db.NewContext();
        ctx.SetCurrentUser(StudentUserId);
        var booking = new Booking
        {
            TeacherId = TeacherId, StudentId = StudentId, AvailabilitySlotId = slotId,
            Status = BookingStatus.Approved, CreatedAt = Now, DecisionAt = Now
        };
        ctx.Bookings.Add(booking);
        await ctx.SaveChangesAsync();
        return booking.Id;
    }

    [Theory]
    // DB'de doğrudan yazılmış 10 saatlik satır (10:00–20:00): pencere başlangıç + 4 saat + 30 dk = 14:30'da kapanır.
    [InlineData(10, 0, 20, 0, 15, 14, 30, 15, 14, 31)]
    // Gün aşan 23 saatlik satır (20:00–19:00 ertesi gün): 00:00 + 30 dk = ertesi gün 00:30.
    [InlineData(20, 0, 19, 0, 16, 0, 30, 16, 0, 31)]
    public async Task GetLiveSessionAccessAsync_OverlongRow_WindowIsCappedAtMaxSlotDuration(
        int sH, int sM, int eH, int eM, int closeDay, int closeHour, int closeMinute, int afterDay, int afterHour, int afterMinute)
    {
        var bookingId = await SeedApprovedBookingAsync(new DateOnly(2026, 6, 15), new TimeOnly(sH, sM), new TimeOnly(eH, eM));
        var closesAt = new DateTime(2026, 6, closeDay, closeHour, closeMinute, 0, DateTimeKind.Utc);

        _clock.Now = new DateTimeOffset(closesAt, TimeSpan.Zero);
        await using (var ctx = _db.NewContext())
        {
            var atClose = await NewService(ctx).GetLiveSessionAccessAsync(StudentUserId, bookingId);
            atClose.Allowed.ShouldBeTrue();
            atClose.Window.ClosesAtUtc.ShouldBe(closesAt);
            (atClose.Window.EndUtc - atClose.Window.StartUtc).ShouldBe(BookingService.MaxSlotDuration);
        }

        _clock.Now = new DateTimeOffset(2026, 6, afterDay, afterHour, afterMinute, 0, TimeSpan.Zero);
        await using (var ctx = _db.NewContext())
            (await NewService(ctx).GetLiveSessionAccessAsync(StudentUserId, bookingId)).Denial
                .ShouldBe(BookingLiveSessionDenial.WindowClosed);
    }

    [Theory]
    [InlineData(14, 0)]  // tam "başlangıç" anı
    [InlineData(13, 50)] // "açılış" sonrası
    [InlineData(23, 59)] // 24 saat sayılsaydı açık olurdu
    public async Task GetLiveSessionAccessAsync_ZeroLengthRow_WindowNeverOpens(int hour, int minute)
    {
        var bookingId = await SeedApprovedBookingAsync(new DateOnly(2026, 6, 15), new TimeOnly(14, 0), new TimeOnly(14, 0));
        _clock.Now = new DateTimeOffset(2026, 6, 15, hour, minute, 0, TimeSpan.Zero);

        await using var ctx = _db.NewContext();
        var access = await NewService(ctx).GetLiveSessionAccessAsync(StudentUserId, bookingId);

        access.Denial.ShouldBe(BookingLiveSessionDenial.WindowClosed);
        access.Window.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void SlotTimeRange_ZeroLength_IsNotTwentyFourHoursAndOverlapsNothing()
    {
        var date = new DateOnly(2026, 6, 15);
        var zero = SlotTimeRange.From(date, new TimeOnly(14, 0), new TimeOnly(14, 0));

        zero.Duration.ShouldBe(TimeSpan.Zero);
        zero.IsValid.ShouldBeFalse();
        SlotTimeRange.DurationOf(new TimeOnly(14, 0), new TimeOnly(14, 0)).ShouldBe(TimeSpan.Zero);
        SlotTimeRange.CrossesMidnight(new TimeOnly(14, 0), new TimeOnly(14, 0)).ShouldBeFalse();
        zero.Overlaps(SlotTimeRange.From(date, new TimeOnly(13, 0), new TimeOnly(15, 0))).ShouldBeFalse();
        SlotTimeRange.From(date, new TimeOnly(13, 0), new TimeOnly(15, 0)).Overlaps(zero).ShouldBeFalse();
    }

    [Theory]
    [InlineData(23, 30, 0, 30, 23, 30, 24, 30)]  // gün aşan: bitiş ertesi gün
    [InlineData(14, 0, 15, 0, 14, 0, 15, 0)]     // aynı gün
    [InlineData(23, 0, 0, 0, 23, 0, 24, 0)]      // tam gece yarısında biten
    public void SlotTimeRange_From_PutsEndOnNextDayWhenEndIsNotAfterStart(
        int sH, int sM, int eH, int eM, int expectedStartHour, int expectedStartMinute, int expectedEndHour, int expectedEndMinute)
    {
        var date = new DateOnly(2026, 6, 15);
        var range = SlotTimeRange.From(date, new TimeOnly(sH, sM), new TimeOnly(eH, eM));

        var midnight = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        range.StartUtc.ShouldBe(midnight.AddHours(expectedStartHour).AddMinutes(expectedStartMinute));
        range.EndUtc.ShouldBe(midnight.AddHours(expectedEndHour).AddMinutes(expectedEndMinute));
        range.StartUtc.Kind.ShouldBe(DateTimeKind.Utc);
        range.EndUtc.Kind.ShouldBe(DateTimeKind.Utc);
        range.Duration.ShouldBe(SlotTimeRange.DurationOf(new TimeOnly(sH, sM), new TimeOnly(eH, eM)));
        range.EndUtc.ShouldBeGreaterThan(range.StartUtc);
    }

    // ------ issue #376: slotu soft-delete edilmiş randevu ------

    /// <summary>Slotu doğrudan soft-delete eder (eski veri / bilinmeyen yol simülasyonu; servis yolu artık izin vermez).</summary>
    private async Task SoftDeleteSlotAsync(int slotId)
    {
        await using var ctx = _db.NewContext();
        (await ctx.TeacherAvailabilitySlots.IgnoreQueryFilters()
            .Where(s => s.Id == slotId)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.IsDeleted, true))).ShouldBe(1);
    }

    private async Task SoftDeleteBookingAsync(int bookingId)
    {
        await using var ctx = _db.NewContext();
        (await ctx.Bookings.IgnoreQueryFilters()
            .Where(b => b.Id == bookingId)
            .ExecuteUpdateAsync(set => set.SetProperty(b => b.IsDeleted, true))).ShouldBe(1);
    }

    private async Task<int> SlotIdOfAsync(int bookingId)
    {
        await using var ctx = _db.NewContext();
        return await ctx.Bookings.IgnoreQueryFilters().Where(b => b.Id == bookingId).Select(b => b.AvailabilitySlotId).SingleAsync();
    }

    private async Task<int> SeedBookingOnSlotAsync(int slotId, BookingStatus status)
    {
        await using var ctx = _db.NewContext();
        ctx.SetCurrentUser(StudentUserId);
        var booking = new Booking
        {
            TeacherId = TeacherId, StudentId = StudentId, AvailabilitySlotId = slotId,
            Status = status, CreatedAt = Now, DecisionAt = status == BookingStatus.Pending ? null : Now
        };
        ctx.Bookings.Add(booking);
        await ctx.SaveChangesAsync();
        return booking.Id;
    }

    [Fact]
    public async Task GetLiveSessionAccessAsync_ApprovedBookingWithSoftDeletedSlot_IsAllowedForBothParties()
    {
        var start = Now.AddMinutes(5);
        var bookingId = await SeedApprovedBookingAsync(DateOnly.FromDateTime(start), new TimeOnly(start.Hour, start.Minute),
            new TimeOnly(start.Hour, start.Minute).AddHours(1));
        await SoftDeleteSlotAsync(await SlotIdOfAsync(bookingId));

        await using var ctx = _db.NewContext();
        var service = NewService(ctx);
        var teacher = await service.GetLiveSessionAccessAsync(TeacherUserId, bookingId);
        var student = await service.GetLiveSessionAccessAsync(StudentUserId, bookingId);

        teacher.Denial.ShouldBe(BookingLiveSessionDenial.None);
        teacher.IsTeacher.ShouldBeTrue();
        student.Denial.ShouldBe(BookingLiveSessionDenial.None);
        // Pencere silinmiş slotun saatinden hesaplanır.
        teacher.Window.StartUtc.ShouldBe(new DateTime(start.Year, start.Month, start.Day, start.Hour, start.Minute, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task GetVideoSessionAsync_ApprovedBookingWithSoftDeletedSlot_DoesNotReturnBookingNotFound()
    {
        var start = Now.AddMinutes(5);
        var bookingId = await SeedApprovedBookingAsync(DateOnly.FromDateTime(start), new TimeOnly(start.Hour, start.Minute),
            new TimeOnly(start.Hour, start.Minute).AddHours(1));
        await SoftDeleteSlotAsync(await SlotIdOfAsync(bookingId));
        _videoProvider.CreateOrJoinSessionAsync(Arg.Any<VideoSessionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new VideoSessionDto { RoomName = "room" });

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).GetVideoSessionAsync(TeacherUserId, bookingId, CancellationToken.None);

        result.NotFound.ShouldBeFalse();
        result.Success.ShouldBeTrue(result.Message);
    }

    [Fact]
    public async Task GetLiveSessionAccessAsync_SoftDeletedBooking_IsStillNotFound_EvenWithSoftDeletedSlot()
    {
        // Yalnız slot filtresi gevşer; booking'in kendi soft-delete filtresi geçerli kalır.
        var start = Now.AddMinutes(5);
        var bookingId = await SeedApprovedBookingAsync(DateOnly.FromDateTime(start), new TimeOnly(start.Hour, start.Minute),
            new TimeOnly(start.Hour, start.Minute).AddHours(1));
        await SoftDeleteSlotAsync(await SlotIdOfAsync(bookingId));
        await SoftDeleteBookingAsync(bookingId);

        await using var ctx = _db.NewContext();
        (await NewService(ctx).GetLiveSessionAccessAsync(TeacherUserId, bookingId)).Denial.ShouldBe(BookingLiveSessionDenial.NotFound);
    }

    [Fact]
    public async Task GetLiveSessionAccessAsync_SoftDeletedStudent_IsStillNotFound_EvenWithSoftDeletedSlot()
    {
        var start = Now.AddMinutes(5);
        var bookingId = await SeedApprovedBookingAsync(DateOnly.FromDateTime(start), new TimeOnly(start.Hour, start.Minute),
            new TimeOnly(start.Hour, start.Minute).AddHours(1));
        await SoftDeleteSlotAsync(await SlotIdOfAsync(bookingId));
        await using (var ctx = _db.NewContext())
            await ctx.Students.IgnoreQueryFilters().Where(s => s.Id == StudentId)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.IsDeleted, true));

        await using var ctxRead = _db.NewContext();
        (await NewService(ctxRead).GetLiveSessionAccessAsync(TeacherUserId, bookingId)).Denial.ShouldBe(BookingLiveSessionDenial.NotFound);
    }

    [Fact]
    public async Task BookingLists_IncludeBookingWithSoftDeletedSlot_WithTheSlotTimes()
    {
        var date = DateOnly.FromDateTime(Now.AddDays(3));
        var bookingId = await SeedApprovedBookingAsync(date, new TimeOnly(9, 0), new TimeOnly(10, 0));
        var liveSlotId = await SeedSlotAsync(TeacherId, date, new TimeOnly(11, 0), new TimeOnly(12, 0));
        var pendingId = await SeedBookingOnSlotAsync(liveSlotId, BookingStatus.Pending);
        await SoftDeleteSlotAsync(await SlotIdOfAsync(bookingId));

        await using var ctx = _db.NewContext();
        var service = NewService(ctx);
        var teacherList = await service.GetTeacherBookingsAsync(TeacherUserId, 0, 50);
        var studentList = await service.GetStudentBookingsAsync(StudentUserId, 0, 50);

        teacherList.Items.Select(b => b.Id).ShouldBe(new[] { pendingId, bookingId }); // tarih/saat azalan
        studentList.Items.Select(b => b.Id).ShouldBe(new[] { pendingId, bookingId });
        var deletedSlotBooking = teacherList.Items.Single(b => b.Id == bookingId);
        deletedSlotBooking.Status.ShouldBe("Approved");
        deletedSlotBooking.Date.ShouldBe(date);
        deletedSlotBooking.StartTime.ShouldBe(new TimeOnly(9, 0));
        deletedSlotBooking.EndTime.ShouldBe(new TimeOnly(10, 0));
    }

    [Fact]
    public async Task BookingLists_StillExcludeSoftDeletedBookings()
    {
        var date = DateOnly.FromDateTime(Now.AddDays(3));
        var bookingId = await SeedApprovedBookingAsync(date, new TimeOnly(9, 0), new TimeOnly(10, 0));
        await SoftDeleteSlotAsync(await SlotIdOfAsync(bookingId));
        await SoftDeleteBookingAsync(bookingId);

        await using var ctx = _db.NewContext();
        (await NewService(ctx).GetTeacherBookingsAsync(TeacherUserId, 0, 50)).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task SlotLists_StillHideSoftDeletedSlots()
    {
        // Gevşeme yalnız randevu sorgularında: öğretmenin takvimi ve öğrencinin açık slotları silinmiş slotu göstermez.
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        var date = DateOnly.FromDateTime(Now.AddDays(3));
        var deletedId = await SeedSlotAsync(TeacherId, date, new TimeOnly(9, 0), new TimeOnly(10, 0));
        var liveId = await SeedSlotAsync(TeacherId, date, new TimeOnly(11, 0), new TimeOnly(12, 0));
        await SoftDeleteSlotAsync(deletedId);

        await using var ctx = _db.NewContext();
        var service = NewService(ctx);
        (await service.GetMySlotsAsync(TeacherUserId, 0, 50)).Items.Select(s => s.Id).ShouldBe(new[] { liveId });
        (await service.GetTeacherOpenSlotsAsync(TeacherId, 0, 50))
            .Items.Select(s => s.Id).ShouldBe(new[] { liveId });
    }

    [Fact]
    public async Task RejectBookingAsync_PendingBookingWithSoftDeletedSlot_CanStillBeDecided()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);
        var slotId = await SeedSlotAsync(TeacherId, DateOnly.FromDateTime(Now.AddDays(3)), new TimeOnly(9, 0), new TimeOnly(10, 0));
        var bookingId = await SeedBookingOnSlotAsync(slotId, BookingStatus.Pending);
        await SoftDeleteSlotAsync(slotId);

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).RejectBookingAsync(TeacherUserId, bookingId, null);

        result.Success.ShouldBeTrue(result.Message);
        result.Booking.ShouldNotBeNull();
        result.Booking!.Status.ShouldBe("Rejected");
    }

    [Fact]
    public async Task ApproveBookingAsync_PendingBookingWithSoftDeletedSlot_Returns409_AndStaysPending()
    {
        // Karar (#376 review): slotu silinmiş Pending talep yalnız reddedilebilir; onay silinmiş slota ders bağlamaz.
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);
        var slotId = await SeedSlotAsync(TeacherId, DateOnly.FromDateTime(Now.AddDays(3)), new TimeOnly(9, 0), new TimeOnly(10, 0));
        var bookingId = await SeedBookingOnSlotAsync(slotId, BookingStatus.Pending);
        await SoftDeleteSlotAsync(slotId);

        await using (var ctx = _db.NewContext())
        {
            var result = await NewService(ctx).ApproveBookingAsync(TeacherUserId, bookingId);

            result.Success.ShouldBeFalse();
            result.Conflict.ShouldBeTrue();
            result.NotFound.ShouldBeFalse();
            result.ShouldBeOfType<BookingDecisionResultDto>().ErrorCode.ShouldBe(BookingErrorCodes.RequestSlotDeleted);
            result.Message.ShouldNotBeNullOrWhiteSpace();
        }

        await using var check = _db.NewContext();
        (await check.Bookings.SingleAsync(b => b.Id == bookingId)).Status.ShouldBe(BookingStatus.Pending);
        (await check.OutboxMessages.CountAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData(BookingStatus.Pending)]
    [InlineData(BookingStatus.Approved)]
    public async Task DeleteSlotAsync_SlotWithActiveBooking_Returns409WithErrorCode_AndKeepsTheSlot(BookingStatus status)
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);
        var slotId = await SeedSlotAsync(TeacherId, DateOnly.FromDateTime(Now.AddDays(5)), new TimeOnly(14, 0), new TimeOnly(15, 0));
        await SeedBookingOnSlotAsync(slotId, status);

        await using (var ctx = _db.NewContext())
        {
            var result = await NewService(ctx).DeleteSlotAsync(TeacherUserId, slotId);
            result.Success.ShouldBeFalse();
            result.Conflict.ShouldBeTrue();
            result.ErrorCode.ShouldBe(BookingErrorCodes.SlotHasActiveBooking);
            result.Message.ShouldNotBeNullOrWhiteSpace();
        }

        await using var check = _db.NewContext();
        (await check.TeacherAvailabilitySlots.IgnoreQueryFilters().SingleAsync(s => s.Id == slotId)).IsDeleted.ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteSlotAsync_SlotWithOnlyRejectedBooking_IsDeleted()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);
        var slotId = await SeedSlotAsync(TeacherId, DateOnly.FromDateTime(Now.AddDays(5)), new TimeOnly(14, 0), new TimeOnly(15, 0));
        await SeedBookingOnSlotAsync(slotId, BookingStatus.Rejected);

        await using (var ctx = _db.NewContext())
        {
            var result = await NewService(ctx).DeleteSlotAsync(TeacherUserId, slotId);
            result.Success.ShouldBeTrue(result.Message);
            result.ErrorCode.ShouldBeNull();
        }

        await using var check = _db.NewContext();
        (await check.TeacherAvailabilitySlots.IgnoreQueryFilters().SingleAsync(s => s.Id == slotId)).IsDeleted.ShouldBeTrue();
    }

    [Fact]
    public async Task DeleteSlotAsync_AlreadyDeletedSlot_ReturnsNotFound()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        var slotId = await SeedSlotAsync(TeacherId, DateOnly.FromDateTime(Now.AddDays(5)), new TimeOnly(14, 0), new TimeOnly(15, 0));
        await SoftDeleteSlotAsync(slotId);

        await using var ctx = _db.NewContext();
        (await NewService(ctx).DeleteSlotAsync(TeacherUserId, slotId)).NotFound.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateBookingAsync_SoftDeletedSlot_ReturnsNotFound_AndWritesNothing()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync(StudentId, StudentUserId);
        var slotId = await SeedSlotAsync(TeacherId, DateOnly.FromDateTime(Now.AddDays(5)), new TimeOnly(14, 0), new TimeOnly(15, 0));
        await SoftDeleteSlotAsync(slotId);

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).CreateBookingAsync(StudentUserId, new CreateBookingDto { AvailabilitySlotId = slotId })).NotFound.ShouldBeTrue();

        await using var check = _db.NewContext();
        (await check.Bookings.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    public void Dispose() => _db.Dispose();
}
