using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Bookings;
using ExamApp.Api.Models.Dtos.Video;
using ExamApp.Api.Services.Bookings;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Video;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #439 — özel derste veli onayı: durum geçişleri (AwaitingParent → Pending / ParentRejected / Expired), çok veli yarışı
/// (ilk karar kazanır), öğretmenin AwaitingParent talebi görmemesi, muaf (veliye tabi olmayan) hesap, veli doğrudan talebi,
/// ödeme kapısı ve süre aşımı süpürmesi. SQLite (TestDb); Postgres'e özgü kilit/çeviri IntegrationTests'te.
/// </summary>
public class BookingParentApprovalTests : IDisposable
{
    private const int TeacherUserId = 43100;
    private const int StudentUserId = 43200;
    private const int OtherStudentUserId = 43201;
    private const int ParentUserId = 43300;
    private const int SecondParentUserId = 43301;
    private const int UnrelatedParentUserId = 43302;
    private const int PendingParentUserId = 43303;

    private static readonly DateTimeOffset DefaultNow = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly TestDb _db = TestDb.Create();
    private readonly FixedTimeProvider _clock = new(DefaultNow);
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public BookingParentApprovalTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<UserLookupResultDto>>(
                call.Arg<IEnumerable<int>>()
                    .Select(id => new UserLookupResultDto { Id = id, KeycloakId = $"kc-{id}", FullName = $"User {id}" })
                    .ToList()));
    }

    public void Dispose() => _db.Dispose();

    /// <summary>
    /// issue #439 review: event'lerdeki adlar ortak veli bildirimi lookup'ından gelir (kısa görünen ad "Ad S.");
    /// sahte dizindeki "User 43200" → "User 4.".
    /// </summary>
    private static string Short(int userId) => $"User {userId.ToString()[0]}.";

    private BookingService NewService(AppDbContext ctx, IBookingParentApprovalGate? gate = null)
    {
        var recurring = new RecurringAvailabilityService(ctx, _clock, NullLogger<RecurringAvailabilityService>.Instance);
        return new BookingService(ctx, _authApi, Substitute.For<IVideoSessionProvider>(), Options.Create(new VideoOptions()), _clock,
            recurring, NullLogger<BookingService>.Instance, localizer: null, parentApprovalGate: gate);
    }

    private sealed record World(int TeacherId, int StudentId, int OtherStudentId, int ParentId, int SecondParentId, int SlotId, int SlotId2);

    private async Task<World> SeedAsync(bool secondParent = true)
    {
        await using var ctx = _db.NewContext();
        var teacher = new Teacher
        {
            UserId = TeacherUserId, ApprovalStatus = TeacherApprovalStatus.Approved, IsIndependentTutor = true,
            AccountApprovedAt = Now.AddDays(-30), Bio = "t"
        };
        var student = new Student { UserId = StudentUserId, StudentNumber = "s1" };
        var other = new Student { UserId = OtherStudentUserId, StudentNumber = "s2" };
        var parent = new Parent { UserId = ParentUserId };
        var parent2 = new Parent { UserId = SecondParentUserId };
        var unrelated = new Parent { UserId = UnrelatedParentUserId };
        var pendingParent = new Parent { UserId = PendingParentUserId };
        ctx.AddRange(teacher, student, other, parent, parent2, unrelated, pendingParent);
        await ctx.SaveChangesAsync();

        ctx.ParentStudentLinks.Add(Link(parent.Id, student.Id, ParentStudentLinkStatus.Active, primary: true));
        if (secondParent)
            ctx.ParentStudentLinks.Add(Link(parent2.Id, student.Id, ParentStudentLinkStatus.Active));
        ctx.ParentStudentLinks.Add(Link(unrelated.Id, other.Id, ParentStudentLinkStatus.Active, primary: true));
        ctx.ParentStudentLinks.Add(Link(pendingParent.Id, student.Id, ParentStudentLinkStatus.Pending));

        var day = DateOnly.FromDateTime(Now).AddDays(10);
        var slot = new TeacherAvailabilitySlot
        {
            TeacherId = teacher.Id, Date = day, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(11, 0), CreatedAt = Now
        };
        var slot2 = new TeacherAvailabilitySlot
        {
            TeacherId = teacher.Id, Date = day, StartTime = new TimeOnly(14, 0), EndTime = new TimeOnly(15, 0), CreatedAt = Now
        };
        ctx.TeacherAvailabilitySlots.AddRange(slot, slot2);
        await ctx.SaveChangesAsync();
        return new World(teacher.Id, student.Id, other.Id, parent.Id, parent2.Id, slot.Id, slot2.Id);
    }

    private ParentStudentLink Link(int parentId, int studentId, ParentStudentLinkStatus status, bool primary = false) => new()
    {
        ParentId = parentId,
        StudentId = studentId,
        Status = status,
        Origin = primary ? ParentStudentLinkOrigin.ParentCreated : ParentStudentLinkOrigin.InviteCode,
        IsPrimary = primary && status == ParentStudentLinkStatus.Active,
        CreatedAt = Now.AddDays(-5),
        ActivatedAt = status == ParentStudentLinkStatus.Active ? Now.AddDays(-5) : null,
        RevokedAt = status == ParentStudentLinkStatus.Revoked ? Now.AddDays(-1) : null
    };

    private async Task<BookingResultDto> StudentRequestAsync(int slotId, bool requiresParent = true, int studentUserId = StudentUserId)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx).CreateBookingAsync(
            studentUserId, new CreateBookingDto { AvailabilitySlotId = slotId }, requiresParent);
    }

    private async Task<T> WithServiceAsync<T>(Func<BookingService, Task<T>> work, IBookingParentApprovalGate? gate = null)
    {
        await using var ctx = _db.NewContext();
        return await work(NewService(ctx, gate));
    }

    private async Task<Booking> BookingAsync(int id)
    {
        await using var ctx = _db.NewContext();
        return await ctx.Bookings.AsNoTracking().SingleAsync(b => b.Id == id);
    }

    private async Task<List<T>> EventsAsync<T>()
    {
        await using var ctx = _db.NewContext();
        var type = OutboxEventRegistry.NameFor<T>();
        return (await ctx.OutboxMessages.AsNoTracking().Where(m => m.Type == type).Select(m => m.Content).ToListAsync())
            .Select(c => JsonSerializer.Deserialize<T>(c)!)
            .ToList();
    }

    private async Task SetCreatedAtAsync(int bookingId, DateTime createdAt)
    {
        await using var ctx = _db.NewContext();
        await ctx.Bookings.Where(b => b.Id == bookingId).ExecuteUpdateAsync(s => s.SetProperty(b => b.CreatedAt, createdAt));
    }

    // ------------------------------------------------------------------
    // Öğrenci talebi
    // ------------------------------------------------------------------

    [Fact]
    public async Task Student_requiring_a_parent_creates_an_AwaitingParent_request_and_notifies_parents_not_the_teacher()
    {
        var w = await SeedAsync();

        var result = await StudentRequestAsync(w.SlotId);

        result.Success.ShouldBeTrue(result.Message);
        result.Booking!.Status.ShouldBe(nameof(BookingStatus.AwaitingParent));
        result.Booking.ParentApprovedAt.ShouldBeNull();
        result.Booking.ExpiresAt.ShouldBe(Now.AddDays(7)); // ders 10 gün sonra → 7 günlük pencere önce dolar

        (await BookingAsync(result.ObjectId)).Status.ShouldBe(BookingStatus.AwaitingParent);
        (await EventsAsync<BookingRequestCreatedEvent>()).ShouldBeEmpty("öğretmene veli onayından önce bildirim gitmez");

        var requested = (await EventsAsync<BookingParentApprovalRequestedEvent>()).ShouldHaveSingleItem();
        requested.BookingId.ShouldBe(result.ObjectId);
        requested.EventId.ShouldNotBe(Guid.Empty);
        requested.StudentId.ShouldBe(w.StudentId);
        requested.StudentName.ShouldBe(Short(StudentUserId));
        requested.TeacherName.ShouldBe(Short(TeacherUserId));
        requested.ExpiresAt.ShouldBe(Now.AddDays(7));
        // Yalnız Active veliler (Pending bağlantılı veli ve başka çocuğun velisi değil).
        requested.Parents.Select(p => p.ParentUserId).OrderBy(x => x).ShouldBe(new[] { ParentUserId, SecondParentUserId });
        requested.Parents.ShouldAllBe(p => p.KeycloakId == $"kc-{p.ParentUserId}");
    }

    [Fact]
    public async Task ExpiresAt_is_the_lesson_start_when_the_lesson_is_sooner_than_seven_days()
    {
        var w = await SeedAsync();
        int soonSlot;
        await using (var ctx = _db.NewContext())
        {
            var slot = new TeacherAvailabilitySlot
            {
                TeacherId = w.TeacherId, Date = DateOnly.FromDateTime(Now).AddDays(2), StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(10, 0), CreatedAt = Now
            };
            ctx.TeacherAvailabilitySlots.Add(slot);
            await ctx.SaveChangesAsync();
            soonSlot = slot.Id;
        }

        var result = await StudentRequestAsync(soonSlot);

        result.Booking!.ExpiresAt.ShouldBe(new DateTime(2026, 6, 17, 9, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Student_requiring_a_parent_without_an_active_parent_gets_ParentRequired_and_nothing_is_written()
    {
        var w = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            // Yalnız Pending bağlantı + koparılmış eski bağlantı kalsın.
            await ctx.ParentStudentLinks.Where(l => l.StudentId == w.StudentId && l.Status == ParentStudentLinkStatus.Active)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(l => l.Status, ParentStudentLinkStatus.Revoked)
                    .SetProperty(l => l.IsPrimary, false)
                    .SetProperty(l => l.RevokedAt, Now));
        }

        var result = await StudentRequestAsync(w.SlotId);

        result.Success.ShouldBeFalse();
        result.Forbidden.ShouldBeTrue();
        result.ErrorCode.ShouldBe(BookingErrorCodes.ParentRequired);
        await using var check = _db.NewContext();
        (await check.Bookings.CountAsync()).ShouldBe(0);
        (await check.OutboxMessages.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Exempt_account_keeps_the_old_flow_Pending_and_teacher_notification()
    {
        var w = await SeedAsync();

        var result = await StudentRequestAsync(w.SlotId, requiresParent: false);

        result.Success.ShouldBeTrue();
        result.Booking!.Status.ShouldBe(nameof(BookingStatus.Pending));
        result.Booking.ExpiresAt.ShouldBeNull();
        (await EventsAsync<BookingRequestCreatedEvent>()).ShouldHaveSingleItem().TargetKeycloakId.ShouldBe($"kc-{TeacherUserId}");
        (await EventsAsync<BookingParentApprovalRequestedEvent>()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Exempt_account_without_any_parent_can_still_book()
    {
        var w = await SeedAsync();

        // Diğer öğrencinin velisi var ama muaf hesap için veli kontrolü hiç yapılmaz; velisiz öğrenci de muafsa talep açar.
        var result = await StudentRequestAsync(w.SlotId, requiresParent: false, studentUserId: OtherStudentUserId);

        result.Success.ShouldBeTrue();
        result.Booking!.Status.ShouldBe(nameof(BookingStatus.Pending));
    }

    [Fact]
    public async Task AwaitingParent_holds_the_slot_for_other_students_and_open_slot_lists()
    {
        var w = await SeedAsync();
        (await StudentRequestAsync(w.SlotId)).Success.ShouldBeTrue();

        var second = await StudentRequestAsync(w.SlotId, requiresParent: false, studentUserId: OtherStudentUserId);
        second.Success.ShouldBeFalse();
        second.Conflict.ShouldBeTrue();

        var open = await WithServiceAsync(s => s.GetTeacherOpenSlotsAsync(w.TeacherId, 0, 50));
        open.Items.Select(i => i.Id).ShouldBe(new[] { w.SlotId2 });
    }

    // ------------------------------------------------------------------
    // Öğretmen görünürlüğü
    // ------------------------------------------------------------------

    [Fact]
    public async Task Teacher_does_not_see_AwaitingParent_ParentRejected_or_Expired_requests()
    {
        var w = await SeedAsync();
        // Aynı öğretmen için tek açık AwaitingParent (review #3): önce ikinci slottaki talep açılıp veliden reddedilir.
        var rejected = (await StudentRequestAsync(w.SlotId2)).ObjectId;
        (await WithServiceAsync(s => s.ParentRejectBookingAsync(ParentUserId, rejected, null))).Success.ShouldBeTrue();
        var awaiting = (await StudentRequestAsync(w.SlotId)).ObjectId;
        awaiting.ShouldBeGreaterThan(0);

        var list = await WithServiceAsync(s => s.GetTeacherBookingsAsync(TeacherUserId, 0, 50));
        list.Success.ShouldBeTrue();
        list.Items.ShouldBeEmpty();

        // Karar uçları da 404 (403 değil — talebin varlığı öğretmene sızmaz).
        (await WithServiceAsync(s => s.ApproveBookingAsync(TeacherUserId, awaiting))).NotFound.ShouldBeTrue();
        (await WithServiceAsync(s => s.RejectBookingAsync(TeacherUserId, rejected, "x"))).NotFound.ShouldBeTrue();
        (await BookingAsync(awaiting)).Status.ShouldBe(BookingStatus.AwaitingParent);

        // Slot listesinde slot dolu görünür ama talep kimliği / öğrenci adı yok.
        var slots = await WithServiceAsync(s => s.GetMySlotsAsync(TeacherUserId, 0, 50));
        var held = slots.Items.Single(i => i.Id == w.SlotId);
        held.IsBooked.ShouldBeTrue();
        held.BookingStatus.ShouldBe(nameof(BookingStatus.AwaitingParent));
        held.BookingId.ShouldBeNull();
        held.StudentName.ShouldBeNull();
        var freed = slots.Items.Single(i => i.Id == w.SlotId2);
        freed.IsBooked.ShouldBeFalse("veli reddi slotu serbest bırakır");

        // Görüşme odası: öğretmen için talep yok.
        var video = await WithServiceAsync(s => s.GetLiveSessionAccessAsync(TeacherUserId, awaiting));
        video.Denial.ShouldBe(BookingLiveSessionDenial.NotFound);
    }

    [Fact]
    public async Task Teacher_can_delete_a_slot_held_only_by_an_AwaitingParent_request_which_expires_with_a_notification()
    {
        var w = await SeedAsync();
        var id = (await StudentRequestAsync(w.SlotId)).ObjectId;

        var delete = await WithServiceAsync(s => s.DeleteSlotAsync(TeacherUserId, w.SlotId));

        delete.Success.ShouldBeTrue(delete.Message);
        var stored = await BookingAsync(id);
        stored.Status.ShouldBe(BookingStatus.Expired);
        stored.DecisionAt.ShouldBe(Now);
        stored.UpdateUserId.ShouldBe(TeacherUserId);
        await using (var ctx = _db.NewContext())
            (await ctx.TeacherAvailabilitySlots.IgnoreQueryFilters().SingleAsync(sl => sl.Id == w.SlotId)).IsDeleted.ShouldBeTrue();

        var notice = (await EventsAsync<BookingDecisionEvent>()).ShouldHaveSingleItem();
        notice.BookingId.ShouldBe(id);
        notice.TeacherUnavailable.ShouldBeTrue();
        notice.Approved.ShouldBeFalse();
        notice.StudentName.ShouldBe(Short(StudentUserId));
        notice.TargetKeycloakId.ShouldBe($"kc-{StudentUserId}");
        notice.Parents.Select(p => p.ParentUserId).OrderBy(x => x).ShouldBe(new[] { ParentUserId, SecondParentUserId });
        (await EventsAsync<BookingParentDecisionEvent>()).ShouldBeEmpty();
        (await WithServiceAsync(s => s.GetTeacherBookingsAsync(TeacherUserId, 0, 50))).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Teacher_still_cannot_delete_a_slot_with_a_Pending_request()
    {
        var w = await SeedAsync();
        var id = (await StudentRequestAsync(w.SlotId, requiresParent: false)).ObjectId;

        var delete = await WithServiceAsync(s => s.DeleteSlotAsync(TeacherUserId, w.SlotId));

        delete.Conflict.ShouldBeTrue();
        delete.ErrorCode.ShouldBe(BookingErrorCodes.SlotHasActiveBooking);
        (await BookingAsync(id)).Status.ShouldBe(BookingStatus.Pending);
        (await EventsAsync<BookingDecisionEvent>()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Series_delete_releases_AwaitingParent_slots_and_keeps_Pending_ones()
    {
        var w = await SeedAsync();
        int ruleId, awaitingSlot, pendingSlot;
        await using (var ctx = _db.NewContext())
        {
            var day = DateOnly.FromDateTime(Now).AddDays(20);
            var rule = new RecurringAvailabilityRule
            {
                TeacherId = w.TeacherId, DayOfWeek = day.DayOfWeek, StartTime = new TimeOnly(16, 0), EndTime = new TimeOnly(17, 0),
                EffectiveFrom = DateOnly.FromDateTime(Now), IsActive = true
            };
            ctx.RecurringAvailabilityRules.Add(rule);
            await ctx.SaveChangesAsync();
            var a = new TeacherAvailabilitySlot
            {
                TeacherId = w.TeacherId, Date = day, StartTime = new TimeOnly(16, 0), EndTime = new TimeOnly(17, 0), CreatedAt = Now,
                RecurringAvailabilityRuleId = rule.Id
            };
            var b = new TeacherAvailabilitySlot
            {
                TeacherId = w.TeacherId, Date = day.AddDays(7), StartTime = new TimeOnly(16, 0), EndTime = new TimeOnly(17, 0), CreatedAt = Now,
                RecurringAvailabilityRuleId = rule.Id
            };
            ctx.TeacherAvailabilitySlots.AddRange(a, b);
            await ctx.SaveChangesAsync();
            (ruleId, awaitingSlot, pendingSlot) = (rule.Id, a.Id, b.Id);
        }
        var awaiting = (await StudentRequestAsync(awaitingSlot)).ObjectId;
        var pending = (await StudentRequestAsync(pendingSlot, requiresParent: false, studentUserId: OtherStudentUserId)).ObjectId;

        RecurringAvailabilityRuleDeleteResultDto result;
        await using (var ctx = _db.NewContext())
        {
            var recurring = new RecurringAvailabilityService(ctx, _clock, NullLogger<RecurringAvailabilityService>.Instance,
                localizer: null, authApiClient: _authApi);
            result = await recurring.DeleteRuleAsync(TeacherUserId, ruleId);
        }

        result.Success.ShouldBeTrue(result.Message);
        result.DeletedSlotIds.ShouldContain(awaitingSlot);
        result.PreservedSlotIds.ShouldBe(new[] { pendingSlot });
        (await BookingAsync(awaiting)).Status.ShouldBe(BookingStatus.Expired);
        (await BookingAsync(pending)).Status.ShouldBe(BookingStatus.Pending);
        var notice = (await EventsAsync<BookingDecisionEvent>()).ShouldHaveSingleItem();
        notice.BookingId.ShouldBe(awaiting);
        notice.TeacherUnavailable.ShouldBeTrue();
        notice.TeacherName.ShouldBe(Short(TeacherUserId));
    }

    // ------------------------------------------------------------------
    // Veli kararı
    // ------------------------------------------------------------------

    [Fact]
    public async Task Parent_approval_moves_the_request_to_the_teacher_and_notifies_everyone()
    {
        var w = await SeedAsync();
        var id = (await StudentRequestAsync(w.SlotId)).ObjectId;
        _clock.Now = DefaultNow.AddHours(3);

        var result = await WithServiceAsync(s => s.ParentApproveBookingAsync(SecondParentUserId, id));

        result.Success.ShouldBeTrue(result.Message);
        result.Booking!.Status.ShouldBe(nameof(BookingStatus.Pending));
        result.Booking.ParentApprovedAt.ShouldBe(Now);
        var stored = await BookingAsync(id);
        stored.Status.ShouldBe(BookingStatus.Pending);
        stored.ParentApprovedAt.ShouldBe(Now);
        stored.ParentApprovedBy.ShouldBe(SecondParentUserId);
        stored.DecisionAt.ShouldBeNull("öğretmen henüz karar vermedi");

        var toTeacher = (await EventsAsync<BookingRequestCreatedEvent>()).ShouldHaveSingleItem();
        toTeacher.BookingId.ShouldBe(id);
        toTeacher.TargetKeycloakId.ShouldBe($"kc-{TeacherUserId}");
        var decision = (await EventsAsync<BookingParentDecisionEvent>()).ShouldHaveSingleItem();
        decision.Decision.ShouldBe(BookingParentDecisions.Approved);
        decision.StudentKeycloakId.ShouldBe($"kc-{StudentUserId}");
        decision.DecidedByParentUserId.ShouldBe(SecondParentUserId);
        decision.DecidedByParentName.ShouldBe(Short(SecondParentUserId));
        decision.Parents.Select(p => p.ParentUserId).OrderBy(x => x).ShouldBe(new[] { ParentUserId, SecondParentUserId });

        // Artık öğretmen akışında: liste + onay aynen çalışır; karar velilere de gider.
        (await WithServiceAsync(s => s.GetTeacherBookingsAsync(TeacherUserId, 0, 50))).Items.ShouldHaveSingleItem().Id.ShouldBe(id);
        (await WithServiceAsync(s => s.ApproveBookingAsync(TeacherUserId, id))).Success.ShouldBeTrue();
        var teacherDecision = (await EventsAsync<BookingDecisionEvent>()).ShouldHaveSingleItem();
        teacherDecision.Approved.ShouldBeTrue();
        teacherDecision.Parents.Select(p => p.ParentUserId).OrderBy(x => x).ShouldBe(new[] { ParentUserId, SecondParentUserId });
    }

    [Fact]
    public async Task First_parent_decision_wins_the_other_parent_gets_AlreadyDecided()
    {
        var w = await SeedAsync();
        var id = (await StudentRequestAsync(w.SlotId)).ObjectId;

        (await WithServiceAsync(s => s.ParentRejectBookingAsync(ParentUserId, id, "  Bu hafta sınav var  "))).Success.ShouldBeTrue();
        var late = await WithServiceAsync(s => s.ParentApproveBookingAsync(SecondParentUserId, id));
        var lateReject = await WithServiceAsync(s => s.ParentRejectBookingAsync(SecondParentUserId, id, null));

        late.Success.ShouldBeFalse();
        late.Conflict.ShouldBeTrue();
        late.ErrorCode.ShouldBe(BookingErrorCodes.AlreadyDecided);
        lateReject.ErrorCode.ShouldBe(BookingErrorCodes.AlreadyDecided);

        var stored = await BookingAsync(id);
        stored.Status.ShouldBe(BookingStatus.ParentRejected);
        stored.ParentRejectedBy.ShouldBe(ParentUserId);
        stored.RejectionReason.ShouldBe("Bu hafta sınav var");
        stored.DecisionAt.ShouldBe(Now);
        stored.ParentApprovedAt.ShouldBeNull();

        var decision = (await EventsAsync<BookingParentDecisionEvent>()).ShouldHaveSingleItem("kaybeden karar event yazmaz");
        decision.Decision.ShouldBe(BookingParentDecisions.Rejected);
        decision.Note.ShouldBe("Bu hafta sınav var");
        (await EventsAsync<BookingRequestCreatedEvent>()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(UnrelatedParentUserId)] // başka çocuğun velisi
    [InlineData(PendingParentUserId)] // Pending bağlantı
    [InlineData(99_999)] // veli kaydı yok
    public async Task Parent_without_an_active_link_to_the_student_gets_NotFound(int callerUserId)
    {
        var w = await SeedAsync();
        var id = (await StudentRequestAsync(w.SlotId)).ObjectId;

        var approve = await WithServiceAsync(s => s.ParentApproveBookingAsync(callerUserId, id));
        var reject = await WithServiceAsync(s => s.ParentRejectBookingAsync(callerUserId, id, null));

        approve.NotFound.ShouldBeTrue();
        approve.ErrorCode.ShouldBe(BookingErrorCodes.NotFound);
        reject.NotFound.ShouldBeTrue();
        (await BookingAsync(id)).Status.ShouldBe(BookingStatus.AwaitingParent);
        (await WithServiceAsync(s => s.GetParentBookingRequestsAsync(callerUserId))).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Unknown_booking_is_NotFound_for_a_parent()
    {
        await SeedAsync();
        (await WithServiceAsync(s => s.ParentApproveBookingAsync(ParentUserId, 424242))).NotFound.ShouldBeTrue();
    }

    [Fact]
    public async Task Parent_cannot_decide_a_request_that_already_reached_the_teacher()
    {
        var w = await SeedAsync();
        var id = (await StudentRequestAsync(w.SlotId, requiresParent: false)).ObjectId; // eski akış → Pending

        var approve = await WithServiceAsync(s => s.ParentApproveBookingAsync(ParentUserId, id));

        approve.Conflict.ShouldBeTrue();
        approve.ErrorCode.ShouldBe(BookingErrorCodes.AlreadyDecided);
        (await BookingAsync(id)).Status.ShouldBe(BookingStatus.Pending);
    }

    [Fact]
    public async Task Expired_request_cannot_be_decided_and_is_not_listed()
    {
        var w = await SeedAsync();
        var id = (await StudentRequestAsync(w.SlotId)).ObjectId;
        _clock.Now = DefaultNow.AddDays(7);

        var approve = await WithServiceAsync(s => s.ParentApproveBookingAsync(ParentUserId, id));

        approve.Conflict.ShouldBeTrue();
        approve.ErrorCode.ShouldBe(BookingErrorCodes.RequestExpired);
        (await BookingAsync(id)).Status.ShouldBe(BookingStatus.AwaitingParent);
        (await WithServiceAsync(s => s.GetParentBookingRequestsAsync(ParentUserId))).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Parent_list_shows_only_open_requests_of_linked_children()
    {
        var w = await SeedAsync();
        var mine = (await StudentRequestAsync(w.SlotId)).ObjectId;
        // Başka çocuğun (başka velinin) talebi + kendi çocuğunun eski akış (Pending) talebi listede olmamalı.
        await using (var ctx = _db.NewContext())
        {
            ctx.Bookings.Add(new Booking
            {
                TeacherId = w.TeacherId, StudentId = w.OtherStudentId, AvailabilitySlotId = w.SlotId2,
                Status = BookingStatus.AwaitingParent, CreatedAt = Now
            });
            await ctx.SaveChangesAsync();
        }

        var list = await WithServiceAsync(s => s.GetParentBookingRequestsAsync(ParentUserId));

        var item = list.Items.ShouldHaveSingleItem();
        item.Id.ShouldBe(mine);
        item.StudentId.ShouldBe(w.StudentId);
        item.StudentName.ShouldBe($"User {StudentUserId}");
        item.TeacherId.ShouldBe(w.TeacherId);
        item.TeacherName.ShouldBe($"User {TeacherUserId}");
        item.ExpiresAt.ShouldBe(Now.AddDays(7));
        item.StartUtc.ShouldBe(new DateTime(2026, 6, 25, 10, 0, 0, DateTimeKind.Utc));

        (await WithServiceAsync(s => s.GetParentBookingRequestsAsync(UnrelatedParentUserId))).Items.ShouldHaveSingleItem()
            .StudentId.ShouldBe(w.OtherStudentId);
    }

    [Fact]
    public async Task Approval_when_the_teacher_was_suspended_closes_the_request_as_teacher_unavailable()
    {
        var w = await SeedAsync();
        var id = (await StudentRequestAsync(w.SlotId)).ObjectId;
        await using (var ctx = _db.NewContext())
        {
            await ctx.Teachers.Where(t => t.Id == w.TeacherId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.AccountSuspendedAt, Now).SetProperty(t => t.AccountApprovedAt, (DateTime?)null));
        }

        var approve = await WithServiceAsync(s => s.ParentApproveBookingAsync(ParentUserId, id));

        approve.Conflict.ShouldBeTrue();
        approve.ErrorCode.ShouldBe(BookingErrorCodes.TeacherUnavailable);
        // issue #439 review: öğretmene hiç ulaşmamış talep Rejected değil Expired → öğretmen listesine/kararına düşmez.
        (await BookingAsync(id)).Status.ShouldBe(BookingStatus.Expired);
        (await WithServiceAsync(s => s.GetTeacherBookingsAsync(TeacherUserId, 0, 50))).Items.ShouldBeEmpty();
        (await WithServiceAsync(s => s.ApproveBookingAsync(TeacherUserId, id))).NotFound.ShouldBeTrue();
        var rejection = (await EventsAsync<BookingDecisionEvent>()).ShouldHaveSingleItem();
        rejection.TeacherUnavailable.ShouldBeTrue();
        rejection.Parents.Select(p => p.ParentUserId).OrderBy(x => x).ShouldBe(new[] { ParentUserId, SecondParentUserId });
        (await EventsAsync<BookingRequestCreatedEvent>()).ShouldBeEmpty();
        (await EventsAsync<BookingParentDecisionEvent>()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Suspended_teacher_sweep_also_closes_AwaitingParent_requests_and_notifies_parents()
    {
        var w = await SeedAsync();
        var id = (await StudentRequestAsync(w.SlotId)).ObjectId;
        await using (var ctx = _db.NewContext())
        {
            await ctx.Teachers.Where(t => t.Id == w.TeacherId).ExecuteUpdateAsync(s => s.SetProperty(t => t.AccountSuspendedAt, Now));
        }

        await using (var ctx = _db.NewContext())
        {
            var job = new SuspendedTeacherBookingSweepJob(ctx,
                new StaticOptionsMonitor<SuspendedTeacherBookingSweepOptions>(new SuspendedTeacherBookingSweepOptions()), _authApi, _clock);
            (await job.SweepAsync()).ShouldBe(1);
        }

        (await BookingAsync(id)).Status.ShouldBe(BookingStatus.Expired, "AwaitingParent kökenli talep öğretmene görünmez");
        (await WithServiceAsync(s => s.GetTeacherBookingsAsync(TeacherUserId, 0, 50))).Items.ShouldBeEmpty();
        (await WithServiceAsync(s => s.RejectBookingAsync(TeacherUserId, id, null))).NotFound.ShouldBeTrue();
        var rejection = (await EventsAsync<BookingDecisionEvent>()).ShouldHaveSingleItem();
        rejection.TeacherUnavailable.ShouldBeTrue();
        rejection.TargetKeycloakId.ShouldBe($"kc-{StudentUserId}");
        rejection.Parents.Select(p => p.KeycloakId).OrderBy(x => x).ShouldBe(new[] { $"kc-{ParentUserId}", $"kc-{SecondParentUserId}" });
    }

    // ------------------------------------------------------------------
    // Veli doğrudan talebi + ödeme kapısı
    // ------------------------------------------------------------------

    [Fact]
    public async Task Parent_direct_request_skips_AwaitingParent()
    {
        var w = await SeedAsync();

        var result = await WithServiceAsync(s => s.CreateBookingForChildAsync(
            ParentUserId, w.StudentId, new CreateBookingDto { AvailabilitySlotId = w.SlotId }));

        result.Success.ShouldBeTrue(result.Message);
        result.Booking!.Status.ShouldBe(nameof(BookingStatus.Pending));
        result.Booking.StudentId.ShouldBe(w.StudentId);
        var stored = await BookingAsync(result.ObjectId);
        stored.ParentApprovedAt.ShouldBe(Now);
        stored.ParentApprovedBy.ShouldBe(ParentUserId);
        stored.CreateUserId.ShouldBe(ParentUserId);
        (await EventsAsync<BookingRequestCreatedEvent>()).ShouldHaveSingleItem().StudentName.ShouldBe(Short(StudentUserId));
        (await EventsAsync<BookingParentApprovalRequestedEvent>()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(UnrelatedParentUserId)]
    [InlineData(PendingParentUserId)]
    public async Task Parent_direct_request_for_an_unlinked_child_is_NotFound(int callerUserId)
    {
        var w = await SeedAsync();

        var result = await WithServiceAsync(s => s.CreateBookingForChildAsync(
            callerUserId, w.StudentId, new CreateBookingDto { AvailabilitySlotId = w.SlotId }));

        result.NotFound.ShouldBeTrue();
        result.ErrorCode.ShouldBe(BookingErrorCodes.NotFound);
        await using var ctx = _db.NewContext();
        (await ctx.Bookings.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Approval_gate_runs_before_any_state_change_and_can_refuse()
    {
        var w = await SeedAsync();
        var id = (await StudentRequestAsync(w.SlotId)).ObjectId;
        var gate = Substitute.For<IBookingParentApprovalGate>();
        gate.AuthorizeAsync(Arg.Any<BookingParentApprovalContext>(), Arg.Any<CancellationToken>())
            .Returns(new BookingParentApprovalGateResult(false, "PaymentRequired", "booking.parent.approvalNotAllowed"));

        var approve = await WithServiceAsync(s => s.ParentApproveBookingAsync(ParentUserId, id), gate);
        var direct = await WithServiceAsync(s => s.CreateBookingForChildAsync(
            ParentUserId, w.StudentId, new CreateBookingDto { AvailabilitySlotId = w.SlotId2 }), gate);

        approve.Conflict.ShouldBeTrue();
        approve.ErrorCode.ShouldBe("PaymentRequired");
        direct.ErrorCode.ShouldBe("PaymentRequired");
        (await BookingAsync(id)).Status.ShouldBe(BookingStatus.AwaitingParent);
        await gate.Received(1).AuthorizeAsync(
            Arg.Is<BookingParentApprovalContext>(c => c.Kind == BookingParentApprovalKind.ApproveRequest && c.BookingId == id
                && c.ParentUserId == ParentUserId && c.StudentId == w.StudentId),
            Arg.Any<CancellationToken>());
        await gate.Received(1).AuthorizeAsync(
            Arg.Is<BookingParentApprovalContext>(c => c.Kind == BookingParentApprovalKind.DirectCreate && c.BookingId == null
                && c.AvailabilitySlotId == w.SlotId2),
            Arg.Any<CancellationToken>());
        await using var ctx = _db.NewContext();
        (await ctx.Bookings.CountAsync()).ShouldBe(1);

        // Ret ödeme kapısından geçmez.
        (await WithServiceAsync(s => s.ParentRejectBookingAsync(ParentUserId, id, null), gate)).Success.ShouldBeTrue();
    }

    // ------------------------------------------------------------------
    // Süre aşımı süpürmesi
    // ------------------------------------------------------------------

    private async Task<int> RunExpiryAsync()
    {
        await using var ctx = _db.NewContext();
        var job = new AwaitingParentBookingExpiryJob(ctx,
            new StaticOptionsMonitor<AwaitingParentBookingExpiryOptions>(new AwaitingParentBookingExpiryOptions()), _authApi, _clock);
        return await job.ExpireAsync();
    }

    [Fact]
    public async Task Expiry_job_expires_requests_after_seven_days_idempotently_and_notifies_student_and_parents()
    {
        var w = await SeedAsync();
        var old = (await StudentRequestAsync(w.SlotId)).ObjectId;
        await SetCreatedAtAsync(old, Now.AddDays(-7));
        int fresh;
        await using (var ctx = _db.NewContext())
        {
            // Aynı öğretmene ikinci açık talep servisten açılamaz (review #3); süre sınırını sınamak için doğrudan yazılır.
            var b = new Booking
            {
                TeacherId = w.TeacherId, StudentId = w.StudentId, AvailabilitySlotId = w.SlotId2,
                Status = BookingStatus.AwaitingParent, CreatedAt = Now.AddDays(-6).AddHours(-23)
            };
            ctx.Bookings.Add(b);
            await ctx.SaveChangesAsync();
            fresh = b.Id;
        }

        (await RunExpiryAsync()).ShouldBe(1);
        (await RunExpiryAsync()).ShouldBe(0, "ikinci tur aynı talebe ikinci event yazmaz");

        var expired = await BookingAsync(old);
        expired.Status.ShouldBe(BookingStatus.Expired);
        expired.DecisionAt.ShouldBe(Now);
        (await BookingAsync(fresh)).Status.ShouldBe(BookingStatus.AwaitingParent);

        var e = (await EventsAsync<BookingParentDecisionEvent>()).ShouldHaveSingleItem();
        e.BookingId.ShouldBe(old);
        e.Decision.ShouldBe(BookingParentDecisions.Expired);
        e.DecidedByParentUserId.ShouldBeNull();
        e.StudentKeycloakId.ShouldBe($"kc-{StudentUserId}");
        e.Parents.Select(p => p.ParentUserId).OrderBy(x => x).ShouldBe(new[] { ParentUserId, SecondParentUserId });

        // Slot serbest kaldı: başka öğrenci alabilir.
        (await StudentRequestAsync(w.SlotId, requiresParent: false, studentUserId: OtherStudentUserId)).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Expiry_job_expires_a_request_whose_lesson_already_started_and_leaves_other_statuses_alone()
    {
        var w = await SeedAsync();
        var awaiting = (await StudentRequestAsync(w.SlotId)).ObjectId;
        var pending = (await StudentRequestAsync(w.SlotId2, requiresParent: false)).ObjectId;
        _clock.Now = new DateTimeOffset(2026, 6, 25, 10, 0, 0, TimeSpan.Zero); // ders başlangıcı (oluşturmadan 10 gün sonra)

        (await RunExpiryAsync()).ShouldBe(1);

        (await BookingAsync(awaiting)).Status.ShouldBe(BookingStatus.Expired);
        (await BookingAsync(pending)).Status.ShouldBe(BookingStatus.Pending, "mevcut Pending/Approved talepler dokunulmaz");
    }

    [Fact]
    public async Task Expiry_job_does_not_touch_a_request_a_parent_decided_meanwhile()
    {
        var w = await SeedAsync();
        var id = (await StudentRequestAsync(w.SlotId)).ObjectId;
        (await WithServiceAsync(s => s.ParentApproveBookingAsync(ParentUserId, id))).Success.ShouldBeTrue();
        _clock.Now = DefaultNow.AddDays(8);

        (await RunExpiryAsync()).ShouldBe(0);
        (await BookingAsync(id)).Status.ShouldBe(BookingStatus.Pending);
    }

    [Fact]
    public void ExpiresAt_and_IsExpired_agree_on_the_boundary()
    {
        var created = new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);
        var date = new DateOnly(2026, 6, 20);
        var start = new TimeOnly(10, 0);

        BookingParentApproval.ExpiresAt(created, date, start).ShouldBe(created.AddDays(7));
        BookingParentApproval.IsExpired(created, date, start, created.AddDays(7).AddTicks(-1)).ShouldBeFalse();
        BookingParentApproval.IsExpired(created, date, start, created.AddDays(7)).ShouldBeTrue();
        BookingParentApproval.ExpiresAt(created, new DateOnly(2026, 6, 3), start)
            .ShouldBe(new DateTime(2026, 6, 3, 10, 0, 0, DateTimeKind.Utc));
    }

    // ------------------------------------------------------------------
    // Ek kapsam (#439 kabul kriterleri): öğretmen karar olayının çocuk adı, koparılmış veli, süre aşımı görünürlüğü,
    // mevcut Pending talebin korunması
    // ------------------------------------------------------------------

    [Fact]
    public async Task Teacher_decision_event_carries_StudentName_for_the_parents_on_approve_and_reject()
    {
        var w = await SeedAsync();
        var approvedId = (await StudentRequestAsync(w.SlotId)).ObjectId;
        (await WithServiceAsync(s => s.ParentApproveBookingAsync(ParentUserId, approvedId))).Success.ShouldBeTrue();
        (await WithServiceAsync(s => s.ApproveBookingAsync(TeacherUserId, approvedId))).Success.ShouldBeTrue();

        var rejectedId = (await StudentRequestAsync(w.SlotId2, requiresParent: false)).ObjectId;
        (await WithServiceAsync(s => s.RejectBookingAsync(TeacherUserId, rejectedId, "Müsait değilim"))).Success.ShouldBeTrue();

        var events = await EventsAsync<BookingDecisionEvent>();
        events.Count.ShouldBe(2);
        var approved = events.Single(e => e.BookingId == approvedId);
        approved.Approved.ShouldBeTrue();
        approved.StudentName.ShouldBe(Short(StudentUserId));
        approved.Parents.Select(p => p.ParentUserId).OrderBy(x => x).ShouldBe(new[] { ParentUserId, SecondParentUserId });
        var rejected = events.Single(e => e.BookingId == rejectedId);
        rejected.Approved.ShouldBeFalse();
        rejected.StudentName.ShouldBe(Short(StudentUserId));
    }

    [Fact]
    public async Task Teacher_unavailable_rejection_from_the_sweep_carries_StudentName_for_student_and_parents()
    {
        var w = await SeedAsync();
        var id = (await StudentRequestAsync(w.SlotId)).ObjectId;
        await using (var ctx = _db.NewContext())
        {
            await ctx.Teachers.Where(t => t.Id == w.TeacherId).ExecuteUpdateAsync(s => s.SetProperty(t => t.AccountSuspendedAt, Now));
        }
        await using (var ctx = _db.NewContext())
        {
            var job = new SuspendedTeacherBookingSweepJob(ctx,
                new StaticOptionsMonitor<SuspendedTeacherBookingSweepOptions>(new SuspendedTeacherBookingSweepOptions()), _authApi, _clock);
            (await job.SweepAsync()).ShouldBe(1);
        }

        (await BookingAsync(id)).Status.ShouldBe(BookingStatus.Expired);
        var rejection = (await EventsAsync<BookingDecisionEvent>()).ShouldHaveSingleItem();
        rejection.StudentName.ShouldBe($"User {StudentUserId}");
        rejection.TeacherName.ShouldBe($"User {TeacherUserId}");
    }

    [Fact]
    public async Task Parent_approval_refused_because_teacher_is_suspended_names_the_child_for_parents()
    {
        // issue #439 review (BLOCKER): ParentDecideAsync → TeacherUnavailableBookingRejection.RejectAsync studentNameOf geçirir;
        // BookingDecisionEvent.StudentName dolu → veli bildirimi çocuğun adını kullanır.
        var w = await SeedAsync();
        var id = (await StudentRequestAsync(w.SlotId)).ObjectId;
        await using (var ctx = _db.NewContext())
        {
            await ctx.Teachers.Where(t => t.Id == w.TeacherId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.AccountSuspendedAt, Now).SetProperty(t => t.AccountApprovedAt, (DateTime?)null));
        }

        var approve = await WithServiceAsync(s => s.ParentApproveBookingAsync(ParentUserId, id));

        approve.ErrorCode.ShouldBe(BookingErrorCodes.TeacherUnavailable);
        var rejection = (await EventsAsync<BookingDecisionEvent>()).ShouldHaveSingleItem();
        rejection.StudentName.ShouldBe(Short(StudentUserId));
    }

    [Fact]
    public async Task Revoked_parent_link_cannot_decide_is_dropped_from_the_list_and_from_the_decision_recipients()
    {
        var w = await SeedAsync();
        var id = (await StudentRequestAsync(w.SlotId)).ObjectId;
        await using (var ctx = _db.NewContext())
        {
            await ctx.ParentStudentLinks
                .Where(l => l.StudentId == w.StudentId && l.ParentId == w.ParentId)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.Status, ParentStudentLinkStatus.Revoked)
                    .SetProperty(l => l.IsPrimary, false)
                    .SetProperty(l => l.RevokedAt, Now));
        }

        var revokedApprove = await WithServiceAsync(s => s.ParentApproveBookingAsync(ParentUserId, id));
        var revokedReject = await WithServiceAsync(s => s.ParentRejectBookingAsync(ParentUserId, id, null));

        revokedApprove.NotFound.ShouldBeTrue();
        revokedReject.NotFound.ShouldBeTrue();
        (await WithServiceAsync(s => s.GetParentBookingRequestsAsync(ParentUserId))).Items.ShouldBeEmpty();
        (await BookingAsync(id)).Status.ShouldBe(BookingStatus.AwaitingParent);

        // Kalan Active veli karar verebilir; koparılmış veli karar olayının alıcısı değildir.
        var decided = await WithServiceAsync(s => s.ParentApproveBookingAsync(SecondParentUserId, id));
        decided.Success.ShouldBeTrue(decided.Message);
        var decision = (await EventsAsync<BookingParentDecisionEvent>()).ShouldHaveSingleItem();
        decision.Parents.Select(p => p.ParentUserId).ShouldBe(new[] { SecondParentUserId });
    }

    [Fact]
    public async Task Existing_Pending_request_stays_on_the_teacher_list_and_is_not_touched_by_the_parent_flow()
    {
        var w = await SeedAsync();
        var pending = (await StudentRequestAsync(w.SlotId, requiresParent: false)).ObjectId;

        var teacherList = await WithServiceAsync(s => s.GetTeacherBookingsAsync(TeacherUserId, 0, 50));
        teacherList.Items.ShouldHaveSingleItem().Id.ShouldBe(pending);
        (await WithServiceAsync(s => s.GetParentBookingRequestsAsync(ParentUserId))).Items.ShouldBeEmpty();

        var parentApprove = await WithServiceAsync(s => s.ParentApproveBookingAsync(ParentUserId, pending));
        parentApprove.ErrorCode.ShouldBe(BookingErrorCodes.AlreadyDecided);
        (await RunExpiryAsync()).ShouldBe(0);

        (await BookingAsync(pending)).Status.ShouldBe(BookingStatus.Pending);
        (await WithServiceAsync(s => s.ApproveBookingAsync(TeacherUserId, pending))).Success.ShouldBeTrue();
        (await BookingAsync(pending)).Status.ShouldBe(BookingStatus.Approved);
    }

    [Fact]
    public async Task Expired_request_is_invisible_to_the_teacher_and_cannot_be_approved_by_them()
    {
        var w = await SeedAsync();
        var id = (await StudentRequestAsync(w.SlotId)).ObjectId;
        _clock.Now = DefaultNow.AddDays(7);
        (await RunExpiryAsync()).ShouldBe(1);

        (await WithServiceAsync(s => s.GetTeacherBookingsAsync(TeacherUserId, 0, 50))).Items.ShouldBeEmpty();
        var approve = await WithServiceAsync(s => s.ApproveBookingAsync(TeacherUserId, id));
        approve.Success.ShouldBeFalse();
        (await BookingAsync(id)).Status.ShouldBe(BookingStatus.Expired);
    }

    [Fact]
    public async Task Payment_gate_default_is_a_noop_that_allows_both_approval_kinds()
    {
        var w = await SeedAsync();
        var noop = NoOpBookingParentApprovalGate.Instance;
        var ctx0 = new BookingParentApprovalContext(BookingParentApprovalKind.ApproveRequest, 1, w.StudentId, w.TeacherId, w.SlotId, ParentUserId);

        (await noop.AuthorizeAsync(ctx0)).Allowed.ShouldBeTrue();
        (await noop.AuthorizeAsync(ctx0 with { Kind = BookingParentApprovalKind.DirectCreate, BookingId = null })).ShouldBe(BookingParentApprovalGateResult.Allow);

        // Kapı verilmezse servis varsayılanı no-op'tur: veli onayı ve doğrudan talep ödeme kontrolü olmadan geçer.
        var id = (await StudentRequestAsync(w.SlotId)).ObjectId;
        (await WithServiceAsync(s => s.ParentApproveBookingAsync(ParentUserId, id))).Success.ShouldBeTrue();
        (await WithServiceAsync(s => s.CreateBookingForChildAsync(
            ParentUserId, w.StudentId, new CreateBookingDto { AvailabilitySlotId = w.SlotId2 }))).Success.ShouldBeTrue();
    }

    // ------------------------------------------------------------------
    // Review turu: açık AwaitingParent tavanı, not temizliği, veli yazma denetimi
    // ------------------------------------------------------------------

    private async Task<int> AddIndependentTeacherSlotAsync(int teacherUserId)
    {
        await using var ctx = _db.NewContext();
        var teacher = new Teacher
        {
            UserId = teacherUserId, ApprovalStatus = TeacherApprovalStatus.Approved, IsIndependentTutor = true,
            AccountApprovedAt = Now.AddDays(-30), Bio = "t"
        };
        ctx.Teachers.Add(teacher);
        await ctx.SaveChangesAsync();
        var slot = new TeacherAvailabilitySlot
        {
            TeacherId = teacher.Id, Date = DateOnly.FromDateTime(Now).AddDays(12), StartTime = new TimeOnly(10, 0),
            EndTime = new TimeOnly(11, 0), CreatedAt = Now
        };
        ctx.TeacherAvailabilitySlots.Add(slot);
        await ctx.SaveChangesAsync();
        return slot.Id;
    }

    [Fact]
    public async Task Only_one_open_AwaitingParent_request_per_teacher()
    {
        var w = await SeedAsync();
        var first = (await StudentRequestAsync(w.SlotId)).ObjectId;

        var second = await StudentRequestAsync(w.SlotId2);

        second.Success.ShouldBeFalse();
        second.Conflict.ShouldBeTrue();
        second.ErrorCode.ShouldBe(BookingErrorCodes.TooManyAwaitingParent);
        await using (var ctx = _db.NewContext())
            (await ctx.Bookings.CountAsync()).ShouldBe(1);

        // Veli karar verince aynı öğretmenden yeni talep açılabilir; muaf (eski akış) talep tavana tabi değil.
        (await WithServiceAsync(s => s.ParentRejectBookingAsync(ParentUserId, first, null))).Success.ShouldBeTrue();
        (await StudentRequestAsync(w.SlotId2)).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task At_most_three_open_AwaitingParent_requests_per_student()
    {
        var w = await SeedAsync();
        var slots = new List<int> { w.SlotId };
        for (var i = 1; i <= 3; i++)
            slots.Add(await AddIndependentTeacherSlotAsync(TeacherUserId + i));

        for (var i = 0; i < 3; i++)
            (await StudentRequestAsync(slots[i])).Success.ShouldBeTrue($"request {i}");
        var fourth = await StudentRequestAsync(slots[3]);

        fourth.Conflict.ShouldBeTrue();
        fourth.ErrorCode.ShouldBe(BookingErrorCodes.TooManyAwaitingParent);

        // Süresi dolmuş (süpürme henüz kapatmamış) talep tavana sayılmaz.
        await using (var ctx = _db.NewContext())
        {
            var firstId = await ctx.Bookings.Where(b => b.AvailabilitySlotId == slots[0]).Select(b => b.Id).SingleAsync();
            await ctx.Bookings.Where(b => b.Id == firstId).ExecuteUpdateAsync(x => x.SetProperty(b => b.CreatedAt, Now.AddDays(-8)));
        }
        (await StudentRequestAsync(slots[3])).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Parent_note_with_control_characters_is_rejected_and_bidi_overrides_are_stripped()
    {
        var w = await SeedAsync();
        var id = (await StudentRequestAsync(w.SlotId)).ObjectId;

        var nul = await WithServiceAsync(s => s.ParentRejectBookingAsync(ParentUserId, id, "kötü\0not"));
        nul.Success.ShouldBeFalse();
        nul.ErrorCode.ShouldBe(BookingErrorCodes.InvalidNote);
        (nul.NotFound || nul.Forbidden || nul.Conflict).ShouldBeFalse("400");
        var bell = await WithServiceAsync(s => s.ParentRejectBookingAsync(ParentUserId, id, "zil\u0007"));
        bell.ErrorCode.ShouldBe(BookingErrorCodes.InvalidNote);
        (await BookingAsync(id)).Status.ShouldBe(BookingStatus.AwaitingParent);

        var ok = await WithServiceAsync(s => s.ParentRejectBookingAsync(ParentUserId, id, "\u202Eters\u2066 metin\u2069\n2. satır\t."));
        ok.Success.ShouldBeTrue(ok.Message);
        var stored = await BookingAsync(id);
        stored.RejectionReason.ShouldBe("ters metin\n2. satır\t.");
        (await EventsAsync<BookingParentDecisionEvent>()).ShouldHaveSingleItem().Note.ShouldBe("ters metin\n2. satır\t.");
    }

    [Fact]
    public async Task Parent_booking_writes_are_recorded_in_the_parent_access_audit()
    {
        var w = await SeedAsync();
        var approved = (await StudentRequestAsync(w.SlotId)).ObjectId;
        (await WithServiceAsync(s => s.ParentApproveBookingAsync(ParentUserId, approved))).Success.ShouldBeTrue();
        var direct = (await WithServiceAsync(s => s.CreateBookingForChildAsync(
            SecondParentUserId, w.StudentId, new CreateBookingDto { AvailabilitySlotId = w.SlotId2 }))).ObjectId;
        await using (var ctx = _db.NewContext())
            await ctx.Bookings.Where(b => b.Id == direct).ExecuteUpdateAsync(x => x.SetProperty(b => b.Status, BookingStatus.Expired));
        var rejected = (await StudentRequestAsync(w.SlotId2)).ObjectId;
        (await WithServiceAsync(s => s.ParentRejectBookingAsync(ParentUserId, rejected, null))).Success.ShouldBeTrue();
        // Reddedilen (yetkisiz) istek denetime yazılmaz.
        (await WithServiceAsync(s => s.ParentApproveBookingAsync(UnrelatedParentUserId, rejected))).NotFound.ShouldBeTrue();

        await using var check = _db.NewContext();
        var audits = await check.ParentAccessAudits.AsNoTracking().OrderBy(a => a.Id).ToListAsync();
        audits.Select(a => (a.ParentId, a.StudentId, a.Endpoint, a.ResourceId)).ShouldBe(new[]
        {
            (w.ParentId, w.StudentId, ParentAccessEndpoints.ChildBookingApprove, (int?)approved),
            (w.SecondParentId, w.StudentId, ParentAccessEndpoints.ChildBookingCreate, (int?)direct),
            (w.ParentId, w.StudentId, ParentAccessEndpoints.ChildBookingReject, (int?)rejected),
        });
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
