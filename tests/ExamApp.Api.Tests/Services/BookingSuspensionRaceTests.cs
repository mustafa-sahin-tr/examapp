using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Bookings;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Services.Bookings;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Video;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #331 (#320 L4): booking talebi / öğretmen askısı yarışı.
/// (1) Talep insert'i öğretmenin talep alabilir olduğunu AYNI transaction'da yeniden doğrular — kilitsiz ön okumadan sonra
/// askı araya girerse talep oluşmaz. SQLite'ta <c>FOR SHARE</c> yok; burada koşulun transaction içinde yeniden okunduğu
/// doğrulanır. Gerçek eşzamanlılık: <c>BookingSuspensionRacePostgresTests</c> (IntegrationTests).
/// (2) Güvenlik ağı <see cref="SuspendedTeacherBookingSweepJob"/>: askıdaki öğretmende kalan Pending talepleri #298 yoluyla
/// (BookingDecisionEvent, TeacherUnavailable) kapatır; idempotent — ikinci tur/önceden kapatılmış talep için event yok.
/// </summary>
public class BookingSuspensionRaceTests : IDisposable
{
    private const int TeacherId = 10, TeacherUserId = 100;
    private const int OtherTeacherId = 11, OtherTeacherUserId = 101;
    private const int StudentA = 20, StudentAUser = 200;
    private const int StudentB = 21, StudentBUser = 201;

    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Future = new(2026, 6, 20);

    private readonly TestDb _db = TestDb.Create();
    private readonly FixedTimeProvider _clock = new(Now);
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    public BookingSuspensionRaceTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var ids = ci.Arg<IEnumerable<int>>().ToHashSet();
                var all = new List<UserLookupResultDto>
                {
                    new() { Id = TeacherUserId, FullName = "Ayşe Öğretmen", KeycloakId = "kc-t" },
                    new() { Id = OtherTeacherUserId, FullName = "Bora Öğretmen", KeycloakId = "kc-t2" },
                    new() { Id = StudentAUser, FullName = "A", KeycloakId = "kc-a" },
                    new() { Id = StudentBUser, FullName = "B", KeycloakId = "kc-b" },
                };
                return (IReadOnlyList<UserLookupResultDto>)all.Where(u => ids.Contains(u.Id)).ToList();
            });

        using var ctx = _db.NewContext();
        foreach (var (id, userId) in new[] { (TeacherId, TeacherUserId), (OtherTeacherId, OtherTeacherUserId) })
            ctx.Teachers.Add(new Teacher
            {
                Id = id, UserId = userId, ApprovalStatus = TeacherApprovalStatus.Approved, Bio = "t",
                AccountApprovedAt = Now.UtcDateTime.AddDays(-30)
            });
        foreach (var (id, userId) in new[] { (StudentA, StudentAUser), (StudentB, StudentBUser) })
            ctx.Students.Add(new Student { Id = id, UserId = userId, StudentNumber = $"S{id}" });
        ctx.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    // ---------------- yardımcılar ----------------

    private BookingService NewBookingService(AppDbContext ctx)
        => new(ctx, _authApi, Substitute.For<IVideoSessionProvider>(), Options.Create(new VideoOptions()), _clock,
            new RecurringAvailabilityService(ctx, _clock, NullLogger<RecurringAvailabilityService>.Instance),
            NullLogger<BookingService>.Instance, new ExamApp.Api.Services.Tenancy.SchoolAccessPolicy(ctx));

    private sealed class Monitor(SuspendedTeacherBookingSweepOptions value) : IOptionsMonitor<SuspendedTeacherBookingSweepOptions>
    {
        public SuspendedTeacherBookingSweepOptions CurrentValue => value;
        public SuspendedTeacherBookingSweepOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<SuspendedTeacherBookingSweepOptions, string?> listener) => null;
    }

    private SuspendedTeacherBookingSweepJob NewSweep(AppDbContext ctx, int batchSize = 500, IAuthApiClient? authApi = null)
        => new(ctx, new Monitor(new SuspendedTeacherBookingSweepOptions { BatchSize = batchSize }), authApi ?? _authApi, _clock);

    private async Task<int> SweepAsync(int batchSize = 500, IAuthApiClient? authApi = null)
    {
        await using var ctx = _db.NewContextWithRetryingExecutionStrategy();
        return await NewSweep(ctx, batchSize, authApi).SweepAsync();
    }

    private async Task<int> SeedSlotAsync(int teacherId, int hour)
    {
        await using var ctx = _db.NewContext();
        var slot = new TeacherAvailabilitySlot
        {
            TeacherId = teacherId, Date = Future, StartTime = new TimeOnly(hour, 0), EndTime = new TimeOnly(hour + 1, 0),
            CreatedAt = Now.UtcDateTime
        };
        ctx.TeacherAvailabilitySlots.Add(slot);
        await ctx.SaveChangesAsync();
        return slot.Id;
    }

    private async Task<int> AddBookingAsync(int teacherId, int studentId, BookingStatus status, int hour)
    {
        await using var ctx = _db.NewContext();
        var booking = BookingSeed.Add(ctx, teacherId, studentId, status, hour, Future);
        await ctx.SaveChangesAsync();
        return booking.Id;
    }

    /// <summary>Askıyı doğrudan yazar (yan etkisiz) — yarışta askı akışının Pending'leri görmediği durumu kurar.</summary>
    private static Task SuspendRowAsync(AppDbContext ctx, int teacherId)
        => ctx.Teachers.Where(t => t.Id == teacherId).ExecuteUpdateAsync(s => s
            .SetProperty(t => t.AccountSuspendedAt, Now.UtcDateTime)
            .SetProperty(t => t.AccountApprovedAt, (DateTime?)null));

    private async Task SuspendRowAsync(int teacherId)
    {
        await using var ctx = _db.NewContext();
        await SuspendRowAsync(ctx, teacherId);
    }

    private async Task<List<T>> EventsAsync<T>()
    {
        await using var ctx = _db.NewContext();
        return (await ctx.OutboxMessages.AsNoTracking().ToListAsync())
            .Where(m => m.Type == OutboxEventRegistry.NameFor<T>())
            .Select(m => JsonSerializer.Deserialize<T>(m.Content)!)
            .ToList();
    }

    private async Task<int> OutboxCountAsync()
    {
        await using var ctx = _db.NewContext();
        return await ctx.OutboxMessages.CountAsync();
    }

    private async Task<Booking> BookingAsync(int id)
    {
        await using var ctx = _db.NewContext();
        return await ctx.Bookings.AsNoTracking().SingleAsync(b => b.Id == id);
    }

    private async Task<List<Booking>> BookingsOfAsync(int teacherId)
    {
        await using var ctx = _db.NewContext();
        return await ctx.Bookings.AsNoTracking().Where(b => b.TeacherId == teacherId).ToListAsync();
    }

    // ---------------- talep oluşturma: transaction içi yeniden doğrulama ----------------

    [Fact]
    public async Task Create_booking_for_an_active_teacher_still_writes_the_booking_and_one_request_event()
    {
        var slotId = await SeedSlotAsync(TeacherId, 10);

        await using (var ctx = _db.NewContextWithRetryingExecutionStrategy())
            (await NewBookingService(ctx).CreateBookingAsync(StudentAUser, new CreateBookingDto { AvailabilitySlotId = slotId }))
                .Success.ShouldBeTrue();

        (await BookingsOfAsync(TeacherId)).Single().Status.ShouldBe(BookingStatus.Pending);
        (await EventsAsync<BookingRequestCreatedEvent>()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Suspension_committed_after_the_pre_read_rejects_the_insert_in_the_same_transaction()
    {
        var slotId = await SeedSlotAsync(TeacherId, 10);

        // Kilitsiz ön okuma öğretmeni aktif gördü; talep transaction'ı başlarken askı "araya girer".
        var interceptor = new OnTransactionStartedInterceptor(async transaction =>
        {
            await using var other = _db.NewContext();
            await other.Database.UseTransactionAsync(transaction);
            await SuspendRowAsync(other, TeacherId);
        });

        BookingResultDto result;
        await using (var ctx = _db.NewContext(interceptor))
            result = await NewBookingService(ctx).CreateBookingAsync(StudentAUser, new CreateBookingDto { AvailabilitySlotId = slotId });

        interceptor.Fired.ShouldBeTrue();
        result.Success.ShouldBeFalse();
        result.NotFound.ShouldBeTrue(); // ön okumadaki askı yanıtıyla aynı (slot görünmez)
        (await BookingsOfAsync(TeacherId)).ShouldBeEmpty();
        (await OutboxCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Teacher_whose_application_approval_was_withdrawn_in_between_gets_no_request_either()
    {
        var slotId = await SeedSlotAsync(TeacherId, 10);

        var interceptor = new OnTransactionStartedInterceptor(async transaction =>
        {
            await using var other = _db.NewContext();
            await other.Database.UseTransactionAsync(transaction);
            await other.Teachers.Where(t => t.Id == TeacherId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ApprovalStatus, TeacherApprovalStatus.Rejected));
        });

        await using (var ctx = _db.NewContext(interceptor))
            (await NewBookingService(ctx).CreateBookingAsync(StudentAUser, new CreateBookingDto { AvailabilitySlotId = slotId }))
                .NotFound.ShouldBeTrue();

        (await BookingsOfAsync(TeacherId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Failed_commit_retry_writes_the_booking_and_its_event_exactly_once()
    {
        var slotId = await SeedSlotAsync(TeacherId, 10);
        var interceptor = new FailFirstCommitInterceptor();

        await using (var ctx = _db.NewContextWithTransientRetry(interceptor))
            (await NewBookingService(ctx).CreateBookingAsync(StudentAUser, new CreateBookingDto { AvailabilitySlotId = slotId }))
                .Success.ShouldBeTrue();

        interceptor.Failures.ShouldBe(1);
        (await BookingsOfAsync(TeacherId)).Count.ShouldBe(1);
        (await EventsAsync<BookingRequestCreatedEvent>()).Count.ShouldBe(1);
    }

    // ---------------- süpürme (güvenlik ağı) ----------------

    [Fact]
    public async Task Sweep_rejects_pending_requests_left_on_a_suspended_teacher_with_one_teacher_unavailable_event_each()
    {
        var leftA = await AddBookingAsync(TeacherId, StudentA, BookingStatus.Pending, 9);
        var leftB = await AddBookingAsync(TeacherId, StudentB, BookingStatus.Pending, 11);
        var approved = await AddBookingAsync(TeacherId, StudentA, BookingStatus.Approved, 13);
        var otherTeachers = await AddBookingAsync(OtherTeacherId, StudentA, BookingStatus.Pending, 9);
        await SuspendRowAsync(TeacherId);

        (await SweepAsync()).ShouldBe(2);

        foreach (var id in new[] { leftA, leftB })
        {
            var b = await BookingAsync(id);
            b.Status.ShouldBe(BookingStatus.Rejected);
            b.DecisionAt.ShouldBe(Now.UtcDateTime);
            b.UpdateTime.ShouldBe(Now.UtcDateTime);
            b.UpdateUserId.ShouldBeNull(); // sistem
            b.RejectionReason.ShouldBeNull();
        }
        (await BookingAsync(approved)).Status.ShouldBe(BookingStatus.Approved); // #315: onaylı randevu iptal edilmez
        (await BookingAsync(otherTeachers)).Status.ShouldBe(BookingStatus.Pending);

        var events = await EventsAsync<BookingDecisionEvent>();
        events.Select(e => e.BookingId).ShouldBe([leftA, leftB], ignoreOrder: true);
        events.ShouldAllBe(e => !e.Approved && e.TeacherUnavailable && e.RejectionReason == null && e.TeacherId == TeacherId
            && e.TeacherName == "Ayşe Öğretmen");
        events.Single(e => e.BookingId == leftA).TargetKeycloakId.ShouldBe("kc-a");
        events.Single(e => e.BookingId == leftB).TargetKeycloakId.ShouldBe("kc-b");
        (await OutboxCountAsync()).ShouldBe(2); // BookingTeacherUnavailableEvent vb. ek event yok
    }

    [Fact]
    public async Task Sweep_rejects_a_left_over_pending_request_whose_slot_was_soft_deleted()
    {
        // issue #376: SelectPending slot filtresini gevşetir; aksi halde parti bu satırı hiç görmez, talep Pending kalırdı.
        var left = await AddBookingAsync(TeacherId, StudentA, BookingStatus.Pending, 9);
        await using (var ctx = _db.NewContext())
        {
            var slotId = await ctx.Bookings.Where(b => b.Id == left).Select(b => b.AvailabilitySlotId).SingleAsync();
            await ctx.TeacherAvailabilitySlots.Where(s => s.Id == slotId)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.IsDeleted, true));
        }
        await SuspendRowAsync(TeacherId);

        (await SweepAsync()).ShouldBe(1);

        (await BookingAsync(left)).Status.ShouldBe(BookingStatus.Rejected);
        (await EventsAsync<BookingDecisionEvent>()).ShouldHaveSingleItem().BookingId.ShouldBe(left);
    }

    [Fact]
    public async Task Sweep_is_idempotent_and_a_second_run_writes_no_event()
    {
        await AddBookingAsync(TeacherId, StudentA, BookingStatus.Pending, 9);
        await SuspendRowAsync(TeacherId);

        (await SweepAsync()).ShouldBe(1);
        (await SweepAsync()).ShouldBe(0);

        (await EventsAsync<BookingDecisionEvent>()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Sweep_after_the_suspension_flow_finds_nothing_and_does_not_notify_twice()
    {
        await AddBookingAsync(TeacherId, StudentA, BookingStatus.Pending, 9);
        await using (var ctx = _db.NewContext())
            (await new AdminTeacherSuspensionService(ctx, new AdminUserActionAuditService(ctx),
                    NullLogger<AdminTeacherSuspensionService>.Instance, _authApi, _clock)
                .SuspendAsync(TeacherId, "neden", "kc-admin-331", 999)).Status.ShouldBe(AdminTeacherSuspensionStatus.Success);
        (await EventsAsync<BookingDecisionEvent>()).Count.ShouldBe(1);

        (await SweepAsync()).ShouldBe(0);

        (await EventsAsync<BookingDecisionEvent>()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Sweep_does_nothing_for_active_teachers()
    {
        await AddBookingAsync(TeacherId, StudentA, BookingStatus.Pending, 9);

        (await SweepAsync()).ShouldBe(0);

        (await OutboxCountAsync()).ShouldBe(0);
        _ = _authApi.DidNotReceiveWithAnyArgs().GetUsersByIdsAsync(default!, default);
    }

    [Fact]
    public async Task Request_decided_between_the_sweeps_read_and_write_is_skipped_without_an_event()
    {
        var decided = await AddBookingAsync(TeacherId, StudentA, BookingStatus.Pending, 9);
        var stillPending = await AddBookingAsync(TeacherId, StudentB, BookingStatus.Pending, 11);
        await SuspendRowAsync(TeacherId);

        // Süpürme Pending'leri okudu; yazma transaction'ı başlarken talep başka yoldan karara bağlanır.
        var interceptor = new OnTransactionStartedInterceptor(async transaction =>
        {
            await using var other = _db.NewContext();
            await other.Database.UseTransactionAsync(transaction);
            await other.Bookings.Where(b => b.Id == decided)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, BookingStatus.Rejected));
        });

        await using (var ctx = _db.NewContext(interceptor))
            (await NewSweep(ctx).SweepAsync()).ShouldBe(1);

        interceptor.Fired.ShouldBeTrue();
        (await EventsAsync<BookingDecisionEvent>()).Single().BookingId.ShouldBe(stillPending);
    }

    [Fact]
    public async Task Request_of_a_teacher_unsuspended_between_the_sweeps_read_and_write_stays_pending()
    {
        var pending = await AddBookingAsync(TeacherId, StudentA, BookingStatus.Pending, 9);
        await SuspendRowAsync(TeacherId);

        var interceptor = new OnTransactionStartedInterceptor(async transaction =>
        {
            await using var other = _db.NewContext();
            await other.Database.UseTransactionAsync(transaction);
            await other.Teachers.Where(t => t.Id == TeacherId).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.AccountSuspendedAt, (DateTime?)null)
                .SetProperty(t => t.AccountApprovedAt, Now.UtcDateTime));
        });

        await using (var ctx = _db.NewContext(interceptor))
            (await NewSweep(ctx).SweepAsync()).ShouldBe(0);

        (await BookingAsync(pending)).Status.ShouldBe(BookingStatus.Pending);
        (await OutboxCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Sweep_processes_at_most_one_batch_per_run_and_the_rest_on_the_next_run()
    {
        for (var hour = 8; hour < 13; hour++)
            await AddBookingAsync(TeacherId, StudentA, BookingStatus.Pending, hour);
        await SuspendRowAsync(TeacherId);

        (await SweepAsync(batchSize: 3)).ShouldBe(3);
        (await SweepAsync(batchSize: 3)).ShouldBe(2);
        (await SweepAsync(batchSize: 3)).ShouldBe(0);

        (await EventsAsync<BookingDecisionEvent>()).Select(e => e.BookingId).Distinct().Count().ShouldBe(5);
        (await BookingsOfAsync(TeacherId)).ShouldAllBe(b => b.Status == BookingStatus.Rejected);
    }

    [Fact]
    public async Task Auth_lookup_failure_still_closes_the_requests_with_empty_notification_fields()
    {
        await AddBookingAsync(TeacherId, StudentA, BookingStatus.Pending, 9);
        await SuspendRowAsync(TeacherId);
        var failing = Substitute.For<IAuthApiClient>();
        failing.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<UserLookupResultDto>>>(_ => throw new HttpRequestException("down"));

        (await SweepAsync(authApi: failing)).ShouldBe(1);

        var e = (await EventsAsync<BookingDecisionEvent>()).Single();
        e.TargetKeycloakId.ShouldBeEmpty(); // consumer kendi verisinden çözer (#298)
        e.TeacherName.ShouldBeEmpty();
        e.StudentUserId.ShouldBe(StudentAUser);
    }

    [Fact]
    public async Task Failed_commit_retry_of_the_sweep_writes_each_event_exactly_once()
    {
        await AddBookingAsync(TeacherId, StudentA, BookingStatus.Pending, 9);
        await AddBookingAsync(TeacherId, StudentB, BookingStatus.Pending, 11);
        await SuspendRowAsync(TeacherId);
        var interceptor = new FailFirstCommitInterceptor();

        await using (var ctx = _db.NewContextWithTransientRetry(interceptor))
            (await NewSweep(ctx).SweepAsync()).ShouldBe(2);

        interceptor.Failures.ShouldBe(1);
        (await EventsAsync<BookingDecisionEvent>()).Count.ShouldBe(2);
    }

    // ---------------- review düzeltmeleri ----------------

    [Fact]
    public void Lock_query_matches_the_authorization_definition_and_reuses_the_323_lock_timeout()
    {
        var sql = BookingService.BookableTeacherLockSql;
        sql.ShouldContain("FOR SHARE");
        sql.ShouldContain("\"AccountApprovedAt\" IS NOT NULL");
        sql.ShouldContain("\"AccountSuspendedAt\" IS NULL");
        sql.ShouldContain("NOT \"IsDeleted\"");
        sql.ShouldContain("\"ApprovalStatus\" = {1}");
        ExamApp.Api.Helpers.TeacherAvailabilityLock.SetLockTimeoutSql.ShouldBe("SET LOCAL lock_timeout = '5s'");
    }

    [Fact]
    public async Task Lock_timeout_maps_to_409_busy_without_a_retry_or_a_booking()
    {
        var slotId = await SeedSlotAsync(TeacherId, 10);
        var starts = 0;
        var interceptor = new OnTransactionStartedInterceptor(_ =>
        {
            starts++;
            throw new ExamApp.Api.Helpers.TeacherAvailabilityLockTimeoutException(TeacherId, new Exception("55P03"));
        }, fireAlways: true);

        BookingResultDto result;
        await using (var ctx = _db.NewContextWithTransientRetry(interceptor))
            result = await NewBookingService(ctx).CreateBookingAsync(StudentAUser, new CreateBookingDto { AvailabilitySlotId = slotId });

        starts.ShouldBe(1); // kalıcı istisna: execution strategy yeniden denemez
        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
        result.NotFound.ShouldBeFalse();
        result.Message.ShouldNotBeNullOrWhiteSpace();
        (await BookingsOfAsync(TeacherId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Teacher_without_account_approval_gets_no_request_at_the_pre_check()
    {
        var slotId = await SeedSlotAsync(TeacherId, 10);
        await using (var seed = _db.NewContext())
            await seed.Teachers.Where(t => t.Id == TeacherId).ExecuteUpdateAsync(s => s.SetProperty(t => t.AccountApprovedAt, (DateTime?)null));

        await using (var ctx = _db.NewContext())
            (await NewBookingService(ctx).CreateBookingAsync(StudentAUser, new CreateBookingDto { AvailabilitySlotId = slotId }))
                .NotFound.ShouldBeTrue();

        (await BookingsOfAsync(TeacherId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Account_approval_withdrawn_after_the_pre_read_is_caught_inside_the_transaction()
    {
        var slotId = await SeedSlotAsync(TeacherId, 10);
        var interceptor = new OnTransactionStartedInterceptor(async transaction =>
        {
            await using var other = _db.NewContext();
            await other.Database.UseTransactionAsync(transaction);
            await other.Teachers.Where(t => t.Id == TeacherId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.AccountApprovedAt, (DateTime?)null));
        });

        await using (var ctx = _db.NewContext(interceptor))
            (await NewBookingService(ctx).CreateBookingAsync(StudentAUser, new CreateBookingDto { AvailabilitySlotId = slotId }))
                .NotFound.ShouldBeTrue();

        interceptor.Fired.ShouldBeTrue();
        (await BookingsOfAsync(TeacherId)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(1000, true)]
    [InlineData(1001, false)]
    [InlineData(0, false)]
    public void Batch_size_is_capped_at_1000(int batchSize, bool valid)
    {
        var options = new SuspendedTeacherBookingSweepOptions { BatchSize = batchSize };
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        System.ComponentModel.DataAnnotations.Validator
            .TryValidateObject(options, new System.ComponentModel.DataAnnotations.ValidationContext(options), results, true)
            .ShouldBe(valid);
    }

    [Fact]
    public async Task Sweep_commits_per_teacher_in_chunks_of_at_most_100()
    {
        await using (var seed = _db.NewContext())
        {
            for (var i = 0; i < 150; i++)
                BookingSeed.Add(seed, TeacherId, StudentA, BookingStatus.Pending,
                    new TimeOnly(0, 0).AddMinutes(i * 5), new TimeOnly(0, 0).AddMinutes(i * 5 + 5), Future);
            await seed.SaveChangesAsync();
        }
        await AddBookingAsync(OtherTeacherId, StudentB, BookingStatus.Pending, 9);
        await SuspendRowAsync(TeacherId);
        await SuspendRowAsync(OtherTeacherId);
        var commits = new CommitCounter();

        await using (var ctx = _db.NewContext(commits))
            (await NewSweep(ctx).SweepAsync()).ShouldBe(151);

        commits.Count.ShouldBe(3); // öğretmen 10: 100 + 50, öğretmen 11: 1
        (await EventsAsync<BookingDecisionEvent>()).Count.ShouldBe(151);
    }

    [Fact]
    public async Task Caller_cancellation_during_the_lookup_propagates_and_writes_nothing()
    {
        await AddBookingAsync(TeacherId, StudentA, BookingStatus.Pending, 9);
        await SuspendRowAsync(TeacherId);
        using var cts = new CancellationTokenSource();
        var cancelling = Substitute.For<IAuthApiClient>();
        cancelling.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<UserLookupResultDto>>>(_ =>
            {
                cts.Cancel();
                throw new TaskCanceledException();
            });

        await using (var ctx = _db.NewContext())
            await Should.ThrowAsync<OperationCanceledException>(() => NewSweep(ctx, authApi: cancelling).SweepAsync(cts.Token));

        (await OutboxCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Http_timeout_without_caller_cancellation_is_still_best_effort()
    {
        await AddBookingAsync(TeacherId, StudentA, BookingStatus.Pending, 9);
        await SuspendRowAsync(TeacherId);
        var timingOut = Substitute.For<IAuthApiClient>();
        timingOut.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<UserLookupResultDto>>>(_ => throw new TaskCanceledException("timeout"));

        (await SweepAsync(authApi: timingOut)).ShouldBe(1);
    }

    [Fact]
    public async Task Warning_log_names_only_teachers_whose_requests_were_actually_rejected()
    {
        await AddBookingAsync(TeacherId, StudentA, BookingStatus.Pending, 9);
        var decidedElsewhere = await AddBookingAsync(OtherTeacherId, StudentB, BookingStatus.Pending, 9);
        await SuspendRowAsync(TeacherId);
        await SuspendRowAsync(OtherTeacherId);

        var interceptor = new OnTransactionStartedInterceptor(async transaction =>
        {
            await using var other = _db.NewContext();
            await other.Database.UseTransactionAsync(transaction);
            await other.Bookings.Where(b => b.Id == decidedElsewhere)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, BookingStatus.Rejected));
        });
        var logger = new CapturingLogger();

        await using (var ctx = _db.NewContext(interceptor))
            (await new SuspendedTeacherBookingSweepJob(ctx, new Monitor(new SuspendedTeacherBookingSweepOptions()), _authApi, _clock, logger)
                .SweepAsync()).ShouldBe(1);

        var warning = logger.Messages.Single();
        warning.ShouldContain($"Teacher#{TeacherId})");
        warning.ShouldNotContain(OtherTeacherId.ToString());
    }

    private sealed class CommitCounter : Microsoft.EntityFrameworkCore.Diagnostics.DbTransactionInterceptor
    {
        public int Count { get; private set; }

        public override Task TransactionCommittedAsync(System.Data.Common.DbTransaction transaction,
            Microsoft.EntityFrameworkCore.Diagnostics.TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Count++;
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger<SuspendedTeacherBookingSweepJob>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Warning)
                Messages.Add(formatter(state, exception));
        }
    }

    /// <summary>İlk transaction açıldığında bir kez (ya da <paramref name="fireAlways"/> ile her seferinde) "eşzamanlı yazıcı" çalıştırır.</summary>
    private sealed class OnTransactionStartedInterceptor(Func<System.Data.Common.DbTransaction, Task> write, bool fireAlways = false)
        : Microsoft.EntityFrameworkCore.Diagnostics.DbTransactionInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<System.Data.Common.DbTransaction> TransactionStartedAsync(
            System.Data.Common.DbConnection connection, Microsoft.EntityFrameworkCore.Diagnostics.TransactionEndEventData eventData,
            System.Data.Common.DbTransaction result, CancellationToken cancellationToken = default)
        {
            if (!Fired || fireAlways)
            {
                Fired = true;
                await write(result);
            }

            return result;
        }
    }
}
