using System.Data.Common;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Bookings;
using ExamApp.Api.Models.Dtos.Video;
using ExamApp.Api.Services.Bookings;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Video;
using ExamApp.Api.Services.Whiteboard;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #315 — randevu iptali, erteleme (öneri / kabul / ret / geri çekme) ve no-show. Kurallar
/// <c>BookingService.Lifecycle.cs</c> ve <see cref="BookingPolicyOptions"/> varsayılanlarından (iptal 2 saat, erteleme 2 saat,
/// en fazla 2 erteleme, no-show penceresi 7 gün) gelir. Zaman <see cref="FixedTimeProvider"/> ile sabitlenir.
/// SQLite (TestDb); Postgres'e özgü ExecuteUpdate çevirisi ve gerçek transaction davranışı IntegrationTests'te.
/// </summary>
public class BookingLifecycleTests : IDisposable
{
    private const int TeacherUserId = 43100;
    private const int OtherTeacherUserId = 43150;
    private const int StudentUserId = 43200;
    private const int OtherStudentUserId = 43201;
    private const int ParentUserId = 43300;
    private const int SecondParentUserId = 43301;
    private const int UnrelatedParentUserId = 43302;

    private static readonly DateTimeOffset DefaultNow = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Varsayılan ders günü: şimdiden 10 gün sonra (2026-06-25), ders 10:00-11:00 (UTC duvar saati).</summary>
    private static readonly DateTime LessonStartUtc = new(2026, 6, 25, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LessonEndUtc = new(2026, 6, 25, 11, 0, 0, DateTimeKind.Utc);

    private readonly TestDb _db = TestDb.Create();
    private readonly FixedTimeProvider _clock = new(DefaultNow);
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public BookingLifecycleTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<UserLookupResultDto>>(
                call.Arg<IEnumerable<int>>()
                    .Select(id => new UserLookupResultDto { Id = id, KeycloakId = $"kc-{id}", FullName = $"User {id}" })
                    .ToList()));
    }

    public void Dispose() => _db.Dispose();

    /// <summary>Olay metinlerindeki kısa görünen ad ("Ad S."): sahte dizinde "User 43200" → "User 4.".</summary>
    private static string Short(int userId) => $"User {userId.ToString()[0]}.";

    private BookingService NewService(AppDbContext ctx, IWhiteboardSessionCloser? closer = null)
    {
        var recurring = new RecurringAvailabilityService(ctx, _clock, NullLogger<RecurringAvailabilityService>.Instance);
        return new BookingService(ctx, _authApi, Substitute.For<IVideoSessionProvider>(), Options.Create(new VideoOptions()), _clock,
            recurring, NullLogger<BookingService>.Instance, localizer: null, parentApprovalGate: null,
            policyOptions: Options.Create(new BookingPolicyOptions()), whiteboardCloser: closer);
    }

    private sealed record World(
        int TeacherId, int OtherTeacherId, int StudentId, int OtherStudentId,
        int SlotId, int SlotId2, int SlotId3, int SlotId4, int SlotId5, int OtherTeacherSlotId, int PastSlotId);

    private async Task<World> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var teacher = new Teacher
        {
            UserId = TeacherUserId, ApprovalStatus = TeacherApprovalStatus.Approved, IsIndependentTutor = true,
            AccountApprovedAt = Now.AddDays(-30), Bio = "t"
        };
        var otherTeacher = new Teacher
        {
            UserId = OtherTeacherUserId, ApprovalStatus = TeacherApprovalStatus.Approved, IsIndependentTutor = true,
            AccountApprovedAt = Now.AddDays(-30), Bio = "t2"
        };
        var student = new Student { UserId = StudentUserId, StudentNumber = "s1" };
        var other = new Student { UserId = OtherStudentUserId, StudentNumber = "s2" };
        var parent = new Parent { UserId = ParentUserId };
        var parent2 = new Parent { UserId = SecondParentUserId };
        var unrelated = new Parent { UserId = UnrelatedParentUserId };
        ctx.AddRange(teacher, otherTeacher, student, other, parent, parent2, unrelated);
        await ctx.SaveChangesAsync();

        ctx.ParentStudentLinks.Add(Link(parent.Id, student.Id, ParentStudentLinkStatus.Active, primary: true));
        ctx.ParentStudentLinks.Add(Link(parent2.Id, student.Id, ParentStudentLinkStatus.Active));
        ctx.ParentStudentLinks.Add(Link(unrelated.Id, other.Id, ParentStudentLinkStatus.Active, primary: true));

        var day = DateOnly.FromDateTime(Now).AddDays(10);
        var slot1 = Slot(teacher.Id, day, 10);
        var slot2 = Slot(teacher.Id, day, 14);
        var slot3 = Slot(teacher.Id, day.AddDays(1), 10);
        var slot4 = Slot(teacher.Id, day.AddDays(2), 10);
        var slot5 = Slot(teacher.Id, day.AddDays(3), 10);
        var otherTeacherSlot = Slot(otherTeacher.Id, day, 10);
        var pastSlot = Slot(teacher.Id, day.AddDays(-11), 10);
        ctx.TeacherAvailabilitySlots.AddRange(slot1, slot2, slot3, slot4, slot5, otherTeacherSlot, pastSlot);
        await ctx.SaveChangesAsync();

        return new World(teacher.Id, otherTeacher.Id, student.Id, other.Id,
            slot1.Id, slot2.Id, slot3.Id, slot4.Id, slot5.Id, otherTeacherSlot.Id, pastSlot.Id);
    }

    private TeacherAvailabilitySlot Slot(int teacherId, DateOnly date, int startHour) => new()
    {
        TeacherId = teacherId, Date = date, StartTime = new TimeOnly(startHour, 0), EndTime = new TimeOnly(startHour + 1, 0),
        CreatedAt = Now
    };

    private ParentStudentLink Link(int parentId, int studentId, ParentStudentLinkStatus status, bool primary = false) => new()
    {
        ParentId = parentId,
        StudentId = studentId,
        Status = status,
        Origin = primary ? ParentStudentLinkOrigin.ParentCreated : ParentStudentLinkOrigin.InviteCode,
        IsPrimary = primary && status == ParentStudentLinkStatus.Active,
        CreatedAt = Now.AddDays(-5),
        ActivatedAt = status == ParentStudentLinkStatus.Active ? Now.AddDays(-5) : null,
    };

    private async Task<BookingResultDto> StudentRequestAsync(int slotId, bool requiresParent = false, int studentUserId = StudentUserId)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx).CreateBookingAsync(
            studentUserId, new CreateBookingDto { AvailabilitySlotId = slotId }, requiresParent);
    }

    /// <summary>Pending talep (veli gerektirmeyen hesap) → öğretmen onayı → Approved randevu.</summary>
    private async Task<int> ApprovedAsync(int slotId, int studentUserId = StudentUserId)
    {
        var id = (await StudentRequestAsync(slotId, studentUserId: studentUserId)).ObjectId;
        (await WithServiceAsync(s => s.ApproveBookingAsync(TeacherUserId, id))).Success.ShouldBeTrue();
        return id;
    }

    private async Task<T> WithServiceAsync<T>(Func<BookingService, Task<T>> work, IWhiteboardSessionCloser? closer = null)
    {
        await using var ctx = _db.NewContext();
        return await work(NewService(ctx, closer));
    }

    private async Task<Booking> BookingAsync(int id)
    {
        await using var ctx = _db.NewContext();
        return await ctx.Bookings.AsNoTracking().SingleAsync(b => b.Id == id);
    }

    private async Task<List<BookingLifecycleEvent>> LifecycleEventsAsync()
    {
        await using var ctx = _db.NewContext();
        var type = OutboxEventRegistry.NameFor<BookingLifecycleEvent>();
        return (await ctx.OutboxMessages.AsNoTracking().Where(m => m.Type == type).Select(m => m.Content).ToListAsync())
            .Select(c => JsonSerializer.Deserialize<BookingLifecycleEvent>(c)!)
            .ToList();
    }

    /// <summary>Öğrenci önerir, öğretmen kabul eder (erteleme sayacını bir artırır).</summary>
    private async Task ProposeAndAcceptByTeacherAsync(int bookingId, int slotId)
    {
        (await WithServiceAsync(s => s.ProposeRescheduleAsync(StudentUserId, bookingId, slotId))).Success.ShouldBeTrue();
        (await WithServiceAsync(s => s.RespondRescheduleAsync(TeacherUserId, bookingId, true))).Success.ShouldBeTrue();
    }

    // ------------------------------------------------------------------
    // İptal
    // ------------------------------------------------------------------

    [Fact]
    public async Task CancelBookingAsync_StudentWithdrawsPending_NotifiesTeacherAndStoresCancellation()
    {
        var w = await SeedAsync();
        var id = (await StudentRequestAsync(w.SlotId)).ObjectId;

        var result = await WithServiceAsync(s => s.CancelBookingAsync(StudentUserId, id, "Sınavım var"));

        result.Success.ShouldBeTrue(result.Message);
        result.Booking!.Status.ShouldBe(nameof(BookingStatus.Cancelled));
        var stored = await BookingAsync(id);
        stored.Status.ShouldBe(BookingStatus.Cancelled);
        stored.CancelledFromStatus.ShouldBe(BookingStatus.Pending);
        stored.CancelledByRole.ShouldBe(BookingParty.Student);
        stored.CancelledByUserId.ShouldBe(StudentUserId);
        stored.CancelledAt.ShouldBe(Now);
        stored.CancellationReason.ShouldBe("Sınavım var");

        var e = (await LifecycleEventsAsync()).ShouldHaveSingleItem();
        e.BookingId.ShouldBe(id);
        e.Kind.ShouldBe(BookingLifecycleKinds.Cancelled);
        e.ActorRole.ShouldBe(BookingParty.Student);
        e.NotifyTeacher.ShouldBeTrue();
        e.NotifyStudent.ShouldBeFalse();
        e.Reason.ShouldBe("Sınavım var");
        e.TeacherKeycloakId.ShouldBe($"kc-{TeacherUserId}");
        e.StudentKeycloakId.ShouldBe($"kc-{StudentUserId}");
        e.TeacherName.ShouldBe(Short(TeacherUserId));
        e.StudentName.ShouldBe(Short(StudentUserId));
        e.Date.ShouldBe(DateOnly.FromDateTime(Now).AddDays(10));
        e.StartTime.ShouldBe(new TimeOnly(10, 0));
        e.EndTime.ShouldBe(new TimeOnly(11, 0));
    }

    [Fact]
    public async Task CancelBookingAsync_StudentWithdrawsAwaitingParent_DoesNotNotifyTeacher()
    {
        var w = await SeedAsync();
        var id = (await StudentRequestAsync(w.SlotId, requiresParent: true)).ObjectId;

        var result = await WithServiceAsync(s => s.CancelBookingAsync(StudentUserId, id, null));

        result.Success.ShouldBeTrue(result.Message);
        var stored = await BookingAsync(id);
        stored.Status.ShouldBe(BookingStatus.Cancelled);
        stored.CancelledFromStatus.ShouldBe(BookingStatus.AwaitingParent);

        var e = (await LifecycleEventsAsync()).ShouldHaveSingleItem();
        e.NotifyTeacher.ShouldBeFalse("öğretmen veli onayından önce talebi hiç görmedi");
        e.NotifyStudent.ShouldBeFalse();
        // Veliler, onay bekleyen talebin geri çekildiğini öğrenir.
        e.Parents.Select(p => p.ParentUserId).OrderBy(x => x).ShouldBe(new[] { ParentUserId, SecondParentUserId }.OrderBy(x => x));
    }

    [Fact]
    public async Task CancelBookingAsync_StudentCancelsApprovedBeforeDeadline_NotifiesTeacherAndParents_AndFreesTheSlot()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);

        var result = await WithServiceAsync(s => s.CancelBookingAsync(StudentUserId, id, null));

        result.Success.ShouldBeTrue(result.Message);
        (await BookingAsync(id)).CancelledFromStatus.ShouldBe(BookingStatus.Approved);

        var e = (await LifecycleEventsAsync()).Single(x => x.Kind == BookingLifecycleKinds.Cancelled);
        e.NotifyTeacher.ShouldBeTrue();
        e.NotifyStudent.ShouldBeFalse();
        e.Parents.Select(p => p.ParentUserId).OrderBy(x => x).ShouldBe(new[] { ParentUserId, SecondParentUserId }.OrderBy(x => x));
        e.Parents.ShouldAllBe(p => p.KeycloakId == $"kc-{p.ParentUserId}");

        // Slot müsaitliğe döner: aynı slota yeni talep oluşturulabilir.
        var rebook = await StudentRequestAsync(w.SlotId, studentUserId: StudentUserId);
        rebook.Success.ShouldBeTrue(rebook.Message);
    }

    [Fact]
    public async Task CancelBookingAsync_TeacherCancelsApproved_NotifiesStudentOnly()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);

        var result = await WithServiceAsync(s => s.CancelBookingAsync(TeacherUserId, id, "Hastayım"));

        result.Success.ShouldBeTrue(result.Message);
        var stored = await BookingAsync(id);
        stored.CancelledByRole.ShouldBe(BookingParty.Teacher);
        stored.CancelledByUserId.ShouldBe(TeacherUserId);

        var e = (await LifecycleEventsAsync()).Single(x => x.Kind == BookingLifecycleKinds.Cancelled);
        e.ActorRole.ShouldBe(BookingParty.Teacher);
        e.NotifyTeacher.ShouldBeFalse();
        e.NotifyStudent.ShouldBeTrue();
        e.Reason.ShouldBe("Hastayım");
        e.Parents.Count.ShouldBe(2);
    }

    [Fact]
    public async Task CancelBookingAsync_ApprovedAtExactlyTheDeadline_Succeeds()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);
        _clock.Now = LessonStartUtc.AddHours(-2); // sınır dahil: "en az 2 saat kala"

        var result = await WithServiceAsync(s => s.CancelBookingAsync(StudentUserId, id, null));

        result.Success.ShouldBeTrue(result.Message);
        (await BookingAsync(id)).Status.ShouldBe(BookingStatus.Cancelled);
    }

    [Fact]
    public async Task CancelBookingAsync_ApprovedAfterTheDeadline_ReturnsCancellationWindowClosed()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);
        _clock.Now = LessonStartUtc.AddHours(-2).AddMinutes(1);

        var result = await WithServiceAsync(s => s.CancelBookingAsync(StudentUserId, id, null));

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
        result.ErrorCode.ShouldBe(BookingErrorCodes.CancellationWindowClosed);
        (await BookingAsync(id)).Status.ShouldBe(BookingStatus.Approved);
        (await LifecycleEventsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task CancelBookingAsync_TeacherCancelsPending_ReturnsInvalidState()
    {
        var w = await SeedAsync();
        var id = (await StudentRequestAsync(w.SlotId)).ObjectId;

        var result = await WithServiceAsync(s => s.CancelBookingAsync(TeacherUserId, id, null));

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
        result.ErrorCode.ShouldBe(BookingErrorCodes.InvalidState);
        (await BookingAsync(id)).Status.ShouldBe(BookingStatus.Pending);
        (await LifecycleEventsAsync()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(OtherStudentUserId)]
    [InlineData(ParentUserId)]
    [InlineData(OtherTeacherUserId)]
    public async Task CancelBookingAsync_NonParticipant_ReturnsNotFound(int callerUserId)
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);

        var result = await WithServiceAsync(s => s.CancelBookingAsync(callerUserId, id, null));

        result.Success.ShouldBeFalse();
        result.NotFound.ShouldBeTrue();
        result.ErrorCode.ShouldBe(BookingErrorCodes.NotFound);
        (await BookingAsync(id)).Status.ShouldBe(BookingStatus.Approved);
        (await LifecycleEventsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task CancelBookingAsync_ReasonWithControlCharacters_ReturnsInvalidNote()
    {
        var w = await SeedAsync();
        var id = (await StudentRequestAsync(w.SlotId)).ObjectId;

        var result = await WithServiceAsync(s => s.CancelBookingAsync(StudentUserId, id, "bir\u0007iki"));

        result.Success.ShouldBeFalse();
        result.ErrorCode.ShouldBe(BookingErrorCodes.InvalidNote);
        (await BookingAsync(id)).Status.ShouldBe(BookingStatus.Pending);
    }

    [Fact]
    public async Task GetTeacherBookingsAsync_ListsCancelledPendingButHidesCancelledAwaitingParent()
    {
        var w = await SeedAsync();
        var pending = (await StudentRequestAsync(w.SlotId)).ObjectId;
        var awaiting = (await StudentRequestAsync(w.SlotId2, requiresParent: true)).ObjectId;
        (await WithServiceAsync(s => s.CancelBookingAsync(StudentUserId, pending, null))).Success.ShouldBeTrue();
        (await WithServiceAsync(s => s.CancelBookingAsync(StudentUserId, awaiting, null))).Success.ShouldBeTrue();

        var list = await WithServiceAsync(s => s.GetTeacherBookingsAsync(TeacherUserId, 0, 50));

        var item = list.Items.ShouldHaveSingleItem();
        item.Id.ShouldBe(pending);
        item.Status.ShouldBe(nameof(BookingStatus.Cancelled));
        item.CancelledByRole.ShouldBe(BookingParty.Student);
    }

    [Fact]
    public async Task CancelBookingAsync_ApprovedBooking_ClosesWhiteboardWithBookingCancelledReason()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);
        var closer = Substitute.For<IWhiteboardSessionCloser>();

        var result = await WithServiceAsync(s => s.CancelBookingAsync(StudentUserId, id, null), closer);

        result.Success.ShouldBeTrue(result.Message);
        await closer.Received(1).CloseAsync(id, WhiteboardCloseReasons.BookingCancelled, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelBookingAsync_PendingBooking_DoesNotTouchTheWhiteboard()
    {
        var w = await SeedAsync();
        var id = (await StudentRequestAsync(w.SlotId)).ObjectId;
        var closer = Substitute.For<IWhiteboardSessionCloser>();

        var result = await WithServiceAsync(s => s.CancelBookingAsync(StudentUserId, id, null), closer);

        result.Success.ShouldBeTrue(result.Message);
        closer.ReceivedCalls().ShouldBeEmpty();
    }

    // ------------------------------------------------------------------
    // Erteleme: öneri
    // ------------------------------------------------------------------

    [Fact]
    public async Task ProposeRescheduleAsync_StudentProposesFreeSlot_StoresProposalWithoutMovingTheBooking()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);

        var result = await WithServiceAsync(s => s.ProposeRescheduleAsync(StudentUserId, id, w.SlotId2));

        result.Success.ShouldBeTrue(result.Message);
        var stored = await BookingAsync(id);
        stored.AvailabilitySlotId.ShouldBe(w.SlotId, "öneri kabul edilene kadar randevu eski slotta kalır");
        stored.ProposedSlotId.ShouldBe(w.SlotId2);
        stored.ProposedByRole.ShouldBe(BookingParty.Student);
        stored.ProposedByUserId.ShouldBe(StudentUserId);
        stored.ProposedAt.ShouldBe(Now);
        stored.RescheduleCount.ShouldBe(0);

        var e = (await LifecycleEventsAsync()).Single(x => x.Kind == BookingLifecycleKinds.RescheduleProposed);
        e.ActorRole.ShouldBe(BookingParty.Student);
        e.NotifyTeacher.ShouldBeTrue();
        e.NotifyStudent.ShouldBeFalse();
        e.Date.ShouldBe(DateOnly.FromDateTime(Now).AddDays(10));
        e.StartTime.ShouldBe(new TimeOnly(10, 0));
        e.NewDate.ShouldBe(DateOnly.FromDateTime(Now).AddDays(10));
        e.NewStartTime.ShouldBe(new TimeOnly(14, 0));
        e.NewEndTime.ShouldBe(new TimeOnly(15, 0));
    }

    [Fact]
    public async Task ProposeRescheduleAsync_TeacherProposes_NotifiesStudentOnly()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);

        var result = await WithServiceAsync(s => s.ProposeRescheduleAsync(TeacherUserId, id, w.SlotId2));

        result.Success.ShouldBeTrue(result.Message);
        (await BookingAsync(id)).ProposedByRole.ShouldBe(BookingParty.Teacher);
        var e = (await LifecycleEventsAsync()).Single(x => x.Kind == BookingLifecycleKinds.RescheduleProposed);
        e.NotifyTeacher.ShouldBeFalse();
        e.NotifyStudent.ShouldBeTrue();
    }

    [Fact]
    public async Task ProposeRescheduleAsync_SecondProposalWhileOnePending_ReturnsRescheduleAlreadyProposed()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);
        (await WithServiceAsync(s => s.ProposeRescheduleAsync(StudentUserId, id, w.SlotId2))).Success.ShouldBeTrue();

        var second = await WithServiceAsync(s => s.ProposeRescheduleAsync(TeacherUserId, id, w.SlotId3));

        second.Success.ShouldBeFalse();
        second.Conflict.ShouldBeTrue();
        second.ErrorCode.ShouldBe(BookingErrorCodes.RescheduleAlreadyProposed);
        (await BookingAsync(id)).ProposedSlotId.ShouldBe(w.SlotId2);
    }

    [Fact]
    public async Task ProposeRescheduleAsync_SlotOfAnotherTeacher_ReturnsProposedSlotUnavailableWithoutConflict()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);

        var result = await WithServiceAsync(s => s.ProposeRescheduleAsync(StudentUserId, id, w.OtherTeacherSlotId));

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeFalse("400 — çakışma değil, geçersiz slot");
        result.NotFound.ShouldBeFalse();
        result.ErrorCode.ShouldBe(BookingErrorCodes.ProposedSlotUnavailable);
        (await BookingAsync(id)).ProposedSlotId.ShouldBeNull();
    }

    [Fact]
    public async Task ProposeRescheduleAsync_PastSlot_ReturnsProposedSlotUnavailableWithoutConflict()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);

        var result = await WithServiceAsync(s => s.ProposeRescheduleAsync(StudentUserId, id, w.PastSlotId));

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeFalse();
        result.ErrorCode.ShouldBe(BookingErrorCodes.ProposedSlotUnavailable);
    }

    [Fact]
    public async Task ProposeRescheduleAsync_CurrentSlot_ReturnsProposedSlotUnavailableWithoutConflict()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);

        var result = await WithServiceAsync(s => s.ProposeRescheduleAsync(StudentUserId, id, w.SlotId));

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeFalse();
        result.ErrorCode.ShouldBe(BookingErrorCodes.ProposedSlotUnavailable);
    }

    [Fact]
    public async Task ProposeRescheduleAsync_SlotBookedByAnotherStudent_ReturnsProposedSlotUnavailableAsConflict()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);
        (await StudentRequestAsync(w.SlotId2, studentUserId: OtherStudentUserId)).Success.ShouldBeTrue();

        var result = await WithServiceAsync(s => s.ProposeRescheduleAsync(StudentUserId, id, w.SlotId2));

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
        result.ErrorCode.ShouldBe(BookingErrorCodes.ProposedSlotUnavailable);
        (await BookingAsync(id)).ProposedSlotId.ShouldBeNull();
    }

    [Fact]
    public async Task ProposeRescheduleAsync_WithinTheNoticeWindow_ReturnsRescheduleWindowClosed()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);
        _clock.Now = LessonStartUtc.AddHours(-2).AddMinutes(1);

        var result = await WithServiceAsync(s => s.ProposeRescheduleAsync(StudentUserId, id, w.SlotId2));

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
        result.ErrorCode.ShouldBe(BookingErrorCodes.RescheduleWindowClosed);
        (await BookingAsync(id)).ProposedSlotId.ShouldBeNull();
    }

    [Fact]
    public async Task ProposeRescheduleAsync_PendingBooking_ReturnsInvalidState()
    {
        var w = await SeedAsync();
        var id = (await StudentRequestAsync(w.SlotId)).ObjectId;

        var result = await WithServiceAsync(s => s.ProposeRescheduleAsync(StudentUserId, id, w.SlotId2));

        result.Success.ShouldBeFalse();
        result.ErrorCode.ShouldBe(BookingErrorCodes.InvalidState);
    }

    [Fact]
    public async Task ProposeRescheduleAsync_AfterTwoAcceptedReschedules_ReturnsRescheduleLimitReached()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);
        await ProposeAndAcceptByTeacherAsync(id, w.SlotId2);
        await ProposeAndAcceptByTeacherAsync(id, w.SlotId3);
        (await BookingAsync(id)).RescheduleCount.ShouldBe(2);

        var third = await WithServiceAsync(s => s.ProposeRescheduleAsync(StudentUserId, id, w.SlotId4));

        third.Success.ShouldBeFalse();
        third.Conflict.ShouldBeTrue();
        third.ErrorCode.ShouldBe(BookingErrorCodes.RescheduleLimitReached);
        (await BookingAsync(id)).ProposedSlotId.ShouldBeNull();
    }

    // ------------------------------------------------------------------
    // Erteleme: yanıt
    // ------------------------------------------------------------------

    [Fact]
    public async Task RespondRescheduleAsync_CounterpartyAccepts_MovesBookingToNewSlot_AndFreesOldSlot()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);
        (await WithServiceAsync(s => s.ProposeRescheduleAsync(StudentUserId, id, w.SlotId2))).Success.ShouldBeTrue();

        var result = await WithServiceAsync(s => s.RespondRescheduleAsync(TeacherUserId, id, true));

        result.Success.ShouldBeTrue(result.Message);
        var stored = await BookingAsync(id);
        stored.Status.ShouldBe(BookingStatus.Approved);
        stored.AvailabilitySlotId.ShouldBe(w.SlotId2);
        stored.RescheduleCount.ShouldBe(1);
        stored.ProposedSlotId.ShouldBeNull();
        stored.ProposedByRole.ShouldBeNull();

        var e = (await LifecycleEventsAsync()).Single(x => x.Kind == BookingLifecycleKinds.RescheduleAccepted);
        e.ActorRole.ShouldBe(BookingParty.Teacher);
        e.NotifyStudent.ShouldBeTrue();
        e.NotifyTeacher.ShouldBeFalse();
        e.NewStartTime.ShouldBe(new TimeOnly(14, 0));
        e.Parents.Select(p => p.ParentUserId).OrderBy(x => x).ShouldBe(new[] { ParentUserId, SecondParentUserId }.OrderBy(x => x));

        (await StudentRequestAsync(w.SlotId, studentUserId: OtherStudentUserId)).Success.ShouldBeTrue("eski slot serbest");
        (await StudentRequestAsync(w.SlotId2, studentUserId: OtherStudentUserId)).Conflict.ShouldBeTrue("yeni slot dolu");
    }

    [Fact]
    public async Task RespondRescheduleAsync_CounterpartyDeclines_ClearsProposalAndKeepsOriginalSlot()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);
        (await WithServiceAsync(s => s.ProposeRescheduleAsync(StudentUserId, id, w.SlotId2))).Success.ShouldBeTrue();

        var result = await WithServiceAsync(s => s.RespondRescheduleAsync(TeacherUserId, id, false));

        result.Success.ShouldBeTrue(result.Message);
        var stored = await BookingAsync(id);
        stored.Status.ShouldBe(BookingStatus.Approved);
        stored.AvailabilitySlotId.ShouldBe(w.SlotId);
        stored.RescheduleCount.ShouldBe(0);
        stored.ProposedSlotId.ShouldBeNull();

        var e = (await LifecycleEventsAsync()).Single(x => x.Kind == BookingLifecycleKinds.RescheduleDeclined);
        e.NotifyStudent.ShouldBeTrue();
        e.NotifyTeacher.ShouldBeFalse();
    }

    [Fact]
    public async Task RespondRescheduleAsync_ProposerAcceptsOwnProposal_ReturnsCannotRespondOwnProposal()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);
        (await WithServiceAsync(s => s.ProposeRescheduleAsync(StudentUserId, id, w.SlotId2))).Success.ShouldBeTrue();

        var result = await WithServiceAsync(s => s.RespondRescheduleAsync(StudentUserId, id, true));

        result.Success.ShouldBeFalse();
        result.Forbidden.ShouldBeTrue();
        result.ErrorCode.ShouldBe(BookingErrorCodes.CannotRespondOwnProposal);
        (await BookingAsync(id)).ProposedSlotId.ShouldBe(w.SlotId2);
    }

    [Fact]
    public async Task RespondRescheduleAsync_ProposerWithdraws_EmitsRescheduleWithdrawnForTheCounterparty()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);
        (await WithServiceAsync(s => s.ProposeRescheduleAsync(StudentUserId, id, w.SlotId2))).Success.ShouldBeTrue();

        var result = await WithServiceAsync(s => s.RespondRescheduleAsync(StudentUserId, id, false));

        result.Success.ShouldBeTrue(result.Message);
        (await BookingAsync(id)).ProposedSlotId.ShouldBeNull();
        var e = (await LifecycleEventsAsync()).Single(x => x.Kind == BookingLifecycleKinds.RescheduleWithdrawn);
        e.ActorRole.ShouldBe(BookingParty.Student);
        e.NotifyTeacher.ShouldBeTrue();
        e.NotifyStudent.ShouldBeFalse();
    }

    [Fact]
    public async Task RespondRescheduleAsync_AcceptWhenSlotWasBookedMeanwhile_ReturnsProposedSlotUnavailable()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);
        (await WithServiceAsync(s => s.ProposeRescheduleAsync(StudentUserId, id, w.SlotId2))).Success.ShouldBeTrue();
        (await StudentRequestAsync(w.SlotId2, studentUserId: OtherStudentUserId)).Success.ShouldBeTrue();

        var result = await WithServiceAsync(s => s.RespondRescheduleAsync(TeacherUserId, id, true));

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
        result.ErrorCode.ShouldBe(BookingErrorCodes.ProposedSlotUnavailable);
        var stored = await BookingAsync(id);
        stored.AvailabilitySlotId.ShouldBe(w.SlotId, "yarım taşıma yok");
        stored.RescheduleCount.ShouldBe(0);
        stored.ProposedSlotId.ShouldBe(w.SlotId2);
        (await LifecycleEventsAsync()).ShouldNotContain(x => x.Kind == BookingLifecycleKinds.RescheduleAccepted);
    }

    [Fact]
    public async Task RespondRescheduleAsync_AcceptWithinTheNoticeWindow_ReturnsRescheduleWindowClosed()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);
        (await WithServiceAsync(s => s.ProposeRescheduleAsync(StudentUserId, id, w.SlotId2))).Success.ShouldBeTrue();
        _clock.Now = LessonStartUtc.AddHours(-1);

        var result = await WithServiceAsync(s => s.RespondRescheduleAsync(TeacherUserId, id, true));

        result.Success.ShouldBeFalse();
        result.ErrorCode.ShouldBe(BookingErrorCodes.RescheduleWindowClosed);
        (await BookingAsync(id)).AvailabilitySlotId.ShouldBe(w.SlotId);
    }

    [Fact]
    public async Task RespondRescheduleAsync_NoProposalPending_ReturnsNoPendingReschedule()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);

        var result = await WithServiceAsync(s => s.RespondRescheduleAsync(TeacherUserId, id, true));

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
        result.ErrorCode.ShouldBe(BookingErrorCodes.NoPendingReschedule);
    }

    // ------------------------------------------------------------------
    // DTO alanları (öğretmen listesi)
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetTeacherBookingsAsync_ApprovedBooking_ExposesDeadlinesAndRescheduleFlag()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);

        var dto = (await WithServiceAsync(s => s.GetTeacherBookingsAsync(TeacherUserId, 0, 50))).Items.Single(i => i.Id == id);

        dto.CancellationDeadlineUtc.ShouldBe(LessonStartUtc.AddHours(-2));
        dto.CanProposeReschedule.ShouldBeTrue();
        dto.RescheduleCount.ShouldBe(0);
        dto.ProposedSlotId.ShouldBeNull();
        dto.NoShowReportableUntilUtc.ShouldBe(LessonEndUtc.AddDays(7));
        dto.NoShowParty.ShouldBeNull();
    }

    [Fact]
    public async Task GetTeacherBookingsAsync_ApprovedBookingPastNoticeWindow_CanNotProposeReschedule()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);
        _clock.Now = LessonStartUtc.AddHours(-2).AddMinutes(1);

        var dto = (await WithServiceAsync(s => s.GetTeacherBookingsAsync(TeacherUserId, 0, 50))).Items.Single(i => i.Id == id);

        dto.CanProposeReschedule.ShouldBeFalse();
    }

    [Fact]
    public async Task GetTeacherBookingsAsync_WithPendingProposal_ExposesProposedSlotAndCannotProposeAgain()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);
        (await WithServiceAsync(s => s.ProposeRescheduleAsync(StudentUserId, id, w.SlotId2))).Success.ShouldBeTrue();

        var dto = (await WithServiceAsync(s => s.GetTeacherBookingsAsync(TeacherUserId, 0, 50))).Items.Single(i => i.Id == id);

        dto.ProposedSlotId.ShouldBe(w.SlotId2);
        dto.ProposedStartUtc.ShouldBe(LessonStartUtc.AddHours(4));
        dto.ProposedEndUtc.ShouldBe(LessonStartUtc.AddHours(5));
        dto.ProposedByRole.ShouldBe(BookingParty.Student);
        dto.CanProposeReschedule.ShouldBeFalse();
    }

    // ------------------------------------------------------------------
    // No-show
    // ------------------------------------------------------------------

    [Fact]
    public async Task ReportNoShowAsync_BeforeLessonEnds_ReturnsNoShowWindowClosed()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);
        _clock.Now = LessonStartUtc.AddMinutes(30);

        var result = await WithServiceAsync(s => s.ReportNoShowAsync(StudentUserId, id));

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
        result.ErrorCode.ShouldBe(BookingErrorCodes.NoShowWindowClosed);
        (await BookingAsync(id)).NoShowParty.ShouldBeNull();
    }

    [Fact]
    public async Task ReportNoShowAsync_WithinWindow_MarksTheOtherPartyAndNotifiesTeacher()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);
        _clock.Now = LessonEndUtc.AddHours(1);

        var result = await WithServiceAsync(s => s.ReportNoShowAsync(StudentUserId, id));

        result.Success.ShouldBeTrue(result.Message);
        var stored = await BookingAsync(id);
        stored.Status.ShouldBe(BookingStatus.Approved, "no-show durumu değiştirmez");
        stored.NoShowParty.ShouldBe(BookingParty.Teacher);
        stored.NoShowReportedByUserId.ShouldBe(StudentUserId);
        stored.NoShowReportedAt.ShouldBe(Now);

        var e = (await LifecycleEventsAsync()).Single(x => x.Kind == BookingLifecycleKinds.NoShowReported);
        e.NoShowParty.ShouldBe(BookingParty.Teacher);
        e.NotifyTeacher.ShouldBeTrue();
        e.NotifyStudent.ShouldBeFalse();
        e.Parents.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ReportNoShowAsync_SecondReportByEitherParty_ReturnsNoShowAlreadyReported_AndKeepsFirstVerdict()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);
        _clock.Now = LessonEndUtc.AddHours(1);
        (await WithServiceAsync(s => s.ReportNoShowAsync(StudentUserId, id))).Success.ShouldBeTrue();

        var counter = await WithServiceAsync(s => s.ReportNoShowAsync(TeacherUserId, id));

        counter.Success.ShouldBeFalse();
        counter.Conflict.ShouldBeTrue();
        counter.ErrorCode.ShouldBe(BookingErrorCodes.NoShowAlreadyReported);
        (await BookingAsync(id)).NoShowParty.ShouldBe(BookingParty.Teacher);
        (await LifecycleEventsAsync()).Count(x => x.Kind == BookingLifecycleKinds.NoShowReported).ShouldBe(1);
    }

    [Fact]
    public async Task ReportNoShowAsync_OnTheLastDayOfTheWindow_Succeeds()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);
        _clock.Now = LessonEndUtc.AddDays(7);

        var result = await WithServiceAsync(s => s.ReportNoShowAsync(TeacherUserId, id));

        result.Success.ShouldBeTrue(result.Message);
        (await BookingAsync(id)).NoShowParty.ShouldBe(BookingParty.Student);
    }

    [Fact]
    public async Task ReportNoShowAsync_AfterTheWindow_ReturnsNoShowWindowClosed()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);
        _clock.Now = LessonEndUtc.AddDays(7).AddMinutes(1);

        var result = await WithServiceAsync(s => s.ReportNoShowAsync(StudentUserId, id));

        result.Success.ShouldBeFalse();
        result.ErrorCode.ShouldBe(BookingErrorCodes.NoShowWindowClosed);
        (await BookingAsync(id)).NoShowParty.ShouldBeNull();
    }

    [Fact]
    public async Task ReportNoShowAsync_PendingBooking_ReturnsInvalidState()
    {
        var w = await SeedAsync();
        var id = (await StudentRequestAsync(w.SlotId)).ObjectId;
        _clock.Now = LessonEndUtc.AddHours(1);

        var result = await WithServiceAsync(s => s.ReportNoShowAsync(StudentUserId, id));

        result.Success.ShouldBeFalse();
        result.ErrorCode.ShouldBe(BookingErrorCodes.InvalidState);
    }

    // ------------------------------------------------------------------
    // Güvenlik turu: süre eşleşmesi, öneri sayısı, kabulde öğretmen uygunluğu, UPDATE'te slot koşulu
    // ------------------------------------------------------------------

    /// <summary>
    /// Koşullu UPDATE'ten hemen önce randevunun slotunu değiştirir (yarış simülasyonu: load ile yazım arasında başka bir istek
    /// slotu taşımış gibi). Yazım 0 satır etkilemeli.
    /// </summary>
    private sealed class SlotMoverInterceptor(int bookingId, int newSlotId, string marker) : DbCommandInterceptor
    {
        public int Fired { get; private set; }

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Move(command);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Move(command);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Move(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Move(command);
            return ValueTask.FromResult(result);
        }

        private void Move(DbCommand command)
        {
            if (Fired > 0 || !command.CommandText.Contains("UPDATE \"Bookings\"") || !command.CommandText.Contains(marker))
                return;

            Fired++;
            using var inner = command.Connection!.CreateCommand();
            inner.Transaction = command.Transaction;
            inner.CommandText = $"UPDATE \"Bookings\" SET \"AvailabilitySlotId\" = {newSlotId} WHERE \"Id\" = {bookingId}";
            inner.ExecuteNonQuery();
        }
    }

    [Fact]
    public async Task ProposeRescheduleAsync_SlotWithDifferentDuration_ReturnsProposedSlotDurationMismatch()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId); // 10:00-11:00
        int longSlotId;
        await using (var ctx = _db.NewContext())
        {
            var slot = new TeacherAvailabilitySlot
            {
                TeacherId = w.TeacherId, Date = DateOnly.FromDateTime(Now).AddDays(10), StartTime = new TimeOnly(16, 0),
                EndTime = new TimeOnly(17, 30), CreatedAt = Now
            };
            ctx.TeacherAvailabilitySlots.Add(slot);
            await ctx.SaveChangesAsync();
            longSlotId = slot.Id;
        }

        var result = await WithServiceAsync(s => s.ProposeRescheduleAsync(StudentUserId, id, longSlotId));

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeFalse("400 — süre uyuşmazlığı çakışma değil");
        result.ErrorCode.ShouldBe(BookingErrorCodes.ProposedSlotDurationMismatch);
        (await BookingAsync(id)).ProposedSlotId.ShouldBeNull();
    }

    [Fact]
    public async Task ProposeRescheduleAsync_FifthProposal_ReturnsRescheduleProposalLimitReached_EvenWhenEarlierOnesWereDeclined()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);
        for (var i = 0; i < 4; i++)
        {
            (await WithServiceAsync(s => s.ProposeRescheduleAsync(StudentUserId, id, w.SlotId2))).Success.ShouldBeTrue($"öneri {i + 1}");
            (await WithServiceAsync(s => s.RespondRescheduleAsync(TeacherUserId, id, false))).Success.ShouldBeTrue($"ret {i + 1}");
        }

        var stored = await BookingAsync(id);
        stored.RescheduleCount.ShouldBe(0, "reddedilen öneri erteleme sayılmaz");
        stored.RescheduleProposalCount.ShouldBe(4, "her öneri (ret dahil) sayılır");

        var dto = (await WithServiceAsync(s => s.GetTeacherBookingsAsync(TeacherUserId, 0, 50))).Items.Single(i => i.Id == id);
        dto.CanProposeReschedule.ShouldBeFalse();

        var fifth = await WithServiceAsync(s => s.ProposeRescheduleAsync(StudentUserId, id, w.SlotId2));
        fifth.Success.ShouldBeFalse();
        fifth.Conflict.ShouldBeTrue();
        fifth.ErrorCode.ShouldBe(BookingErrorCodes.RescheduleProposalLimitReached);
    }

    [Fact]
    public async Task RespondRescheduleAsync_WithdrawnProposalStillCountsTowardTheProposalLimit()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);
        (await WithServiceAsync(s => s.ProposeRescheduleAsync(StudentUserId, id, w.SlotId2))).Success.ShouldBeTrue();
        (await WithServiceAsync(s => s.RespondRescheduleAsync(StudentUserId, id, false))).Success.ShouldBeTrue();

        (await BookingAsync(id)).RescheduleProposalCount.ShouldBe(1);
    }

    [Fact]
    public async Task RespondRescheduleAsync_AcceptAfterTeacherSuspended_ReturnsTeacherUnavailable_AndKeepsTheProposal()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);
        (await WithServiceAsync(s => s.ProposeRescheduleAsync(StudentUserId, id, w.SlotId2))).Success.ShouldBeTrue();
        await using (var ctx = _db.NewContext())
            await ctx.Teachers.Where(t => t.Id == w.TeacherId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.AccountSuspendedAt, (DateTime?)Now));

        var result = await WithServiceAsync(s => s.RespondRescheduleAsync(TeacherUserId, id, true));

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
        result.ErrorCode.ShouldBe(BookingErrorCodes.TeacherUnavailable);
        var stored = await BookingAsync(id);
        stored.AvailabilitySlotId.ShouldBe(w.SlotId);
        stored.RescheduleCount.ShouldBe(0);
        stored.ProposedSlotId.ShouldBe(w.SlotId2);
        (await LifecycleEventsAsync()).ShouldNotContain(x => x.Kind == BookingLifecycleKinds.RescheduleAccepted);
    }

    [Fact]
    public async Task CancelBookingAsync_SlotChangedBetweenLoadAndWrite_WritesNothing()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);
        var mover = new SlotMoverInterceptor(id, w.SlotId2, "\"CancelledAt\"");
        await using var ctx = _db.NewContext(mover);

        var result = await NewService(ctx).CancelBookingAsync(StudentUserId, id, null);

        mover.Fired.ShouldBe(1, "yarış simülasyonu koşullu UPDATE'ten önce çalışmalı");
        result.Success.ShouldBeFalse();
        result.ErrorCode.ShouldBe(BookingErrorCodes.InvalidState);
        // Simüle edilen değişiklik aynı transaction'da olduğu için rollback ile geri alınır; iptal yazılmamış olmalı.
        var stored = await BookingAsync(id);
        stored.Status.ShouldBe(BookingStatus.Approved);
        stored.CancelledAt.ShouldBeNull();
        (await LifecycleEventsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task ReportNoShowAsync_SlotChangedBetweenLoadAndWrite_WritesNothing()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);
        _clock.Now = LessonEndUtc.AddHours(1);
        var mover = new SlotMoverInterceptor(id, w.SlotId2, "\"NoShowParty\"");
        await using var ctx = _db.NewContext(mover);

        var result = await NewService(ctx).ReportNoShowAsync(StudentUserId, id);

        mover.Fired.ShouldBe(1, "yarış simülasyonu koşullu UPDATE'ten önce çalışmalı");
        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
        (await BookingAsync(id)).NoShowParty.ShouldBeNull();
        (await LifecycleEventsAsync()).ShouldBeEmpty();
    }

    // ------------------------------------------------------------------
    // Code review düzeltmeleri (U1-U3) + kabul kriteri: iptal edilen randevunun görüşme odası
    // ------------------------------------------------------------------

    [Fact]
    public async Task ProposeRescheduleAsync_SlotStartingWithinTheNoticeWindow_ReturnsProposedSlotUnavailable()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);
        int soonSlotId;
        await using (var ctx = _db.NewContext())
        {
            var soon = Now.AddMinutes(90); // varsayılan bildirim süresi 2 saat
            var slot = Slot(w.TeacherId, DateOnly.FromDateTime(soon), soon.Hour);
            ctx.TeacherAvailabilitySlots.Add(slot);
            await ctx.SaveChangesAsync();
            soonSlotId = slot.Id;
        }

        var result = await WithServiceAsync(s => s.ProposeRescheduleAsync(StudentUserId, id, soonSlotId));

        result.Success.ShouldBeFalse();
        result.ErrorCode.ShouldBe(BookingErrorCodes.ProposedSlotUnavailable);
        (await BookingAsync(id)).ProposedSlotId.ShouldBeNull();
    }

    [Fact]
    public async Task CancelledBooking_LiveSessionAccess_IsDeniedAsNotApproved()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);
        (await WithServiceAsync(s => s.CancelBookingAsync(StudentUserId, id, null))).Success.ShouldBeTrue();

        var access = await WithServiceAsync(s => s.GetLiveSessionAccessAsync(StudentUserId, id));

        access.Denial.ShouldBe(BookingLiveSessionDenial.NotApproved);
    }

    [Fact]
    public async Task ApproveBookingAsync_OnRequestWithdrawnByStudent_ReturnsAlreadyDecidedNotNotFound()
    {
        var w = await SeedAsync();
        var id = (await StudentRequestAsync(w.SlotId)).ObjectId;
        (await WithServiceAsync(s => s.CancelBookingAsync(StudentUserId, id, null))).Success.ShouldBeTrue();

        var result = await WithServiceAsync(s => s.ApproveBookingAsync(TeacherUserId, id));

        result.Success.ShouldBeFalse();
        result.NotFound.ShouldBeFalse();
        (await BookingAsync(id)).Status.ShouldBe(BookingStatus.Cancelled);
    }

    [Fact]
    public async Task GetStudentBookingsAsync_ProposalOnDeletedSlot_CanNotBeAcceptedAnyMore()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);
        (await WithServiceAsync(s => s.ProposeRescheduleAsync(TeacherUserId, id, w.SlotId2))).Success.ShouldBeTrue();

        var before = (await WithServiceAsync(s => s.GetStudentBookingsAsync(StudentUserId, 0, 50))).Items.Single(i => i.Id == id);
        before.CanRespondReschedule.ShouldBeTrue();

        await using (var ctx = _db.NewContext())
        {
            var slot = await ctx.TeacherAvailabilitySlots.SingleAsync(s => s.Id == w.SlotId2);
            slot.IsDeleted = true;
            await ctx.SaveChangesAsync();
        }

        var after = (await WithServiceAsync(s => s.GetStudentBookingsAsync(StudentUserId, 0, 50))).Items.Single(i => i.Id == id);
        after.ProposedSlotId.ShouldBe(w.SlotId2);
        after.CanRespondReschedule.ShouldBeFalse();

        // Bayat öneri yine reddedilebilir (yeni öneriye yer açılır).
        (await WithServiceAsync(s => s.RespondRescheduleAsync(StudentUserId, id, false))).Success.ShouldBeTrue();
        (await BookingAsync(id)).ProposedSlotId.ShouldBeNull();
    }

    // ------------------------------------------------------------------
    // issue #320: açık görüşmenin periyodik durum yoklaması (live-status)
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetLiveSessionStatusAsync_WithinWindow_IsActive()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);
        _clock.Now = LessonStartUtc.AddMinutes(10);

        var status = await WithServiceAsync(s => s.GetLiveSessionStatusAsync(StudentUserId, id));

        status.Success.ShouldBeTrue();
        status.Active.ShouldBeTrue();
        status.ErrorCode.ShouldBeNull();
    }

    [Fact]
    public async Task GetLiveSessionStatusAsync_TeacherSuspendedDuringLesson_IsInactiveWithTeacherUnavailable()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);
        _clock.Now = LessonStartUtc.AddMinutes(10);
        await using (var ctx = _db.NewContext())
        {
            var teacher = await ctx.Teachers.SingleAsync(t => t.Id == w.TeacherId);
            teacher.AccountSuspendedAt = Now;
            await ctx.SaveChangesAsync();
        }

        var status = await WithServiceAsync(s => s.GetLiveSessionStatusAsync(StudentUserId, id));

        status.Success.ShouldBeTrue();
        status.Active.ShouldBeFalse();
        status.ErrorCode.ShouldBe(LiveSessionStatusCodes.TeacherUnavailable);
    }

    [Fact]
    public async Task GetLiveSessionStatusAsync_CancelledBooking_IsInactiveWithBookingNotApproved()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);
        (await WithServiceAsync(s => s.CancelBookingAsync(TeacherUserId, id, null))).Success.ShouldBeTrue();
        _clock.Now = LessonStartUtc.AddMinutes(10);

        var status = await WithServiceAsync(s => s.GetLiveSessionStatusAsync(StudentUserId, id));

        status.Active.ShouldBeFalse();
        status.ErrorCode.ShouldBe(LiveSessionStatusCodes.BookingNotApproved);
    }

    [Fact]
    public async Task GetLiveSessionStatusAsync_AfterWindow_IsInactiveWithWindowClosed()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);
        _clock.Now = LessonEndUtc.AddMinutes(31);

        var status = await WithServiceAsync(s => s.GetLiveSessionStatusAsync(TeacherUserId, id));

        status.Active.ShouldBeFalse();
        status.ErrorCode.ShouldBe(LiveSessionStatusCodes.WindowClosed);
    }

    [Fact]
    public async Task GetLiveSessionStatusAsync_NonParticipant_IsForbidden_AndUnknownBookingNotFound()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);

        var stranger = await WithServiceAsync(s => s.GetLiveSessionStatusAsync(OtherStudentUserId, id));
        stranger.Success.ShouldBeFalse();
        stranger.NotFound.ShouldBeTrue(); // taraf olmayana da 404 (varlık sızdırılmaz)

        var missing = await WithServiceAsync(s => s.GetLiveSessionStatusAsync(StudentUserId, 999_999));
        missing.NotFound.ShouldBeTrue();
    }
}

