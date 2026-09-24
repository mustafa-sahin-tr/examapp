using ExamApp.Api.Consumers;
using ExamApp.Api.Data;
using ExamApp.Api.Services.StudentPoints;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #225: BadgeService StudentPointsChangedEvent → StudentPoints versiyonlu mutlak-değer upsert.
/// </summary>
public class StudentPointsSyncServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private static readonly DateTime T0 = new(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);

    public void Dispose() => _db.Dispose();

    private async Task<int> SeedStudentAsync(int userId = 42)
    {
        await using var ctx = _db.NewContext();
        var s = new Student { UserId = userId, StudentNumber = $"S{userId}" };
        ctx.Students.Add(s);
        await ctx.SaveChangesAsync();
        return s.Id;
    }

    private async Task<StudentPointsSyncResult> ApplyAsync(int userId, int points, DateTime version, params IInterceptor[] interceptors)
    {
        await using var ctx = interceptors.Length > 0 ? _db.NewContext(interceptors) : _db.NewContext();
        return await new StudentPointsSyncService(ctx, NullLogger<StudentPointsSyncService>.Instance)
            .ApplyAsync(new StudentPointsChangedEvent { UserId = userId, TotalPoints = points, UpdatedAtUtc = version });
    }

    private async Task<List<StudentPoint>> RowsAsync()
    {
        await using var ctx = _db.NewContext();
        return await ctx.StudentPoints.IgnoreQueryFilters().AsNoTracking().ToListAsync();
    }

    [Fact]
    public async Task First_event_creates_the_row_with_the_absolute_value()
    {
        var studentId = await SeedStudentAsync();

        (await ApplyAsync(42, 120, T0)).ShouldBe(StudentPointsSyncResult.Applied);

        var row = (await RowsAsync()).Single();
        row.StudentId.ShouldBe(studentId);
        row.XP.ShouldBe(120);
        row.SourceUpdatedAtUtc.ShouldBe(T0);
    }

    [Fact]
    public async Task Same_event_delivered_twice_leaves_a_single_row_with_the_same_value()
    {
        await SeedStudentAsync();

        (await ApplyAsync(42, 120, T0)).ShouldBe(StudentPointsSyncResult.Applied);
        (await ApplyAsync(42, 120, T0)).ShouldBe(StudentPointsSyncResult.Stale);

        var row = (await RowsAsync()).ShouldHaveSingleItem();
        row.XP.ShouldBe(120);
    }

    [Fact]
    public async Task Newer_event_overwrites_with_the_new_absolute_value()
    {
        await SeedStudentAsync();

        await ApplyAsync(42, 120, T0);
        (await ApplyAsync(42, 150, T0.AddSeconds(3))).ShouldBe(StudentPointsSyncResult.Applied);

        var row = (await RowsAsync()).ShouldHaveSingleItem();
        row.XP.ShouldBe(150);
        row.SourceUpdatedAtUtc.ShouldBe(T0.AddSeconds(3));
    }

    [Fact]
    public async Task Older_event_arriving_late_does_not_overwrite_the_newer_value()
    {
        await SeedStudentAsync();

        await ApplyAsync(42, 150, T0.AddSeconds(3));
        (await ApplyAsync(42, 120, T0)).ShouldBe(StudentPointsSyncResult.Stale);

        var row = (await RowsAsync()).ShouldHaveSingleItem();
        row.XP.ShouldBe(150);
        row.SourceUpdatedAtUtc.ShouldBe(T0.AddSeconds(3));
    }

    [Fact]
    public async Task Missing_student_is_skipped_without_error()
    {
        await SeedStudentAsync(userId: 1);

        (await ApplyAsync(999, 50, T0)).ShouldBe(StudentPointsSyncResult.StudentNotFound);

        (await RowsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Soft_deleted_student_counts_as_missing()
    {
        await SeedStudentAsync();
        await using (var ctx = _db.NewContext())
        {
            ctx.Students.Remove(await ctx.Students.SingleAsync());
            await ctx.SaveChangesAsync();
        }

        (await ApplyAsync(42, 50, T0)).ShouldBe(StudentPointsSyncResult.StudentNotFound);
    }

    [Fact]
    public async Task Legacy_row_without_a_version_is_updated()
    {
        var studentId = await SeedStudentAsync();
        await using (var ctx = _db.NewContext())
        {
            ctx.StudentPoints.Add(new StudentPoint { StudentId = studentId, XP = 7 });
            await ctx.SaveChangesAsync();
        }

        (await ApplyAsync(42, 200, T0)).ShouldBe(StudentPointsSyncResult.Applied);

        var row = (await RowsAsync()).ShouldHaveSingleItem();
        row.XP.ShouldBe(200);
    }

    [Fact]
    public async Task Reset_soft_deleted_row_is_revived_only_by_a_newer_event()
    {
        var studentId = await SeedStudentAsync();
        await ApplyAsync(42, 300, T0);
        await using (var ctx = _db.NewContext())
        {
            var sp = await ctx.StudentPoints.SingleAsync();
            ctx.StudentPoints.Remove(sp); // StudentResetJob: soft delete
            await ctx.SaveChangesAsync();
        }

        // Reset sırasında uçuşta kalmış eski event satırı geri getirmez.
        (await ApplyAsync(42, 300, T0)).ShouldBe(StudentPointsSyncResult.Stale);
        (await RowsAsync()).ShouldHaveSingleItem().IsDeleted.ShouldBeTrue();

        // BadgeService reset'inin 0 event'i (daha yeni) satırı canlandırır.
        (await ApplyAsync(42, 0, T0.AddMinutes(1))).ShouldBe(StudentPointsSyncResult.Applied);
        var row = (await RowsAsync()).ShouldHaveSingleItem();
        row.StudentId.ShouldBe(studentId);
        row.IsDeleted.ShouldBeFalse();
        row.DeleteTime.ShouldBeNull();
        row.XP.ShouldBe(0);
    }

    [Fact]
    public async Task Concurrent_insert_race_retries_and_the_newer_version_wins()
    {
        var studentId = await SeedStudentAsync();

        // Bu teslim INSERT'e karar verdikten sonra, kaydetmeden hemen önce eşzamanlı bir teslim
        // aynı öğrenci için DAHA ESKİ versiyonla satırı ekliyor → UNIQUE ihlali → retry → koşullu UPDATE.
        var racer = new ConcurrentInsert(_db, studentId, xp: 10, version: T0);
        (await ApplyAsync(42, 99, T0.AddSeconds(1), racer)).ShouldBe(StudentPointsSyncResult.Applied);

        racer.Fired.ShouldBeTrue();
        var row = (await RowsAsync()).ShouldHaveSingleItem();
        row.XP.ShouldBe(99);
        row.SourceUpdatedAtUtc.ShouldBe(T0.AddSeconds(1));
    }

    [Fact]
    public async Task Far_future_version_is_clamped_so_later_updates_are_not_locked_out()
    {
        await SeedStudentAsync();

        await ApplyAsync(42, 10, DateTime.UtcNow.AddDays(30));
        (await RowsAsync()).Single().SourceUpdatedAtUtc!.Value.ShouldBeLessThan(DateTime.UtcNow.AddMinutes(1));

        (await ApplyAsync(42, 20, DateTime.UtcNow.AddSeconds(2))).ShouldBe(StudentPointsSyncResult.Applied);
        (await RowsAsync()).Single().XP.ShouldBe(20);
    }

    [Fact]
    public async Task Negative_points_are_stored_as_zero()
    {
        await SeedStudentAsync();
        await ApplyAsync(42, -5, T0);
        (await RowsAsync()).Single().XP.ShouldBe(0);
    }

    // ---- consumer ----

    [Fact]
    public async Task Consumer_delegates_to_the_sync_service()
    {
        var sync = Substitute.For<IStudentPointsSyncService>();
        var evt = new StudentPointsChangedEvent { UserId = 1, TotalPoints = 5, UpdatedAtUtc = T0 };
        var context = Substitute.For<ConsumeContext<StudentPointsChangedEvent>>();
        context.Message.Returns(evt);

        await new StudentPointsChangedConsumer(sync, NullLogger<StudentPointsChangedConsumer>.Instance, Options()).Consume(context);

        await sync.Received(1).ApplyAsync(evt, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consumer_rethrows_unexpected_errors_so_masstransit_can_retry_and_dead_letter()
    {
        var sync = Substitute.For<IStudentPointsSyncService>();
        sync.ApplyAsync(Arg.Any<StudentPointsChangedEvent>(), Arg.Any<CancellationToken>())
            .Returns<Task<StudentPointsSyncResult>>(_ => throw new InvalidOperationException("db down"));
        var context = Substitute.For<ConsumeContext<StudentPointsChangedEvent>>();
        context.Message.Returns(new StudentPointsChangedEvent { UserId = 1 });

        await Should.ThrowAsync<InvalidOperationException>(() =>
            new StudentPointsChangedConsumer(sync, NullLogger<StudentPointsChangedConsumer>.Instance, Options()).Consume(context));
    }

    private static Microsoft.Extensions.Options.IOptions<StudentPointsSyncOptions> Options(int max = 10_000_000)
        => Microsoft.Extensions.Options.Options.Create(new StudentPointsSyncOptions { MaxTotalPoints = max });

    [Fact]
    public async Task Consumer_drops_points_above_the_configured_ceiling_without_calling_the_service()
    {
        var sync = Substitute.For<IStudentPointsSyncService>();
        var context = Substitute.For<ConsumeContext<StudentPointsChangedEvent>>();
        context.Message.Returns(new StudentPointsChangedEvent { UserId = 1, TotalPoints = 1_001, UpdatedAtUtc = T0 });

        await new StudentPointsChangedConsumer(sync, NullLogger<StudentPointsChangedConsumer>.Instance, Options(max: 1_000))
            .Consume(context);

        await sync.DidNotReceiveWithAnyArgs().ApplyAsync(default!, default);
    }

    [Fact]
    public async Task Consumer_accepts_points_exactly_at_the_ceiling()
    {
        var sync = Substitute.For<IStudentPointsSyncService>();
        var context = Substitute.For<ConsumeContext<StudentPointsChangedEvent>>();
        context.Message.Returns(new StudentPointsChangedEvent { UserId = 1, TotalPoints = 1_000, UpdatedAtUtc = T0 });

        await new StudentPointsChangedConsumer(sync, NullLogger<StudentPointsChangedConsumer>.Instance, Options(max: 1_000))
            .Consume(context);

        await sync.ReceivedWithAnyArgs(1).ApplyAsync(default!, default);
    }

    [Fact]
    public void Default_ceiling_is_ten_million()
        => new StudentPointsSyncOptions().MaxTotalPoints.ShouldBe(10_000_000);

    [Fact]
    public async Task Non_unique_db_errors_are_not_retried_and_propagate()
    {
        // FK ihlali (olmayan öğrenci) UNIQUE değil → retry filtresine takılmaz, yukarı fırlar.
        await using var ctx = _db.NewContext();
        await ctx.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;");
        ctx.StudentPoints.Add(new StudentPoint { StudentId = 12345, XP = 1 });
        var ex = await Should.ThrowAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        StudentPointsSyncService.IsUniqueViolation(ex).ShouldBeFalse();
    }

    [Fact]
    public async Task Unique_violation_is_recognised_on_sqlite()
    {
        var studentId = await SeedStudentAsync();
        await using (var first = _db.NewContext())
        {
            first.StudentPoints.Add(new StudentPoint { StudentId = studentId, XP = 1 });
            await first.SaveChangesAsync();
        }
        await using var second = _db.NewContext();
        second.StudentPoints.Add(new StudentPoint { StudentId = studentId, XP = 2 });
        var ex = await Should.ThrowAsync<DbUpdateException>(() => second.SaveChangesAsync());
        StudentPointsSyncService.IsUniqueViolation(ex).ShouldBeTrue();
    }

    /// <summary>İlk SaveChanges'ten hemen önce ayrı bir bağlantı/bağlamla aynı öğrenci için satır ekler.</summary>
    private sealed class ConcurrentInsert(TestDb db, int studentId, int xp, DateTime version) : SaveChangesInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Fired)
            {
                Fired = true;
                await using var other = db.NewContext();
                other.StudentPoints.Add(new StudentPoint { StudentId = studentId, XP = xp, SourceUpdatedAtUtc = version });
                await other.SaveChangesAsync(cancellationToken);
            }
            return result;
        }
    }
}
