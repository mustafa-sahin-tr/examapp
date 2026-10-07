using System.Net;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos.Bookings;
using ExamApp.Api.Services.Bookings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// issue #376 — slotu soft-delete edilmiş randevu gerçek PostgreSQL'de:
/// (a) slotun ADLI soft-delete filtresi randevu okumalarında kapatılır (Npgsql çevirisi), diğer filtreler geçerli kalır;
/// (b) aktif randevulu slot hiçbir yoldan silinemez — tekil silme ve seri silme, randevu talebiyle aynı öğretmen müsaitlik
/// kilidi (#323) altında serileşir; paralel "talep + silme" hiçbir sırada silinmiş slota bağlı aktif randevu bırakmaz.
/// Birim testler SQLite'ta (kilit no-op) koştuğu için yarış yalnız burada gerçekten üretilir.
/// </summary>
public class BookingDeletedSlotPostgresTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const int Rounds = 12;

    // FakeUserDirectory Respawn ile sıfırlanmaz → benzersiz, büyük UserId.
    private static int NewUserId() => 1_000_000 + Random.Shared.Next(0, 900_000_000);

    private static readonly DateOnly Future = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);

    private sealed record Seed(int TeacherId, int TeacherUserId, int StudentId, int StudentUserId, int SlotId, int? RuleId);

    private async Task<Seed> SeedAsync(bool recurring = false)
    {
        var teacherUserId = NewUserId();
        var studentUserId = NewUserId();
        var directory = Factory.Services.GetRequiredService<FakeUserDirectory>();
        directory.Add(new() { Id = teacherUserId, KeycloakId = $"kc-t-{teacherUserId}", FullName = "Öğretmen" });
        directory.Add(new() { Id = studentUserId, KeycloakId = $"kc-s-{studentUserId}", FullName = "Öğrenci" });

        return await WithDbAsync(async db =>
        {
            var teacher = new Teacher
            {
                UserId = teacherUserId, ApprovalStatus = TeacherApprovalStatus.Approved, AccountApprovedAt = DateTime.UtcNow.AddDays(-3),
                IsIndependentTutor = true, // issue #418: randevu bağımsız öğretmen özelliği
                Bio = "t"
            };
            var student = new Student { UserId = studentUserId, StudentNumber = $"S{studentUserId}" };
            db.Teachers.Add(teacher);
            db.Students.Add(student);
            await db.SaveChangesAsync();

            RecurringAvailabilityRule? rule = null;
            if (recurring)
            {
                rule = new RecurringAvailabilityRule
                {
                    TeacherId = teacher.Id, DayOfWeek = Future.DayOfWeek, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(11, 0),
                    EffectiveFrom = Future, IsActive = true
                };
                db.RecurringAvailabilityRules.Add(rule);
                await db.SaveChangesAsync();
            }

            var slot = new TeacherAvailabilitySlot
            {
                TeacherId = teacher.Id, Date = Future, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(11, 0),
                CreatedAt = DateTime.UtcNow, RecurringAvailabilityRuleId = rule?.Id
            };
            db.TeacherAvailabilitySlots.Add(slot);
            await db.SaveChangesAsync();
            return new Seed(teacher.Id, teacherUserId, student.Id, studentUserId, slot.Id, rule?.Id);
        });
    }

    private Task<int> SeedBookingAsync(Seed seed, BookingStatus status) => WithDbAsync(async db =>
    {
        var booking = new Booking
        {
            TeacherId = seed.TeacherId, StudentId = seed.StudentId, AvailabilitySlotId = seed.SlotId, Status = status,
            CreatedAt = DateTime.UtcNow, DecisionAt = status == BookingStatus.Pending ? null : DateTime.UtcNow
        };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return booking.Id;
    });

    private Task SoftDeleteSlotAsync(int slotId) => WithDbAsync(db => db.TeacherAvailabilitySlots.IgnoreQueryFilters()
        .Where(s => s.Id == slotId)
        .ExecuteUpdateAsync(set => set.SetProperty(s => s.IsDeleted, true)));

    private async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work, SemaphoreSlim? gate = null)
    {
        using var scope = Factory.Services.CreateScope();
        if (gate != null)
            await gate.WaitAsync();
        return await work(scope.ServiceProvider);
    }

    private Task<BookingResultDto> CreateBookingAsync(Seed seed, SemaphoreSlim? gate = null)
        => InScopeAsync(sp => sp.GetRequiredService<IBookingService>()
            .CreateBookingAsync(seed.StudentUserId, new CreateBookingDto { AvailabilitySlotId = seed.SlotId }), gate);

    private sealed record SlotState(bool SlotDeleted, List<BookingStatus> Statuses)
    {
        public bool HasActiveBooking => Statuses.Any(s => s is BookingStatus.Pending or BookingStatus.Approved);
    }

    private Task<SlotState> SlotStateAsync(int slotId) => WithDbAsync(async db => new SlotState(
        await db.TeacherAvailabilitySlots.IgnoreQueryFilters().Where(s => s.Id == slotId).Select(s => s.IsDeleted).SingleAsync(),
        await db.Bookings.AsNoTracking().Where(b => b.AvailabilitySlotId == slotId).Select(b => b.Status).ToListAsync()));

    // ------------------------------------------------------------------
    // (a) okuma: adlı filtre Npgsql'de
    // ------------------------------------------------------------------

    [Fact]
    public async Task Approved_booking_with_soft_deleted_slot_is_readable_for_live_session_and_lists()
    {
        var seed = await SeedAsync();
        var bookingId = await SeedBookingAsync(seed, BookingStatus.Approved);
        await SoftDeleteSlotAsync(seed.SlotId);

        var (access, teacherList, studentList) = await InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IBookingService>();
            return (await service.GetLiveSessionAccessAsync(seed.TeacherUserId, bookingId),
                await service.GetTeacherBookingsAsync(seed.TeacherUserId, 0, 50),
                await service.GetStudentBookingsAsync(seed.StudentUserId, 0, 50));
        });

        // Ders 7 gün sonra: pencere henüz açık değil — ama "bulunamadı" DEĞİL.
        access.Denial.ShouldBe(BookingLiveSessionDenial.WindowNotOpen);
        teacherList.Items.ShouldHaveSingleItem().Id.ShouldBe(bookingId);
        studentList.Items.ShouldHaveSingleItem().Date.ShouldBe(Future);

        // Diğer filtreler geçerli: booking silinirse yine bulunamaz.
        await WithDbAsync(db => db.Bookings.Where(b => b.Id == bookingId).ExecuteUpdateAsync(set => set.SetProperty(b => b.IsDeleted, true)));
        var afterDelete = await InScopeAsync(sp => sp.GetRequiredService<IBookingService>().GetLiveSessionAccessAsync(seed.TeacherUserId, bookingId));
        afterDelete.Denial.ShouldBe(BookingLiveSessionDenial.NotFound);
    }

    // ------------------------------------------------------------------
    // (b) silme engeli + HTTP sözleşmesi
    // ------------------------------------------------------------------

    [Fact]
    public async Task Delete_slot_endpoint_with_an_active_booking_returns_409_with_error_code()
    {
        var seed = await SeedAsync();
        await SeedBookingAsync(seed, BookingStatus.Approved);
        var client = await ClientAsAsync(seed.TeacherUserId, "Teacher", $"kc-376-{seed.TeacherUserId}", "Teacher");

        var response = await client.DeleteAsync($"/api/booking/slots/{seed.SlotId}");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("errorCode").GetString().ShouldBe(BookingErrorCodes.SlotHasActiveBooking);
        body.RootElement.GetProperty("message").GetString().ShouldNotBeNullOrWhiteSpace();
        (await SlotStateAsync(seed.SlotId)).SlotDeleted.ShouldBeFalse();
    }

    // ------------------------------------------------------------------
    // (b) eşzamanlılık
    // ------------------------------------------------------------------

    [Fact]
    public async Task Parallel_booking_request_and_slot_delete_never_leave_an_active_booking_on_a_deleted_slot()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var seed = await SeedAsync();
            using var gate = new SemaphoreSlim(0, 2);

            var create = Task.Run(() => CreateBookingAsync(seed, gate));
            var delete = Task.Run(() => InScopeAsync(sp => sp.GetRequiredService<IBookingService>()
                .DeleteSlotAsync(seed.TeacherUserId, seed.SlotId), gate));
            gate.Release(2);
            await Task.WhenAll(create, delete);

            var state = await SlotStateAsync(seed.SlotId);
            (state.SlotDeleted && state.HasActiveBooking).ShouldBeFalse($"round {round}: silinmiş slotta aktif randevu kalmamalı");

            var created = await create;
            var deleted = await delete;
            if (created.Success)
            {
                // Talep önce kilidi aldı → silme randevuyu gördü ve 409 döndü.
                deleted.Conflict.ShouldBeTrue($"round {round}");
                deleted.ErrorCode.ShouldBe(BookingErrorCodes.SlotHasActiveBooking, $"round {round}");
                state.SlotDeleted.ShouldBeFalse($"round {round}");
            }
            else
            {
                // Silme önce kazandı → talep slotu bulamadı.
                created.NotFound.ShouldBeTrue($"round {round}: {created.Message}");
                deleted.Success.ShouldBeTrue($"round {round}");
                state.Statuses.ShouldBeEmpty($"round {round}");
            }
        }
    }

    [Fact]
    public async Task Parallel_booking_request_and_series_delete_never_leave_an_active_booking_on_a_deleted_slot()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var seed = await SeedAsync(recurring: true);
            using var gate = new SemaphoreSlim(0, 2);

            var create = Task.Run(() => CreateBookingAsync(seed, gate));
            var delete = Task.Run(() => InScopeAsync(sp => sp.GetRequiredService<IRecurringAvailabilityService>()
                .DeleteRuleAsync(seed.TeacherUserId, seed.RuleId!.Value), gate));
            gate.Release(2);
            await Task.WhenAll(create, delete);

            var deleted = await delete;
            deleted.Success.ShouldBeTrue($"round {round}: {deleted.Message}");
            var state = await SlotStateAsync(seed.SlotId);
            (state.SlotDeleted && state.HasActiveBooking).ShouldBeFalse($"round {round}: silinmiş slotta aktif randevu kalmamalı");

            if ((await create).Success)
                deleted.PreservedSlotIds.ShouldBe([seed.SlotId], $"round {round}");
            else
                deleted.DeletedSlotIds.ShouldBe([seed.SlotId], $"round {round}");
        }
    }

    [Fact]
    public async Task Booking_request_waits_for_an_uncommitted_slot_delete_under_the_lock_and_is_then_refused()
    {
        var seed = await SeedAsync();

        // Silme transaction'ı kilidi aldı ve slotu soft-delete etti ama henüz commit etmedi.
        using var holderScope = Factory.Services.CreateScope();
        var holder = holderScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await holder.Database.BeginTransactionAsync();
        await holder.Database.AcquireTeacherAvailabilityLockAsync(seed.TeacherId);
        await holder.Database.ExecuteSqlRawAsync(
            """UPDATE "TeacherAvailabilitySlots" SET "IsDeleted" = TRUE WHERE "Id" = {0}""", seed.SlotId);

        var create = Task.Run(() => CreateBookingAsync(seed));

        // Kilitsiz eski kod burada (FK kontrolü soft-delete'i görmez) silinen slota Pending talep yazardı.
        (await Task.WhenAny(create, Task.Delay(TimeSpan.FromMilliseconds(750)))).ShouldNotBe(create);

        await tx.CommitAsync();
        var created = await create;

        created.NotFound.ShouldBeTrue(created.Message);
        (await SlotStateAsync(seed.SlotId)).Statuses.ShouldBeEmpty();
    }
}
