using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos.Bookings;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Services.Bookings;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// issue #331 (#320 L4) — booking talebi / öğretmen askısı yarışı gerçek PostgreSQL'de. Talep insert'i öğretmen satırını
/// <c>FOR SHARE</c> ile kilitleyip koşulu aynı transaction'da doğrular; askının koşullu UPDATE'i aynı satırı kilitler →
/// ikisi serileşir. Hangi taraf önce gelirse gelsin askıdaki öğretmende Pending talep kalmaz. Ayrıca güvenlik ağı süpürmesi
/// kilit dışı yazılmış bir Pending talebi kapatır ve tekrar çalıştığında ikinci event yazmaz.
/// </summary>
public class BookingSuspensionRacePostgresTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const int Rounds = 12;

    // FakeUserDirectory Respawn ile sıfırlanmaz → benzersiz, büyük UserId.
    private static int NewUserId() => 1_000_000 + Random.Shared.Next(0, 900_000_000);

    private static readonly DateOnly Future = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);

    private sealed record Seed(int TeacherId, int StudentUserId, int SlotId);

    private async Task<Seed> SeedAsync()
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
                Bio = "t"
            };
            db.Teachers.Add(teacher);
            db.Students.Add(new Student { UserId = studentUserId, StudentNumber = $"S{studentUserId}" });
            await db.SaveChangesAsync();

            var slot = new TeacherAvailabilitySlot
            {
                TeacherId = teacher.Id, Date = Future, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(11, 0),
                CreatedAt = DateTime.UtcNow
            };
            db.TeacherAvailabilitySlots.Add(slot);
            await db.SaveChangesAsync();
            return new Seed(teacher.Id, studentUserId, slot.Id);
        });
    }

    private async Task<BookingResultDto> CreateBookingAsync(Seed seed, SemaphoreSlim? gate = null)
    {
        using var scope = Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IBookingService>();
        if (gate != null)
            await gate.WaitAsync();
        return await service.CreateBookingAsync(seed.StudentUserId, new CreateBookingDto { AvailabilitySlotId = seed.SlotId });
    }

    private async Task<AdminTeacherSuspensionResult> SuspendAsync(int teacherId, SemaphoreSlim? gate = null)
    {
        using var scope = Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminTeacherSuspensionService>();
        if (gate != null)
            await gate.WaitAsync();
        return await service.SuspendAsync(teacherId, "yarış testi", $"kc-admin-{Guid.NewGuid():N}", 2);
    }

    private Task<List<BookingStatus>> StatusesAsync(int teacherId) => WithDbAsync(db =>
        db.Bookings.AsNoTracking().Where(b => b.TeacherId == teacherId).Select(b => b.Status).ToListAsync());

    private Task<int> DecisionEventCountAsync(IEnumerable<int> bookingIds) => WithDbAsync(async db =>
    {
        var ids = bookingIds.ToHashSet();
        var type = OutboxEventRegistry.NameFor<BookingDecisionEvent>();
        return (await db.OutboxMessages.AsNoTracking().Where(m => m.Type == type).Select(m => m.Content).ToListAsync())
            .Select(c => System.Text.Json.JsonSerializer.Deserialize<BookingDecisionEvent>(c)!)
            .Count(e => ids.Contains(e.BookingId) && e.TeacherUnavailable);
    });

    private static async Task ShouldStillBeWaitingAsync(Task task)
        => (await Task.WhenAny(task, Task.Delay(TimeSpan.FromMilliseconds(750)))).ShouldNotBe(task);

    [Fact]
    public async Task Parallel_booking_request_and_suspension_never_leave_a_pending_request_on_the_suspended_teacher()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var seed = await SeedAsync();
            using var gate = new SemaphoreSlim(0, 2);

            var create = Task.Run(() => CreateBookingAsync(seed, gate));
            var suspend = Task.Run(() => SuspendAsync(seed.TeacherId, gate));
            gate.Release(2);
            await Task.WhenAll(create, suspend);

            (await suspend).Status.ShouldBe(AdminTeacherSuspensionStatus.Success, $"round {round}");
            var statuses = await StatusesAsync(seed.TeacherId);
            statuses.ShouldNotContain(BookingStatus.Pending, $"round {round}: askıdaki öğretmende Pending kalmamalı");

            var created = await create;
            if (created.Success)
            {
                // Talep önce kilidi aldı → askı onu gördü ve aynı transaction'da reddetti (tek bildirim).
                statuses.ShouldBe([BookingStatus.Rejected], $"round {round}");
                (await DecisionEventCountAsync([created.ObjectId])).ShouldBe(1, $"round {round}");
            }
            else
            {
                created.NotFound.ShouldBeTrue($"round {round}");
                statuses.ShouldBeEmpty($"round {round}");
            }
        }
    }

    [Fact]
    public async Task Booking_request_waits_for_an_uncommitted_suspension_and_is_then_refused()
    {
        var seed = await SeedAsync();

        // Askı transaction'ı öğretmen satırını güncelledi ama henüz commit etmedi.
        using var holderScope = Factory.Services.CreateScope();
        var holder = holderScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await holder.Database.BeginTransactionAsync();
        await holder.Database.ExecuteSqlRawAsync(
            """UPDATE "Teachers" SET "AccountSuspendedAt" = now(), "AccountApprovedAt" = NULL WHERE "Id" = {0}""", seed.TeacherId);

        var create = Task.Run(() => CreateBookingAsync(seed));

        // Kilitsiz eski kod burada askıyı görmeden (FK kontrolü yalnız FOR KEY SHARE alır) Pending talebi yazardı.
        await ShouldStillBeWaitingAsync(create);

        await tx.CommitAsync();
        var result = await create.WaitAsync(TimeSpan.FromSeconds(30));

        result.Success.ShouldBeFalse();
        result.NotFound.ShouldBeTrue();
        (await StatusesAsync(seed.TeacherId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Suspension_waits_for_an_in_flight_booking_request_and_then_rejects_it()
    {
        var seed = await SeedAsync();

        // Talep transaction'ı öğretmen satırını FOR SHARE ile kilitledi ve Pending talebi yazdı, henüz commit etmedi.
        var studentId = await WithDbAsync(db => db.Students.Where(s => s.UserId == seed.StudentUserId).Select(s => s.Id).SingleAsync());
        using var holderScope = Factory.Services.CreateScope();
        var holder = holderScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await holder.Database.BeginTransactionAsync();
        await holder.Database.ExecuteSqlRawAsync(
            """SELECT 1 FROM "Teachers" WHERE "Id" = {0} AND "AccountSuspendedAt" IS NULL FOR SHARE""", seed.TeacherId);
        await holder.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "Bookings" ("TeacherId", "StudentId", "AvailabilitySlotId", "Status", "CreatedAt", "CreateTime", "IsDeleted")
            VALUES ({0}, {1}, {2}, {3}, now(), now(), FALSE)
            """, seed.TeacherId, studentId, seed.SlotId, (int)BookingStatus.Pending);

        var suspend = Task.Run(() => SuspendAsync(seed.TeacherId));
        await ShouldStillBeWaitingAsync(suspend);

        await tx.CommitAsync();
        (await suspend.WaitAsync(TimeSpan.FromSeconds(30))).Status.ShouldBe(AdminTeacherSuspensionStatus.Success);

        var bookingIds = await WithDbAsync(db => db.Bookings.AsNoTracking().Where(b => b.TeacherId == seed.TeacherId)
            .Select(b => b.Id).ToListAsync());
        (await StatusesAsync(seed.TeacherId)).ShouldBe([BookingStatus.Rejected]);
        (await DecisionEventCountAsync(bookingIds)).ShouldBe(1);
    }

    [Fact]
    public async Task Sweep_closes_a_pending_request_written_outside_the_lock_once()
    {
        var seed = await SeedAsync();
        (await SuspendAsync(seed.TeacherId)).Status.ShouldBe(AdminTeacherSuspensionStatus.Success);

        // Kilit dışı bir yazıcı (doğrudan SQL) askıdaki öğretmene Pending talep bıraktı.
        var studentId = await WithDbAsync(db => db.Students.Where(s => s.UserId == seed.StudentUserId).Select(s => s.Id).SingleAsync());
        await WithDbAsync(db => db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "Bookings" ("TeacherId", "StudentId", "AvailabilitySlotId", "Status", "CreatedAt", "CreateTime", "IsDeleted")
            VALUES ({0}, {1}, {2}, {3}, now(), now(), FALSE)
            """, seed.TeacherId, studentId, seed.SlotId, (int)BookingStatus.Pending));

        async Task<int> SweepAsync()
        {
            using var scope = Factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ISuspendedTeacherBookingSweepJob>().SweepAsync();
        }

        (await SweepAsync()).ShouldBe(1);
        (await SweepAsync()).ShouldBe(0);

        var bookingIds = await WithDbAsync(db => db.Bookings.AsNoTracking().Where(b => b.TeacherId == seed.TeacherId)
            .Select(b => b.Id).ToListAsync());
        (await StatusesAsync(seed.TeacherId)).ShouldBe([BookingStatus.Rejected]);
        (await DecisionEventCountAsync(bookingIds)).ShouldBe(1);
    }
}
