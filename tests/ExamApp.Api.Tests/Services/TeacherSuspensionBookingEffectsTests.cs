using System.Security.Claims;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Hubs;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Models.Dtos.Bookings;
using ExamApp.Api.Models.Dtos.Teachers;
using ExamApp.Api.Models.Dtos.Video;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Services.Bookings;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.TeacherApprovals;
using ExamApp.Api.Services.Teachers;
using ExamApp.Api.Services.Video;
using ExamApp.Api.Services.Whiteboard;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #298 (+ #311 anlık kapanma): öğretmen askıya alınınca Pending talepler otomatik reddedilir (BookingDecisionEvent),
/// henüz bitmemiş Approved randevusu olan her öğrenciye TEK BookingTeacherUnavailableEvent yazılır, canlı ders oturumu
/// (görüşme + tahta) iki tarafa da kapanır, açık tahtalar hemen kapanır; askı kalkınca erişim geri gelir. Askıdaki
/// öğretmenin başvuru onayı "onaylandı" bildirimi üretmez. check-teacher DTO'su askı nedenini taşımaz.
/// </summary>
public class TeacherSuspensionBookingEffectsTests : IDisposable
{
    private const string AdminSub = "kc-admin-298";
    private const int TeacherId = 10, TeacherUserId = 100;
    private const int OtherTeacherId = 11, OtherTeacherUserId = 101;
    private const int StudentA = 20, StudentAUser = 200;
    private const int StudentB = 21, StudentBUser = 201;
    private const int StudentC = 22, StudentCUser = 202;
    private const int StudentD = 23, StudentDUser = 203;

    // "Şimdi" = 2026-06-15 12:10 UTC.
    private static readonly DateOnly Today = new(2026, 6, 15);
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 6, 15, 12, 10, 0, TimeSpan.Zero));
    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    public TeacherSuspensionBookingEffectsTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var ids = ci.Arg<IEnumerable<int>>().ToHashSet();
                var all = new List<UserLookupResultDto>
                {
                    new() { Id = TeacherUserId, FullName = "Ayşe Öğretmen", KeycloakId = "kc-t" },
                    new() { Id = StudentAUser, FullName = "A", KeycloakId = "kc-a" },
                    new() { Id = StudentBUser, FullName = "B", KeycloakId = "kc-b" },
                    new() { Id = StudentCUser, FullName = "C", KeycloakId = "kc-c" },
                };
                return (IReadOnlyList<UserLookupResultDto>)all.Where(u => ids.Contains(u.Id)).ToList();
            });
    }

    public void Dispose() => _db.Dispose();

    // ---------------- yardımcılar ----------------

    private async Task SeedPeopleAsync()
    {
        await using var ctx = _db.NewContext();
        ctx.Teachers.Add(new Teacher
        {
            Id = TeacherId, UserId = TeacherUserId, ApprovalStatus = TeacherApprovalStatus.Approved, Bio = "t",
            AccountApprovedAt = DateTime.UtcNow.AddDays(-30)
        });
        ctx.Teachers.Add(new Teacher
        {
            Id = OtherTeacherId, UserId = OtherTeacherUserId, ApprovalStatus = TeacherApprovalStatus.Approved, Bio = "t",
            AccountApprovedAt = DateTime.UtcNow.AddDays(-30)
        });
        foreach (var (id, userId) in new[] { (StudentA, StudentAUser), (StudentB, StudentBUser), (StudentC, StudentCUser), (StudentD, StudentDUser) })
            ctx.Students.Add(new Student { Id = id, UserId = userId, StudentNumber = $"S{id}" });
        await ctx.SaveChangesAsync();
    }

    private async Task<int> AddBookingAsync(int teacherId, int studentId, BookingStatus status, DateOnly date, int hour, bool deleted = false)
    {
        await using var ctx = _db.NewContext();
        var booking = BookingSeed.Add(ctx, teacherId, studentId, status, hour, date);
        await ctx.SaveChangesAsync();
        if (deleted)
        {
            ctx.Bookings.Remove(booking); // BaseEntity → soft delete
            await ctx.SaveChangesAsync();
        }
        return booking.Id;
    }

    private AdminTeacherSuspensionService NewSuspensionService(AppDbContext ctx,
        IWhiteboardStore? store = null, IWhiteboardSessionCloser? closer = null)
        => new(ctx, new AdminUserActionAuditService(ctx), NullLogger<AdminTeacherSuspensionService>.Instance, _authApi, _clock,
            store, closer);

    private const int AdminUserId = 999;

    private async Task<AdminTeacherSuspensionResult> SuspendAsync(IWhiteboardStore? store = null, IWhiteboardSessionCloser? closer = null,
        string reason = "Gizli askı nedeni")
    {
        await using var ctx = _db.NewContext();
        return await NewSuspensionService(ctx, store, closer).SuspendAsync(TeacherId, reason, AdminSub, AdminUserId);
    }

    private async Task<AdminTeacherSuspensionResult> UnsuspendAsync()
    {
        await using var ctx = _db.NewContext();
        return await NewSuspensionService(ctx).UnsuspendAsync(TeacherId, AdminSub);
    }

    private async Task<List<(string Type, string Content)>> OutboxAsync()
    {
        await using var ctx = _db.NewContext();
        return (await ctx.OutboxMessages.AsNoTracking().ToListAsync()).Select(m => (m.Type, m.Content)).ToList();
    }

    private async Task<List<T>> EventsAsync<T>()
        => (await OutboxAsync())
            .Where(m => m.Type == OutboxEventRegistry.NameFor<T>())
            .Select(m => JsonSerializer.Deserialize<T>(m.Content)!)
            .ToList();

    private async Task<Booking> BookingAsync(int id)
    {
        await using var ctx = _db.NewContext();
        return await ctx.Bookings.IgnoreQueryFilters().AsNoTracking().SingleAsync(b => b.Id == id);
    }

    // ---------------- açık talepler ----------------

    [Fact]
    public async Task Suspend_rejects_all_pending_requests_and_writes_a_teacher_unavailable_decision_event_each()
    {
        await SeedPeopleAsync();
        var futurePending = await AddBookingAsync(TeacherId, StudentA, BookingStatus.Pending, Today.AddDays(3), 9);
        var pastPending = await AddBookingAsync(TeacherId, StudentC, BookingStatus.Pending, Today.AddDays(-5), 9);
        var otherTeachersPending = await AddBookingAsync(OtherTeacherId, StudentB, BookingStatus.Pending, Today.AddDays(3), 9);

        (await SuspendAsync()).Status.ShouldBe(AdminTeacherSuspensionStatus.Success);

        foreach (var id in new[] { futurePending, pastPending })
        {
            var b = await BookingAsync(id);
            b.Status.ShouldBe(BookingStatus.Rejected);
            b.DecisionAt.ShouldBe(_clock.Now.UtcDateTime);
            b.UpdateUserId.ShouldBe(AdminUserId); // code review D5: denetim alanı admin
            b.UpdateTime.ShouldBe(_clock.Now.UtcDateTime);
            b.RejectionReason.ShouldBeNull(); // askı nedeni öğrenciye gitmez
        }
        (await BookingAsync(otherTeachersPending)).Status.ShouldBe(BookingStatus.Pending);

        var events = await EventsAsync<BookingDecisionEvent>();
        events.Select(e => e.BookingId).ShouldBe([futurePending, pastPending], ignoreOrder: true);
        events.ShouldAllBe(e => !e.Approved && e.TeacherUnavailable && e.RejectionReason == null && e.TeacherId == TeacherId);
        var forA = events.Single(e => e.BookingId == futurePending);
        forA.StudentUserId.ShouldBe(StudentAUser);
        forA.TargetKeycloakId.ShouldBe("kc-a");
        forA.TeacherName.ShouldBe("Ayşe Öğretmen");
        forA.Date.ShouldBe(Today.AddDays(3));

        (await OutboxAsync()).ShouldAllBe(m => !m.Content.Contains("Gizli askı nedeni"));
    }

    // ---------------- mevcut (onaylı) randevular ----------------

    [Fact]
    public async Task Suspend_notifies_each_student_with_upcoming_approved_bookings_once_and_keeps_the_bookings()
    {
        await SeedPeopleAsync();
        var a1 = await AddBookingAsync(TeacherId, StudentA, BookingStatus.Approved, Today.AddDays(1), 10);
        var a2 = await AddBookingAsync(TeacherId, StudentA, BookingStatus.Approved, Today.AddDays(2), 10);
        var ongoingB = await AddBookingAsync(TeacherId, StudentB, BookingStatus.Approved, Today, 12); // 12-13, şimdi 12:10
        await AddBookingAsync(TeacherId, StudentC, BookingStatus.Approved, Today, 10); // 10-11: bitti
        await AddBookingAsync(TeacherId, StudentC, BookingStatus.Approved, Today.AddDays(-1), 15); // geçmiş
        await AddBookingAsync(TeacherId, StudentD, BookingStatus.Rejected, Today.AddDays(1), 11); // reddedilmiş
        await AddBookingAsync(TeacherId, StudentD, BookingStatus.Approved, Today.AddDays(1), 12, deleted: true); // silinmiş
        await AddBookingAsync(OtherTeacherId, StudentD, BookingStatus.Approved, Today.AddDays(1), 10); // başka öğretmen

        (await SuspendAsync()).Status.ShouldBe(AdminTeacherSuspensionStatus.Success);

        var events = await EventsAsync<BookingTeacherUnavailableEvent>();
        events.Select(e => e.StudentUserId).ShouldBe([StudentAUser, StudentBUser], ignoreOrder: true);
        var forA = events.Single(e => e.StudentUserId == StudentAUser);
        forA.BookingIds.ShouldBe([a1, a2]);
        forA.TargetKeycloakId.ShouldBe("kc-a");
        forA.TeacherId.ShouldBe(TeacherId);
        forA.EventId.ShouldNotBe(Guid.Empty);
        forA.UnavailableSinceUtc.ShouldBe(_clock.Now.UtcDateTime);
        events.Single(e => e.StudentUserId == StudentBUser).BookingIds.ShouldBe([ongoingB]);
        events.Select(e => e.EventId).Distinct().Count().ShouldBe(2);

        // Randevular iptal edilmez (#315), listede kalır.
        (await BookingAsync(a1)).Status.ShouldBe(BookingStatus.Approved);
        (await BookingAsync(ongoingB)).Status.ShouldBe(BookingStatus.Approved);
        (await EventsAsync<BookingDecisionEvent>()).ShouldBeEmpty();

        // Payload'da PII (isim/e-posta) ve askı nedeni yok.
        var raw = (await OutboxAsync()).Where(m => m.Type == OutboxEventRegistry.NameFor<BookingTeacherUnavailableEvent>())
            .Select(m => m.Content).ToList();
        raw.ShouldAllBe(c => !c.Contains("Gizli askı nedeni") && !c.Contains("Ayşe") && !c.Contains("@"));
    }

    [Fact]
    public async Task Suspend_without_bookings_writes_no_events_and_skips_the_auth_lookup()
    {
        await SeedPeopleAsync();

        (await SuspendAsync()).Status.ShouldBe(AdminTeacherSuspensionStatus.Success);

        (await OutboxAsync()).ShouldBeEmpty();
        await _authApi.DidNotReceive().GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Auth_lookup_failure_still_commits_suspension_and_events_with_empty_fields()
    {
        await SeedPeopleAsync();
        var pending = await AddBookingAsync(TeacherId, StudentA, BookingStatus.Pending, Today.AddDays(3), 9);
        await AddBookingAsync(TeacherId, StudentB, BookingStatus.Approved, Today.AddDays(3), 10);
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<UserLookupResultDto>>(_ => throw new HttpRequestException("down"));

        (await SuspendAsync()).Status.ShouldBe(AdminTeacherSuspensionStatus.Success);

        (await BookingAsync(pending)).Status.ShouldBe(BookingStatus.Rejected);
        var decision = (await EventsAsync<BookingDecisionEvent>()).ShouldHaveSingleItem();
        decision.TargetKeycloakId.ShouldBeEmpty();
        decision.TeacherName.ShouldBeEmpty();
        (await EventsAsync<BookingTeacherUnavailableEvent>()).ShouldHaveSingleItem().TargetKeycloakId.ShouldBeEmpty();
    }

    [Fact]
    public async Task Conflicting_suspend_has_no_booking_side_effects()
    {
        await SeedPeopleAsync();
        await SuspendAsync();
        var pending = await AddBookingAsync(TeacherId, StudentA, BookingStatus.Pending, Today.AddDays(3), 9);
        await AddBookingAsync(TeacherId, StudentB, BookingStatus.Approved, Today.AddDays(3), 10);

        (await SuspendAsync()).Status.ShouldBe(AdminTeacherSuspensionStatus.AlreadySuspended);

        (await BookingAsync(pending)).Status.ShouldBe(BookingStatus.Pending);
        (await OutboxAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Suspend_runs_its_transaction_inside_the_execution_strategy()
    {
        await SeedPeopleAsync();
        await AddBookingAsync(TeacherId, StudentA, BookingStatus.Pending, Today.AddDays(3), 9);

        // Retrying strategy + strateji dışında açılan transaction → InvalidOperationException (Postgres/Aspire davranışı).
        await using var ctx = _db.NewContextWithRetryingExecutionStrategy();
        var result = await NewSuspensionService(ctx).SuspendAsync(TeacherId, "neden", AdminSub);

        result.Status.ShouldBe(AdminTeacherSuspensionStatus.Success);
        (await EventsAsync<BookingDecisionEvent>()).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Unsuspend_does_not_restore_rejected_requests_or_notify()
    {
        await SeedPeopleAsync();
        var pending = await AddBookingAsync(TeacherId, StudentA, BookingStatus.Pending, Today.AddDays(3), 9);
        await SuspendAsync();
        var outboxAfterSuspend = (await OutboxAsync()).Count;

        (await UnsuspendAsync()).Status.ShouldBe(AdminTeacherSuspensionStatus.Success);

        (await BookingAsync(pending)).Status.ShouldBe(BookingStatus.Rejected);
        (await OutboxAsync()).Count.ShouldBe(outboxAfterSuspend);
    }

    // ---------------- code review O1: karar / askı yarışı ----------------

    [Fact]
    public async Task Teacher_decision_before_suspension_wins_and_no_auto_rejection_is_written()
    {
        await SeedPeopleAsync();
        var bookingId = await AddBookingAsync(TeacherId, StudentA, BookingStatus.Pending, Today.AddDays(3), 9);
        await using (var ctx = _db.NewContext())
            (await NewBookingService(ctx).ApproveBookingAsync(TeacherUserId, bookingId)).Success.ShouldBeTrue();

        await SuspendAsync();

        (await BookingAsync(bookingId)).Status.ShouldBe(BookingStatus.Approved);
        var decision = (await EventsAsync<BookingDecisionEvent>()).ShouldHaveSingleItem();
        decision.Approved.ShouldBeTrue();
        decision.TeacherUnavailable.ShouldBeFalse();
        // Onaylı randevu artık "öğretmen müsait değil" bilgilendirmesine girer — çelişki değil, sonraki durum.
        (await EventsAsync<BookingTeacherUnavailableEvent>()).ShouldHaveSingleItem().BookingIds.ShouldBe([bookingId]);
    }

    [Fact]
    public async Task Suspension_after_the_teachers_read_makes_the_late_decision_fail_without_a_second_event()
    {
        await SeedPeopleAsync();
        var bookingId = await AddBookingAsync(TeacherId, StudentA, BookingStatus.Pending, Today.AddDays(3), 9);

        // DecideAsync talebi Pending okudu; auth-api lookup'ı sırasında admin öğretmeni askıya alır (otomatik ret commit).
        var racingAuthApi = Substitute.For<IAuthApiClient>();
        racingAuthApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                SuspendAsync().GetAwaiter().GetResult().Status.ShouldBe(AdminTeacherSuspensionStatus.Success);
                return (IReadOnlyList<UserLookupResultDto>)Array.Empty<UserLookupResultDto>();
            });

        BookingResultDto late;
        await using (var ctx = _db.NewContext())
            late = await NewBookingService(ctx, authApi: racingAuthApi).ApproveBookingAsync(TeacherUserId, bookingId);

        late.Success.ShouldBeFalse();
        late.NotFound.ShouldBeFalse();
        late.Message.ShouldContain("Rejected");
        (await BookingAsync(bookingId)).Status.ShouldBe(BookingStatus.Rejected);
        var decision = (await EventsAsync<BookingDecisionEvent>()).ShouldHaveSingleItem();
        decision.TeacherUnavailable.ShouldBeTrue();
        decision.Approved.ShouldBeFalse();
    }

    [Fact]
    public async Task Decision_landing_inside_the_suspension_transaction_is_not_overwritten_or_double_notified()
    {
        await SeedPeopleAsync();
        var raced = await AddBookingAsync(TeacherId, StudentA, BookingStatus.Pending, Today.AddDays(3), 9);
        var stillPending = await AddBookingAsync(TeacherId, StudentB, BookingStatus.Pending, Today.AddDays(3), 11);

        // Askı Pending listesini okuduktan sonra, ilk koşullu UPDATE'ten hemen önce öğretmenin onayı "commit" olur.
        await using (var ctx = _db.NewContext(new ApproveBeforeFirstBookingUpdate(raced)))
            (await NewSuspensionService(ctx).SuspendAsync(TeacherId, "neden", AdminSub, AdminUserId))
                .Status.ShouldBe(AdminTeacherSuspensionStatus.Success);

        (await BookingAsync(raced)).Status.ShouldBe(BookingStatus.Approved);
        (await BookingAsync(stillPending)).Status.ShouldBe(BookingStatus.Rejected);
        (await EventsAsync<BookingDecisionEvent>()).ShouldHaveSingleItem().BookingId.ShouldBe(stillPending);
        (await EventsAsync<BookingTeacherUnavailableEvent>()).ShouldHaveSingleItem().BookingIds.ShouldBe([raced]);
    }

    /// <summary>İlk <c>UPDATE "Bookings"</c> komutundan önce, aynı transaction'da verilen booking'i Approved yapar.</summary>
    private sealed class ApproveBeforeFirstBookingUpdate(int bookingId) : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        private bool _done;

        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> NonQueryExecutingAsync(
            System.Data.Common.DbCommand command, Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_done && command.CommandText.Contains("UPDATE \"Bookings\"", StringComparison.Ordinal))
            {
                _done = true;
                await using var side = command.Connection!.CreateCommand();
                side.Transaction = command.Transaction;
                side.CommandText = $"UPDATE \"Bookings\" SET \"Status\" = {(int)BookingStatus.Approved} WHERE \"Id\" = {bookingId}";
                await side.ExecuteNonQueryAsync(cancellationToken);
            }

            return result;
        }
    }

    // ---------------- code review D3: retry ----------------

    [Fact]
    public async Task Lost_commit_acknowledgement_retry_reports_success_with_a_single_set_of_side_effects()
    {
        await SeedPeopleAsync();
        await AddBookingAsync(TeacherId, StudentA, BookingStatus.Pending, Today.AddDays(3), 9);
        await AddBookingAsync(TeacherId, StudentB, BookingStatus.Approved, Today.AddDays(3), 10);
        var interceptor = new FailAfterFirstCommitInterceptor();

        AdminTeacherSuspensionResult result;
        await using (var ctx = _db.NewContextWithTransientRetry(interceptor))
            result = await NewSuspensionService(ctx).SuspendAsync(TeacherId, "neden", AdminSub, AdminUserId);

        interceptor.Failures.ShouldBe(1);
        result.Status.ShouldBe(AdminTeacherSuspensionStatus.Success);
        (await EventsAsync<BookingDecisionEvent>()).ShouldHaveSingleItem();
        (await EventsAsync<BookingTeacherUnavailableEvent>()).ShouldHaveSingleItem();
        await using var check = _db.NewContext();
        (await check.AdminUserActionLogs.SingleAsync()).Outcome.ShouldBe(AdminUserActionOutcome.Succeeded);
    }

    [Fact]
    public async Task Failed_commit_retry_rewrites_everything_exactly_once()
    {
        await SeedPeopleAsync();
        await AddBookingAsync(TeacherId, StudentA, BookingStatus.Pending, Today.AddDays(3), 9);
        await AddBookingAsync(TeacherId, StudentB, BookingStatus.Approved, Today.AddDays(3), 10);
        var interceptor = new FailFirstCommitInterceptor();

        AdminTeacherSuspensionResult result;
        await using (var ctx = _db.NewContextWithTransientRetry(interceptor))
            result = await NewSuspensionService(ctx).SuspendAsync(TeacherId, "neden", AdminSub, AdminUserId);

        interceptor.Failures.ShouldBe(1);
        result.Status.ShouldBe(AdminTeacherSuspensionStatus.Success);
        (await EventsAsync<BookingDecisionEvent>()).ShouldHaveSingleItem();
        (await EventsAsync<BookingTeacherUnavailableEvent>()).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Another_admins_suspension_is_still_a_conflict()
    {
        await SeedPeopleAsync();
        await using var ctx = _db.NewContext();
        var audit = Substitute.For<IAdminUserActionAuditService>();
        audit.RecordAsync(Arg.Any<AdminUserActionRecord>(), AdminUserActionOutcome.Requested, Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await using var other = _db.NewContext();
                await other.Teachers.Where(t => t.Id == TeacherId).ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.AccountApprovedAt, (DateTime?)null)
                    .SetProperty(t => t.AccountSuspendedAt, DateTime.UtcNow.AddMinutes(-1))
                    .SetProperty(t => t.AccountSuspensionReason, "neden")); // aynı neden, farklı an
                return 3L;
            });

        var result = await new AdminTeacherSuspensionService(ctx, audit, null, _authApi, _clock)
            .SuspendAsync(TeacherId, "neden", AdminSub, AdminUserId);

        result.Status.ShouldBe(AdminTeacherSuspensionStatus.Conflict);
        await audit.Received(1).TryUpdateOutcomeAsync(3L, AdminUserActionOutcome.Conflict);
    }

    /// <summary>İlk COMMIT veritabanına ULAŞTIKTAN sonra geçici hata fırlatır (onay kaybı) — strateji yeniden dener.</summary>
    private sealed class FailAfterFirstCommitInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.DbTransactionInterceptor
    {
        public int Failures { get; private set; }

        public override Task TransactionCommittedAsync(System.Data.Common.DbTransaction transaction,
            Microsoft.EntityFrameworkCore.Diagnostics.TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (Failures == 0)
            {
                Failures++;
                throw new TransientTestException("simulated lost commit acknowledgement");
            }

            return Task.CompletedTask;
        }
    }

    // ---------------- canlı ders oturumu (görüşme + tahta) ----------------

    private BookingService NewBookingService(AppDbContext ctx, IVideoSessionProvider? provider = null, IAuthApiClient? authApi = null)
        => new(ctx, authApi ?? _authApi, provider ?? Substitute.For<IVideoSessionProvider>(), Options.Create(new VideoOptions()), _clock,
            new RecurringAvailabilityService(ctx, _clock, NullLogger<RecurringAvailabilityService>.Instance),
            NullLogger<BookingService>.Instance, new ExamApp.Api.Services.Tenancy.SchoolAccessPolicy(ctx));

    private async Task<VideoSessionResultDto> VideoAsync(int callerUserId, int bookingId)
    {
        var provider = Substitute.For<IVideoSessionProvider>();
        provider.CreateOrJoinSessionAsync(Arg.Any<VideoSessionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new VideoSessionDto { Provider = "Jitsi", RoomName = "r", Token = "x" });
        await using var ctx = _db.NewContext();
        return await NewBookingService(ctx, provider).GetVideoSessionAsync(callerUserId, bookingId);
    }

    private async Task<WhiteboardAccessResult> WhiteboardAsync(int userId, string role, int bookingId)
    {
        var sub = $"kc-{userId}";
        var profiles = Substitute.For<IUserProfileProvider>();
        profiles.GetAsync(sub, Arg.Any<CancellationToken>()).Returns(new UserProfileDto { Id = userId, KeycloakId = sub });
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, sub), new Claim(ClaimTypes.Role, role)], "test"));
        await using var ctx = _db.NewContext();
        return await new WhiteboardAccessService(NewBookingService(ctx), new ApprovedTeacherGuard(ctx), profiles, ctx)
            .AuthorizeAsync(user, bookingId);
    }

    [Fact]
    public async Task Suspended_teacher_closes_the_video_session_for_both_sides_and_unsuspend_reopens_it()
    {
        await SeedPeopleAsync();
        var bookingId = await AddBookingAsync(TeacherId, StudentA, BookingStatus.Approved, Today, 12); // pencere açık

        (await VideoAsync(StudentAUser, bookingId)).Success.ShouldBeTrue();

        await SuspendAsync();

        foreach (var caller in new[] { TeacherUserId, StudentAUser })
        {
            var denied = await VideoAsync(caller, bookingId);
            denied.Success.ShouldBeFalse();
            denied.Conflict.ShouldBeTrue();
            denied.ErrorCode.ShouldBe(TeacherAccessErrorCodes.TeacherUnavailable);
            denied.Session.ShouldBeNull();
            denied.Message.ShouldNotContain("Gizli");
        }

        await UnsuspendAsync();

        var teacher = await VideoAsync(TeacherUserId, bookingId);
        teacher.Success.ShouldBeTrue();
        teacher.ErrorCode.ShouldBeNull();
        (await VideoAsync(StudentAUser, bookingId)).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Teacher_unavailable_is_checked_after_booking_status_and_before_the_window()
    {
        await SeedPeopleAsync();
        var pending = await AddBookingAsync(TeacherId, StudentA, BookingStatus.Approved, Today.AddDays(2), 9);
        await SuspendAsync(); // pending değil ama pencere henüz açılmadı

        await using var ctx = _db.NewContext();
        var service = NewBookingService(ctx);
        (await service.GetLiveSessionAccessAsync(StudentAUser, pending)).Denial.ShouldBe(BookingLiveSessionDenial.TeacherUnavailable);
        (await service.GetLiveSessionAccessAsync(StudentBUser, pending)).Denial.ShouldBe(BookingLiveSessionDenial.NotParticipant);
        (await service.GetLiveSessionAccessAsync(StudentAUser, 9999)).Denial.ShouldBe(BookingLiveSessionDenial.NotFound);

        var rejected = await AddBookingAsync(TeacherId, StudentB, BookingStatus.Rejected, Today.AddDays(2), 11);
        (await service.GetLiveSessionAccessAsync(StudentBUser, rejected)).Denial.ShouldBe(BookingLiveSessionDenial.NotApproved);
    }

    [Fact]
    public async Task Suspended_teacher_blocks_the_whiteboard_for_the_student_too_and_unsuspend_reopens_it()
    {
        await SeedPeopleAsync();
        var bookingId = await AddBookingAsync(TeacherId, StudentA, BookingStatus.Approved, Today, 12);
        (await WhiteboardAsync(StudentAUser, "Student", bookingId)).Allowed.ShouldBeTrue();

        await SuspendAsync();

        (await WhiteboardAsync(StudentAUser, "Student", bookingId)).ErrorCode.ShouldBe(WhiteboardErrorCodes.TeacherUnavailable);
        (await WhiteboardAsync(TeacherUserId, "Teacher", bookingId)).ErrorCode.ShouldBe(WhiteboardErrorCodes.TeacherUnavailable);

        await UnsuspendAsync();

        (await WhiteboardAsync(StudentAUser, "Student", bookingId)).Allowed.ShouldBeTrue();
        (await WhiteboardAsync(TeacherUserId, "Teacher", bookingId)).Allowed.ShouldBeTrue();
    }

    // ---------------- #311: tahtanın anlık kapanması ----------------

    [Fact]
    public async Task Suspend_closes_the_teachers_open_whiteboards_immediately_and_leaves_others_open()
    {
        await SeedPeopleAsync();
        var mine = await AddBookingAsync(TeacherId, StudentA, BookingStatus.Approved, Today, 12);
        var others = await AddBookingAsync(OtherTeacherId, StudentB, BookingStatus.Approved, Today, 12);

        var store = new WhiteboardStore(Whiteboard.WhiteboardTestSupport.Options());
        var hub = Substitute.For<IHubContext<WhiteboardHub, IWhiteboardClient>>();
        var groupClients = new Dictionary<string, IWhiteboardClient>();
        hub.Clients.Group(Arg.Any<string>()).Returns(ci =>
        {
            var name = ci.Arg<string>();
            if (!groupClients.TryGetValue(name, out var client))
                groupClients[name] = client = Substitute.For<IWhiteboardClient>();
            return client;
        });
        var groups = Substitute.For<IGroupManager>();
        hub.Groups.Returns(groups);
        var closer = new WhiteboardSessionCloser(store, hub, NullLogger<WhiteboardSessionCloser>.Instance);

        var closesAt = _clock.Now.UtcDateTime.AddHours(1);
        store.Join("conn-t", Whiteboard.WhiteboardTestSupport.Access(mine, TeacherUserId, WhiteboardRoles.Teacher, closesAt),
            Whiteboard.WhiteboardTestSupport.TestUser, _clock.Now.UtcDateTime);
        store.Join("conn-s", Whiteboard.WhiteboardTestSupport.Access(mine, StudentAUser, WhiteboardRoles.Student, closesAt),
            Whiteboard.WhiteboardTestSupport.TestUser, _clock.Now.UtcDateTime);
        store.Join("conn-o", Whiteboard.WhiteboardTestSupport.Access(others, OtherTeacherUserId, WhiteboardRoles.Teacher, closesAt),
            Whiteboard.WhiteboardTestSupport.TestUser, _clock.Now.UtcDateTime);

        (await SuspendAsync(store, closer)).Status.ShouldBe(AdminTeacherSuspensionStatus.Success);

        store.IsOpen(mine).ShouldBeFalse();
        await groupClients[WhiteboardHub.GroupName(mine)].Received(1).BoardClosed(WhiteboardCloseReasons.TeacherUnavailable);
        await groups.Received().RemoveFromGroupAsync("conn-t", WhiteboardHub.GroupName(mine), Arg.Any<CancellationToken>());
        await groups.Received().RemoveFromGroupAsync("conn-s", WhiteboardHub.GroupName(mine), Arg.Any<CancellationToken>());
        store.IsOpen(others).ShouldBeTrue();
        groupClients.ContainsKey(WhiteboardHub.GroupName(others)).ShouldBeFalse();
    }

    [Fact]
    public async Task Whiteboard_close_failure_does_not_undo_the_suspension()
    {
        await SeedPeopleAsync();
        var mine = await AddBookingAsync(TeacherId, StudentA, BookingStatus.Approved, Today, 12);
        var pending = await AddBookingAsync(TeacherId, StudentB, BookingStatus.Pending, Today.AddDays(1), 9);
        var store = Substitute.For<IWhiteboardStore>();
        store.ListBoards().Returns([new WhiteboardBoardInfo(mine, _clock.Now.UtcDateTime.AddHours(1))]);
        var closer = Substitute.For<IWhiteboardSessionCloser>();
        closer.CloseAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<bool>(_ => throw new InvalidOperationException("hub down"));

        var result = await SuspendAsync(store, closer);

        result.Status.ShouldBe(AdminTeacherSuspensionStatus.Success);
        await closer.Received(1).CloseAsync(mine, WhiteboardCloseReasons.TeacherUnavailable, Arg.Any<CancellationToken>());
        await using var ctx = _db.NewContext();
        (await ctx.Teachers.SingleAsync(t => t.Id == TeacherId)).AccountSuspendedAt.ShouldNotBeNull();
        (await BookingAsync(pending)).Status.ShouldBe(BookingStatus.Rejected);
        (await ctx.AdminUserActionLogs.SingleAsync()).Outcome.ShouldBe(AdminUserActionOutcome.Succeeded);
    }

    // ---------------- askıda "onaylandı" bildirimi ----------------

    private async Task<ResponseBaseDto> ApproveApplicationAsync(int teacherId)
    {
        await using var ctx = _db.NewContext();
        return await new TeacherApprovalService(ctx, _authApi, auditService: new AdminUserActionAuditService(ctx))
            .ApproveAsync(teacherId, 999, AdminSub);
    }

    [Fact]
    public async Task Approving_a_suspended_teachers_application_writes_no_approved_notification_but_is_audited()
    {
        await SeedPeopleAsync();
        await SuspendAsync();
        await using (var ctx = _db.NewContext())
        {
            await ctx.Teachers.Where(t => t.Id == TeacherId).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.IsIndependentTutor, true)
                .SetProperty(t => t.ApprovalStatus, TeacherApprovalStatus.Pending));
        }

        (await ApproveApplicationAsync(TeacherId)).Success.ShouldBeTrue();

        (await EventsAsync<TeacherApplicationDecidedEvent>()).ShouldBeEmpty();
        await using var check = _db.NewContext();
        (await check.AdminUserActionLogs.AsNoTracking().ToListAsync())
            .ShouldContain(l => l.Action == AdminUserAction.TeacherApproved && l.TargetId == TeacherId
                && l.Outcome == AdminUserActionOutcome.Succeeded);
    }

    [Fact]
    public async Task Approving_a_non_suspended_teachers_application_still_writes_the_approved_notification()
    {
        await SeedPeopleAsync();
        await using (var ctx = _db.NewContext())
        {
            await ctx.Teachers.Where(t => t.Id == TeacherId).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.IsIndependentTutor, true)
                .SetProperty(t => t.ApprovalStatus, TeacherApprovalStatus.Pending));
        }

        (await ApproveApplicationAsync(TeacherId)).Success.ShouldBeTrue();

        var e = (await EventsAsync<TeacherApplicationDecidedEvent>()).ShouldHaveSingleItem();
        e.Approved.ShouldBeTrue();
        e.TargetKeycloakId.ShouldBe("kc-t");
    }

    // ---------------- check-teacher DTO ----------------

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Check_teacher_dto_carries_only_the_allowed_fields_and_never_the_suspension_reason()
    {
        var teacher = new Teacher
        {
            Id = 5, UserId = 50, SchoolName = "Okul", SchoolId = 7, IsIndependentTutor = true,
            ThemePreset = "minimal", ThemeCustomConfig = "{}",
            ApprovalStatus = TeacherApprovalStatus.Approved, AccountApprovedAt = null,
            AccountSuspendedAt = DateTime.UtcNow, AccountSuspensionReason = "Gizli askı nedeni",
            Bio = "bio", HourlyRate = 100, IsSeedData = true, RequestedSchoolId = 9
        };

        // Controller Ok(object) → çalışma zamanı tipiyle serileşir.
        var json = JsonSerializer.Serialize<object>(CheckTeacherRecordResponseDto.From(teacher), Web);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.EnumerateObject().Select(p => p.Name).ShouldBe(
            ["teacher", "teacherAccountApproved", "teacherAccountSuspended", "teacherApplicationStatus", "rejectionReason", "hasTeacherRecord"],
            ignoreOrder: true);
        root.GetProperty("hasTeacherRecord").GetBoolean().ShouldBeTrue();
        root.GetProperty("teacherAccountApproved").GetBoolean().ShouldBeFalse();
        root.GetProperty("teacherAccountSuspended").GetBoolean().ShouldBeTrue();
        root.GetProperty("teacherApplicationStatus").GetString().ShouldBe("Approved");
        root.GetProperty("rejectionReason").ValueKind.ShouldBe(JsonValueKind.Null);

        var t = root.GetProperty("teacher");
        t.EnumerateObject().Select(p => p.Name).ShouldBe(
            ["id", "userId", "schoolName", "schoolId", "isIndependentTutor", "themePreset", "themeCustomConfig"],
            ignoreOrder: true);
        t.GetProperty("id").GetInt32().ShouldBe(5);
        t.GetProperty("userId").GetInt32().ShouldBe(50);
        t.GetProperty("schoolId").GetInt32().ShouldBe(7);

        json.ShouldNotContain("Gizli");
        json.ShouldNotContain("accountSuspensionReason", Case.Insensitive);
        json.ShouldNotContain("accountSuspendedAt", Case.Insensitive);
    }

    [Fact]
    public void Check_teacher_dto_without_record_keeps_the_old_shape()
        => JsonSerializer.Serialize<object>(CheckTeacherResponseDto.NoRecord(), Web).ShouldBe("{\"hasTeacherRecord\":false}");
}
