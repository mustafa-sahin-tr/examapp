using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Bookings;
using ExamApp.Api.Models.Dtos.Video;
using ExamApp.Api.Services.Bookings;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Video;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #418: randevu (müsaitlik + talepler) bağımsız öğretmen özelliğidir.
/// <list type="bullet">
/// <item>Öğretmen tarafı uçlar okula bağlı (<c>IsIndependentTutor=false</c>) öğretmene 403 (Forbidden) döner ve hiçbir şey
/// yazmaz — onaylı olsa bile.</item>
/// <item>Öğrenci tarafı (açık slot listesi, randevu talebi) okula bağlı öğretmen için bilinmeyen öğretmen/slot ile aynı 404 —
/// aynı okuldan veya admin olarak da (#190'daki aynı-okul istisnası kaldırıldı). Bağımsız onaylı tutor herkese açık.</item>
/// <item>Bağımsızlık = <c>IsIndependentTutor &amp;&amp; SchoolId == null</c> (<c>TeacherIndependence</c>): admin'in okula
/// bağladığı bağımsız başvurulu (hibrit) öğretmen bağımsız sayılmaz.</item>
/// </list>
/// </summary>
public class BookingIndependentTutorOnlyTests : IDisposable
{
    private const int SchoolTeacherUserId = 100;
    private const int TutorUserId = 101;
    private const int HybridTeacherUserId = 102;
    private const int StudentUserId = 200;

    /// <summary><c>booking.teacherNotIndependent</c> (varsayılan dil tr).</summary>
    private const string NotIndependentMessage = "Randevu ve müsaitlik yalnız bağımsız öğretmenler içindir.";

    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private static RecurringAvailabilityService NewRecurring(AppDbContext ctx) =>
        new(ctx, TimeProvider.System, NullLogger<RecurringAvailabilityService>.Instance);

    private static BookingService NewService(AppDbContext ctx) =>
        new(ctx, Substitute.For<IAuthApiClient>(), Substitute.For<IVideoSessionProvider>(),
            Options.Create(new VideoOptions()), TimeProvider.System, NewRecurring(ctx), NullLogger<BookingService>.Instance);

    private sealed record Seeded(int SchoolTeacherId, int TutorId, int SchoolSlotId, int TutorSlotId, int SchoolBookingId, int SchoolRuleId);

    private async Task<Seeded> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var school = new School { Name = "Okul A" };
        ctx.Schools.Add(school);
        await ctx.SaveChangesAsync();

        // Onaylı (başvuru + hesap) okula bağlı öğretmen: tek eksik bağımsızlık — reddin sebebi yalnız #418 kuralı.
        var schoolTeacher = new Teacher
        {
            UserId = SchoolTeacherUserId, SchoolId = school.Id, ApprovalStatus = TeacherApprovalStatus.Approved,
            AccountApprovedAt = DateTime.UtcNow.AddDays(-30)
        };
        var tutor = new Teacher
        {
            UserId = TutorUserId, IsIndependentTutor = true, ApprovalStatus = TeacherApprovalStatus.Approved,
            AccountApprovedAt = DateTime.UtcNow.AddDays(-30)
        };
        var student = new Student { UserId = StudentUserId, StudentNumber = "S200", SchoolId = school.Id };
        ctx.AddRange(schoolTeacher, tutor, student);
        await ctx.SaveChangesAsync();

        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var slots = new List<TeacherAvailabilitySlot>();
        foreach (var t in new[] { schoolTeacher, tutor })
        {
            ctx.SetCurrentUser(t.UserId);
            var slot = new TeacherAvailabilitySlot
            {
                TeacherId = t.Id, Date = tomorrow, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(11, 0), CreatedAt = DateTime.UtcNow
            };
            ctx.TeacherAvailabilitySlots.Add(slot);
            slots.Add(slot);
        }
        await ctx.SaveChangesAsync();

        // Eski (kural öncesi) veri: okula bağlı öğretmende bekleyen talep ve aktif tekrarlayan kural.
        ctx.SetCurrentUser(SchoolTeacherUserId);
        var booking = BookingSeed.Add(ctx, schoolTeacher.Id, student.Id, BookingStatus.Pending, 14, tomorrow);
        var rule = new RecurringAvailabilityRule
        {
            TeacherId = schoolTeacher.Id, DayOfWeek = DayOfWeek.Wednesday, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(10, 0),
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow), IsActive = true
        };
        ctx.RecurringAvailabilityRules.Add(rule);
        await ctx.SaveChangesAsync();

        return new Seeded(schoolTeacher.Id, tutor.Id, slots[0].Id, slots[1].Id, booking.Id, rule.Id);
    }

    private static void ShouldBeNotIndependentForbidden(ResponseBaseDto r)
    {
        r.Success.ShouldBeFalse();
        r.Forbidden.ShouldBeTrue();
        r.NotFound.ShouldBeFalse();
        r.Message.ShouldBe(NotIndependentMessage);
    }

    // ---------------- Öğretmen tarafı: 403 ----------------

    [Fact]
    public async Task SchoolTeacher_CreateSlot_Forbidden_AndNothingWritten()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();
        var before = await ctx.TeacherAvailabilitySlots.CountAsync();

        var r = await NewService(ctx).CreateSlotAsync(SchoolTeacherUserId, new CreateAvailabilitySlotDto
        {
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)), StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(11, 0)
        });

        ShouldBeNotIndependentForbidden(r);
        await using var verify = _db.NewContext();
        (await verify.TeacherAvailabilitySlots.CountAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task SchoolTeacher_ListAndDeleteOwnSlots_Forbidden()
    {
        var s = await SeedAsync();
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        ShouldBeNotIndependentForbidden(await service.GetMySlotsAsync(SchoolTeacherUserId, 0, 50));
        ShouldBeNotIndependentForbidden(await service.DeleteSlotAsync(SchoolTeacherUserId, s.SchoolSlotId));

        await using var verify = _db.NewContext();
        (await verify.TeacherAvailabilitySlots.AnyAsync(x => x.Id == s.SchoolSlotId)).ShouldBeTrue();
    }

    [Fact]
    public async Task SchoolTeacher_BookingRequests_ListApproveReject_Forbidden_AndStatusUnchanged()
    {
        var s = await SeedAsync();
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        ShouldBeNotIndependentForbidden(await service.GetTeacherBookingsAsync(SchoolTeacherUserId, 0, 50));
        ShouldBeNotIndependentForbidden(await service.ApproveBookingAsync(SchoolTeacherUserId, s.SchoolBookingId));
        ShouldBeNotIndependentForbidden(await service.RejectBookingAsync(SchoolTeacherUserId, s.SchoolBookingId, "x"));

        await using var verify = _db.NewContext();
        (await verify.Bookings.SingleAsync(b => b.Id == s.SchoolBookingId)).Status.ShouldBe(BookingStatus.Pending);
    }

    [Fact]
    public async Task SchoolTeacher_RecurringRules_CreateListDelete_Forbidden()
    {
        var s = await SeedAsync();
        await using var ctx = _db.NewContext();
        var recurring = NewRecurring(ctx);

        ShouldBeNotIndependentForbidden(await recurring.CreateRuleAsync(SchoolTeacherUserId, new CreateRecurringAvailabilityRuleDto
        {
            DayOfWeek = DayOfWeek.Thursday, StartTime = new TimeOnly(14, 0), EndTime = new TimeOnly(15, 0),
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow)
        }));
        ShouldBeNotIndependentForbidden(await recurring.GetMyRulesAsync(SchoolTeacherUserId, 0, 50));
        ShouldBeNotIndependentForbidden(await recurring.DeleteRuleAsync(SchoolTeacherUserId, s.SchoolRuleId));

        await using var verify = _db.NewContext();
        (await verify.RecurringAvailabilityRules.CountAsync()).ShouldBe(1);
        (await verify.RecurringAvailabilityRules.SingleAsync()).IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task IndependentTutor_TeacherSideEndpoints_StillAllowed()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        var slots = await service.GetMySlotsAsync(TutorUserId, 0, 50);
        slots.Success.ShouldBeTrue();
        slots.Items.ShouldHaveSingleItem();
        (await service.GetTeacherBookingsAsync(TutorUserId, 0, 50)).Success.ShouldBeTrue();
        (await NewRecurring(ctx).GetMyRulesAsync(TutorUserId, 0, 50)).Success.ShouldBeTrue();
    }

    // ---------------- Öğrenci tarafı: 404 (var/yok sızmaz) ----------------

    [Fact]
    public async Task SchoolTeacherOpenSlots_NotFound_LikeUnknownTeacher_EvenForSameSchool()
    {
        var s = await SeedAsync();
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx).GetTeacherOpenSlotsAsync(s.SchoolTeacherId, 0, 50);
        var missing = await NewService(ctx).GetTeacherOpenSlotsAsync(99999, 0, 50);

        r.Success.ShouldBeFalse();
        r.NotFound.ShouldBeTrue();
        r.Items.ShouldBeEmpty();
        r.Message.ShouldBe(missing.Message);
    }

    [Fact]
    public async Task IndependentTutorOpenSlots_Visible()
    {
        var s = await SeedAsync();
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx).GetTeacherOpenSlotsAsync(s.TutorId, 0, 50);

        r.Success.ShouldBeTrue();
        r.Items.ShouldHaveSingleItem().Id.ShouldBe(s.TutorSlotId);
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

        (await NewService(ctx).GetTeacherOpenSlotsAsync(77, 0, 50)).NotFound.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateBooking_OnSchoolTeacherSlot_NotFound_LikeUnknownSlot_AndNothingWritten()
    {
        var s = await SeedAsync();
        await using var ctx = _db.NewContext();
        var before = await ctx.Bookings.CountAsync();

        var r = await NewService(ctx).CreateBookingAsync(StudentUserId, new CreateBookingDto { AvailabilitySlotId = s.SchoolSlotId });
        var missing = await NewService(ctx).CreateBookingAsync(StudentUserId, new CreateBookingDto { AvailabilitySlotId = 99999 });

        r.Success.ShouldBeFalse();
        r.NotFound.ShouldBeTrue();
        r.Message.ShouldBe(missing.Message);
        await using var verify = _db.NewContext();
        (await verify.Bookings.CountAsync()).ShouldBe(before);
    }

    // ---------------- Hibrit (IsIndependentTutor + SchoolId): bağımsız sayılmaz ----------------

    /// <summary>Bağımsız başvurulu, onaylı ama admin'in bir okula bağladığı öğretmen + slotu.</summary>
    private async Task<(int TeacherId, int SlotId)> SeedHybridAsync()
    {
        await using var ctx = _db.NewContext();
        var school = new School { Name = "Okul H" };
        ctx.Schools.Add(school);
        await ctx.SaveChangesAsync();
        var hybrid = new Teacher
        {
            UserId = HybridTeacherUserId, IsIndependentTutor = true, SchoolId = school.Id,
            ApprovalStatus = TeacherApprovalStatus.Approved, AccountApprovedAt = DateTime.UtcNow.AddDays(-30)
        };
        ctx.Teachers.Add(hybrid);
        ctx.Students.Add(new Student { UserId = StudentUserId, StudentNumber = "S200" });
        await ctx.SaveChangesAsync();

        ctx.SetCurrentUser(HybridTeacherUserId);
        var slot = new TeacherAvailabilitySlot
        {
            TeacherId = hybrid.Id, Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(11, 0), CreatedAt = DateTime.UtcNow
        };
        ctx.TeacherAvailabilitySlots.Add(slot);
        await ctx.SaveChangesAsync();
        return (hybrid.Id, slot.Id);
    }

    [Fact]
    public async Task HybridTeacher_TeacherSideEndpoints_Forbidden()
    {
        await SeedHybridAsync();
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        ShouldBeNotIndependentForbidden(await service.CreateSlotAsync(HybridTeacherUserId, new CreateAvailabilitySlotDto
        {
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)), StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(11, 0)
        }));
        ShouldBeNotIndependentForbidden(await service.GetMySlotsAsync(HybridTeacherUserId, 0, 50));
        ShouldBeNotIndependentForbidden(await service.GetTeacherBookingsAsync(HybridTeacherUserId, 0, 50));
        ShouldBeNotIndependentForbidden(await NewRecurring(ctx).CreateRuleAsync(HybridTeacherUserId, new CreateRecurringAvailabilityRuleDto
        {
            DayOfWeek = DayOfWeek.Thursday, StartTime = new TimeOnly(14, 0), EndTime = new TimeOnly(15, 0),
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow)
        }));
        ShouldBeNotIndependentForbidden(await NewRecurring(ctx).GetMyRulesAsync(HybridTeacherUserId, 0, 50));
    }

    [Fact]
    public async Task HybridTeacher_StudentSide_NotFound_LikeUnknown()
    {
        var (teacherId, slotId) = await SeedHybridAsync();
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        var slots = await service.GetTeacherOpenSlotsAsync(teacherId, 0, 50);
        slots.NotFound.ShouldBeTrue();
        slots.Message.ShouldBe((await service.GetTeacherOpenSlotsAsync(99999, 0, 50)).Message);

        var booking = await service.CreateBookingAsync(StudentUserId, new CreateBookingDto { AvailabilitySlotId = slotId });
        booking.NotFound.ShouldBeTrue();
        booking.Message.ShouldBe((await service.CreateBookingAsync(StudentUserId, new CreateBookingDto { AvailabilitySlotId = 99999 })).Message);
    }
}
