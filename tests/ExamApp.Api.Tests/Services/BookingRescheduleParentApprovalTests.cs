using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Bookings;
using ExamApp.Api.Services.Bookings;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace ExamApp.Api.Tests.Services;

/// <summary>issue #486: Active velisi olan öğrencide kabul edilen erteleme veli onayına düşer.</summary>
public partial class BookingLifecycleTests
{
    private sealed record AcceptedReschedule(World World, int BookingId);

    /// <summary>Öğrenci önerir, öğretmen kabul eder: veli onayı bekleniyor.</summary>
    private async Task<AcceptedReschedule> AcceptedRescheduleAsync()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);
        (await WithServiceAsync(s => s.ProposeRescheduleAsync(StudentUserId, id, w.SlotId2))).Success.ShouldBeTrue();
        var accepted = await WithServiceAsync(s => s.RespondRescheduleAsync(TeacherUserId, id, true));
        accepted.Success.ShouldBeTrue(accepted.Message);
        return new AcceptedReschedule(w, id);
    }

    private async Task<List<ParentAccessAudit>> RescheduleAuditsAsync()
    {
        await using var ctx = _db.NewContext();
        return await ctx.ParentAccessAudits.AsNoTracking()
            .Where(a => a.Endpoint == ParentAccessEndpoints.ChildRescheduleApprove || a.Endpoint == ParentAccessEndpoints.ChildRescheduleReject)
            .ToListAsync();
    }

    [Fact]
    public async Task RespondRescheduleAsync_StudentHasActiveParent_WaitsForParent_WithoutMovingTheLesson()
    {
        var (w, id) = await AcceptedRescheduleAsync();

        var stored = await BookingAsync(id);
        stored.AvailabilitySlotId.ShouldBe(w.SlotId);
        stored.ProposedSlotId.ShouldBe(w.SlotId2);
        stored.RescheduleAcceptedAt.ShouldNotBeNull();
        stored.RescheduleCount.ShouldBe(0);

        var events = await LifecycleEventsAsync();
        events.ShouldNotContain(x => x.Kind == BookingLifecycleKinds.RescheduleAccepted);
        var e = events.Single(x => x.Kind == BookingLifecycleKinds.RescheduleAwaitingParent);
        e.NotifyStudent.ShouldBeTrue();
        e.NotifyTeacher.ShouldBeFalse();
        e.NewStartTime.ShouldBe(new TimeOnly(14, 0));
        e.Parents.Select(p => p.ParentUserId).OrderBy(x => x).ShouldBe(new[] { ParentUserId, SecondParentUserId }.OrderBy(x => x));
    }

    [Fact]
    public async Task RespondRescheduleAsync_WhileWaitingForParent_DtoCannotBeAcceptedAgain()
    {
        var (_, id) = await AcceptedRescheduleAsync();

        var again = await WithServiceAsync(s => s.RespondRescheduleAsync(TeacherUserId, id, true));

        again.Conflict.ShouldBeTrue();
        again.ErrorCode.ShouldBe(BookingErrorCodes.RescheduleAwaitingParent);
        var dto = (await WithServiceAsync(s => s.GetStudentBookingsAsync(StudentUserId, 0, 10))).Items.Single(b => b.Id == id);
        dto.RescheduleAwaitingParent.ShouldBeTrue();
        dto.CanRespondReschedule.ShouldBeFalse();
    }

    [Fact]
    public async Task RespondRescheduleAsync_WhileWaitingForParent_TheProposerCanWithdraw()
    {
        var (w, id) = await AcceptedRescheduleAsync();

        (await WithServiceAsync(s => s.RespondRescheduleAsync(StudentUserId, id, false))).Success.ShouldBeTrue();

        var stored = await BookingAsync(id);
        stored.AvailabilitySlotId.ShouldBe(w.SlotId);
        stored.ProposedSlotId.ShouldBeNull();
        stored.RescheduleAcceptedAt.ShouldBeNull();
        (await WithServiceAsync(s => s.GetParentRescheduleRequestsAsync(ParentUserId))).Items.ShouldBeEmpty();
        (await WithServiceAsync(s => s.ParentApproveRescheduleAsync(ParentUserId, id, w.SlotId2))).ErrorCode
            .ShouldBe(BookingErrorCodes.NoPendingReschedule);
    }

    [Fact]
    public async Task CancelBookingAsync_WhileWaitingForParent_ClearsTheApproval()
    {
        var (_, id) = await AcceptedRescheduleAsync();

        (await WithServiceAsync(s => s.CancelBookingAsync(StudentUserId, id, null))).Success.ShouldBeTrue();

        (await BookingAsync(id)).RescheduleAcceptedAt.ShouldBeNull();
        (await WithServiceAsync(s => s.GetParentRescheduleRequestsAsync(ParentUserId))).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task ParentApproveRescheduleAsync_MovesTheLesson_NotifiesBothSidesAndOtherParents_AndAudits()
    {
        var (w, id) = await AcceptedRescheduleAsync();

        var result = await WithServiceAsync(s => s.ParentApproveRescheduleAsync(SecondParentUserId, id, w.SlotId2));

        result.Success.ShouldBeTrue(result.Message);
        var stored = await BookingAsync(id);
        stored.AvailabilitySlotId.ShouldBe(w.SlotId2);
        stored.RescheduleCount.ShouldBe(1);
        stored.ProposedSlotId.ShouldBeNull();
        stored.RescheduleAcceptedAt.ShouldBeNull();

        var e = (await LifecycleEventsAsync()).Single(x => x.Kind == BookingLifecycleKinds.RescheduleParentApproved);
        e.NotifyTeacher.ShouldBeTrue();
        e.NotifyStudent.ShouldBeTrue();
        e.ActorRole.ShouldBe("Parent");
        e.ActorParentUserId.ShouldBe(SecondParentUserId);

        var audit = (await RescheduleAuditsAsync()).Single();
        audit.Endpoint.ShouldBe(ParentAccessEndpoints.ChildRescheduleApprove);
        audit.ResourceId.ShouldBe(id);
        audit.StudentId.ShouldBe(w.StudentId);

        (await StudentRequestAsync(w.SlotId, studentUserId: OtherStudentUserId)).Success.ShouldBeTrue("eski slot serbest");
    }

    [Fact]
    public async Task ParentApproveRescheduleAsync_SecondParentAfterTheFirst_ReturnsNoPendingReschedule()
    {
        var (w, id) = await AcceptedRescheduleAsync();
        (await WithServiceAsync(s => s.ParentApproveRescheduleAsync(ParentUserId, id, w.SlotId2))).Success.ShouldBeTrue();

        var late = await WithServiceAsync(s => s.ParentRejectRescheduleAsync(SecondParentUserId, id, w.SlotId2));

        late.Conflict.ShouldBeTrue();
        late.ErrorCode.ShouldBe(BookingErrorCodes.NoPendingReschedule);
        (await BookingAsync(id)).AvailabilitySlotId.ShouldBe(w.SlotId2);
    }

    [Fact]
    public async Task ParentRejectRescheduleAsync_KeepsTheOldSlot_NotifiesBothSides_AndAllowsANewProposal()
    {
        var (w, id) = await AcceptedRescheduleAsync();

        var result = await WithServiceAsync(s => s.ParentRejectRescheduleAsync(ParentUserId, id, w.SlotId2));

        result.Success.ShouldBeTrue(result.Message);
        var stored = await BookingAsync(id);
        stored.AvailabilitySlotId.ShouldBe(w.SlotId);
        stored.RescheduleCount.ShouldBe(0);
        stored.ProposedSlotId.ShouldBeNull();
        stored.RescheduleAcceptedAt.ShouldBeNull();

        var e = (await LifecycleEventsAsync()).Single(x => x.Kind == BookingLifecycleKinds.RescheduleParentRejected);
        e.NotifyTeacher.ShouldBeTrue();
        e.NotifyStudent.ShouldBeTrue();
        (await RescheduleAuditsAsync()).Single().Endpoint.ShouldBe(ParentAccessEndpoints.ChildRescheduleReject);

        (await WithServiceAsync(s => s.ProposeRescheduleAsync(TeacherUserId, id, w.SlotId3))).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task ParentDecision_ByUnrelatedParent_ReturnsNotFound_AndChangesNothing()
    {
        var (w, id) = await AcceptedRescheduleAsync();

        (await WithServiceAsync(s => s.ParentApproveRescheduleAsync(UnrelatedParentUserId, id, w.SlotId2))).NotFound.ShouldBeTrue();
        (await WithServiceAsync(s => s.ParentRejectRescheduleAsync(UnrelatedParentUserId, id, w.SlotId2))).NotFound.ShouldBeTrue();

        var stored = await BookingAsync(id);
        stored.AvailabilitySlotId.ShouldBe(w.SlotId);
        stored.ProposedSlotId.ShouldBe(w.SlotId2);
        (await WithServiceAsync(s => s.GetParentRescheduleRequestsAsync(UnrelatedParentUserId))).Items.ShouldBeEmpty();
        (await RescheduleAuditsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task ParentDecision_BeforeTheCounterpartyAccepts_ReturnsNoPendingReschedule()
    {
        var w = await SeedAsync();
        var id = await ApprovedAsync(w.SlotId);
        (await WithServiceAsync(s => s.ProposeRescheduleAsync(StudentUserId, id, w.SlotId2))).Success.ShouldBeTrue();

        (await WithServiceAsync(s => s.ParentApproveRescheduleAsync(ParentUserId, id, w.SlotId2))).ErrorCode
            .ShouldBe(BookingErrorCodes.NoPendingReschedule);
        (await WithServiceAsync(s => s.ParentRejectRescheduleAsync(ParentUserId, id, w.SlotId2))).ErrorCode
            .ShouldBe(BookingErrorCodes.NoPendingReschedule);
        (await BookingAsync(id)).ProposedSlotId.ShouldBe(w.SlotId2);
    }

    [Fact]
    public async Task ParentRequests_SlotTakenMeanwhile_CannotBeApproved_ButCanBeRejected()
    {
        var (w, id) = await AcceptedRescheduleAsync();
        (await StudentRequestAsync(w.SlotId2, studentUserId: OtherStudentUserId)).Success.ShouldBeTrue("öneri slot tutmaz");

        var item = (await WithServiceAsync(s => s.GetParentRescheduleRequestsAsync(ParentUserId))).Items.Single();
        item.Id.ShouldBe(id);
        item.CanApprove.ShouldBeFalse();

        var approve = await WithServiceAsync(s => s.ParentApproveRescheduleAsync(ParentUserId, id, w.SlotId2));
        approve.Conflict.ShouldBeTrue();
        approve.ErrorCode.ShouldBe(BookingErrorCodes.ProposedSlotUnavailable);
        (await BookingAsync(id)).AvailabilitySlotId.ShouldBe(w.SlotId);

        (await WithServiceAsync(s => s.ParentRejectRescheduleAsync(ParentUserId, id, w.SlotId2))).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task ParentRequests_ListsTheLessonForEveryActiveParent_WithBothTimes()
    {
        var (w, id) = await AcceptedRescheduleAsync();

        foreach (var parent in new[] { ParentUserId, SecondParentUserId })
        {
            var item = (await WithServiceAsync(s => s.GetParentRescheduleRequestsAsync(parent))).Items.Single();
            item.Id.ShouldBe(id);
            item.StudentId.ShouldBe(w.StudentId);
            item.CanApprove.ShouldBeTrue();
            item.ProposedStartUtc.ShouldBeGreaterThan(item.StartUtc);
            item.ProposedByRole.ShouldBe(BookingParty.Student);
        }
    }

    [Fact]
    public async Task ParentApproveRescheduleAsync_WithinTheNoticeWindow_ReturnsRescheduleWindowClosed()
    {
        var (w, id) = await AcceptedRescheduleAsync();
        _clock.Now = LessonStartUtc.AddHours(-1);

        var result = await WithServiceAsync(s => s.ParentApproveRescheduleAsync(ParentUserId, id, w.SlotId2));

        result.ErrorCode.ShouldBe(BookingErrorCodes.RescheduleWindowClosed);
        (await BookingAsync(id)).AvailabilitySlotId.ShouldBe(w.SlotId);
    }

    [Fact]
    public async Task ParentDecision_ProposalChangedAfterTheParentSawIt_ReturnsNoPendingReschedule()
    {
        var (w, id) = await AcceptedRescheduleAsync();
        (await WithServiceAsync(s => s.RespondRescheduleAsync(StudentUserId, id, false))).Success.ShouldBeTrue();
        (await WithServiceAsync(s => s.ProposeRescheduleAsync(StudentUserId, id, w.SlotId3))).Success.ShouldBeTrue();
        (await WithServiceAsync(s => s.RespondRescheduleAsync(TeacherUserId, id, true))).Success.ShouldBeTrue();

        (await WithServiceAsync(s => s.ParentApproveRescheduleAsync(ParentUserId, id, w.SlotId2))).ErrorCode
            .ShouldBe(BookingErrorCodes.NoPendingReschedule);
        (await WithServiceAsync(s => s.ParentRejectRescheduleAsync(ParentUserId, id, w.SlotId2))).ErrorCode
            .ShouldBe(BookingErrorCodes.NoPendingReschedule);

        var stored = await BookingAsync(id);
        stored.AvailabilitySlotId.ShouldBe(w.SlotId);
        stored.ProposedSlotId.ShouldBe(w.SlotId3);
        (await WithServiceAsync(s => s.GetParentRescheduleRequestsAsync(ParentUserId))).Items.Single().ProposedSlotId.ShouldBe(w.SlotId3);
    }

    [Fact]
    public async Task RespondRescheduleAsync_ParentApprovedLessonWithoutActiveParent_DoesNotMoveWithoutApproval()
    {
        var w = await SeedAsync();
        var created = await StudentRequestAsync(w.SlotId, requiresParent: true);
        var id = created.ObjectId;
        (await WithServiceAsync(s => s.ParentApproveBookingAsync(ParentUserId, id))).Success.ShouldBeTrue();
        (await WithServiceAsync(s => s.ApproveBookingAsync(TeacherUserId, id))).Success.ShouldBeTrue();
        (await WithServiceAsync(s => s.ProposeRescheduleAsync(StudentUserId, id, w.SlotId2))).Success.ShouldBeTrue();
        await RemoveParentLinksAsync(w.StudentId);

        var result = await WithServiceAsync(s => s.RespondRescheduleAsync(TeacherUserId, id, true));

        result.Conflict.ShouldBeTrue();
        result.ErrorCode.ShouldBe(BookingErrorCodes.RescheduleParentRequired);
        (await BookingAsync(id)).AvailabilitySlotId.ShouldBe(w.SlotId);
        (await WithServiceAsync(s => s.CancelBookingAsync(StudentUserId, id, null))).Success.ShouldBeTrue("ders iptal edilebilir");
    }

    [Fact]
    public async Task RespondRescheduleAsync_WithdrawWhileWaiting_MarksTheEventForParents_ButAnOrdinaryWithdrawDoesNot()
    {
        var (w, id) = await AcceptedRescheduleAsync();
        (await WithServiceAsync(s => s.RespondRescheduleAsync(StudentUserId, id, false))).Success.ShouldBeTrue();
        (await LifecycleEventsAsync()).Single(x => x.Kind == BookingLifecycleKinds.RescheduleWithdrawn).WasAwaitingParent.ShouldBeTrue();

        (await WithServiceAsync(s => s.ProposeRescheduleAsync(StudentUserId, id, w.SlotId3))).Success.ShouldBeTrue();
        (await WithServiceAsync(s => s.RespondRescheduleAsync(StudentUserId, id, false))).Success.ShouldBeTrue();
        (await LifecycleEventsAsync()).Count(x => x.Kind == BookingLifecycleKinds.RescheduleWithdrawn && !x.WasAwaitingParent).ShouldBe(1);
    }

    [Fact]
    public async Task ParentRequests_LessonAlreadyEnded_LeavesTheList()
    {
        var (_, _) = await AcceptedRescheduleAsync();
        _clock.Now = LessonStartUtc.AddDays(1);

        (await WithServiceAsync(s => s.GetParentRescheduleRequestsAsync(ParentUserId))).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task ParentApproveRescheduleAsync_PaymentGateRefuses_LeavesTheLessonInPlace()
    {
        var (w, id) = await AcceptedRescheduleAsync();
        var gate = Substitute.For<IBookingParentApprovalGate>();
        gate.AuthorizeAsync(Arg.Any<BookingParentApprovalContext>(), Arg.Any<CancellationToken>())
            .Returns(new BookingParentApprovalGateResult(false, "PaymentRequired", "booking.parent.paymentRequired"));

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx, parentApprovalGate: gate).ParentApproveRescheduleAsync(ParentUserId, id, w.SlotId2);

        result.Success.ShouldBeFalse();
        result.ErrorCode.ShouldBe("PaymentRequired");
        await gate.Received(1).AuthorizeAsync(
            Arg.Is<BookingParentApprovalContext>(c => c.Kind == BookingParentApprovalKind.ApproveReschedule
                && c.BookingId == id && c.AvailabilitySlotId == w.SlotId2),
            Arg.Any<CancellationToken>());
        (await BookingAsync(id)).AvailabilitySlotId.ShouldBe(w.SlotId);
        (await RescheduleAuditsAsync()).ShouldBeEmpty();
    }
}
