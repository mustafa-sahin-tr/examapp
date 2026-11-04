using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Bookings;
using ExamApp.Api.Services.Parents;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.Bookings;

/// <summary>
/// issue #486: Active velisi olan öğrencinin randevusunda karşı tarafın kabul ettiği erteleme veli onayına düşer
/// (<see cref="Booking.RescheduleAcceptedAt"/>). Randevu o zamana kadar eski slotta kalır; öneri slotu tutmaz, onay anında kilit
/// altında yeniden doğrulanır. Veli reddederse öneri düşer, randevu yerinde kalır. Bağlantı onay beklerken koparsa taraflar öneriyi
/// geri çekebilir ya da reddedebilir.
/// </summary>
public partial class BookingService
{
    /// <summary>Erteleme kararını veren veli (<see cref="BookingLifecycleEvent.ActorRole"/>).</summary>
    private const string ParentActor = "Parent";

    public async Task<ParentRescheduleRequestListDto> GetParentRescheduleRequestsAsync(int parentUserId, CancellationToken ct = default)
    {
        var studentIds = await _context.ParentStudentLinks
            .AsNoTracking()
            .Where(l => l.Parent.UserId == parentUserId)
            .Where(ParentChildAccess.GrantsAccess)
            .Select(l => l.StudentId)
            .Distinct()
            .ToListAsync(ct);
        if (studentIds.Count == 0)
            return new ParentRescheduleRequestListDto();

        var now = UtcNow();
        var rows = await _context.Bookings
            .WithSoftDeletedSlots()
            .AsNoTracking()
            .Where(b => studentIds.Contains(b.StudentId) && b.Status == BookingStatus.Approved
                        && b.RescheduleAcceptedAt != null && b.ProposedSlotId != null)
            // Ders eski saatinde yapıldıysa öneri artık karar beklemez, liste birikmez.
            .Where(SlotTimeRange.BookingNotEndedAt(now))
            .OrderBy(b => b.AvailabilitySlot.Date)
            .ThenBy(b => b.AvailabilitySlot.StartTime)
            .ThenBy(b => b.Id)
            .Take(MaxTake)
            .Select(b => new
            {
                b.Id,
                b.StudentId,
                StudentUserId = b.Student.UserId,
                b.TeacherId,
                TeacherUserId = b.Teacher.UserId,
                b.AvailabilitySlot.Date,
                b.AvailabilitySlot.StartTime,
                b.AvailabilitySlot.EndTime,
                ProposedDate = b.ProposedSlot!.Date,
                ProposedStartTime = b.ProposedSlot.StartTime,
                ProposedEndTime = b.ProposedSlot.EndTime,
                ProposedSlotDeleted = b.ProposedSlot.IsDeleted,
                ProposedSlotTaken = _context.Bookings.Any(o => o.AvailabilitySlotId == b.ProposedSlotId && o.Id != b.Id
                                                               && ActiveStatuses.Contains(o.Status)),
                ProposedSlotId = b.ProposedSlotId!.Value,
                b.ProposedByRole
            })
            .ToListAsync(ct);

        var names = await ResolveUserNamesAsync(rows.SelectMany(r => new[] { r.StudentUserId, r.TeacherUserId }), ct);

        return new ParentRescheduleRequestListDto
        {
            Items = rows.Select(r =>
            {
                var current = SlotTimeRange.From(r.Date, r.StartTime, r.EndTime);
                var proposed = SlotTimeRange.From(r.ProposedDate, r.ProposedStartTime, r.ProposedEndTime);
                return new ParentRescheduleRequestDto
                {
                    Id = r.Id,
                    StudentId = r.StudentId,
                    StudentName = names.TryGetValue(r.StudentUserId, out var sn) ? sn : null,
                    TeacherId = r.TeacherId,
                    TeacherName = names.TryGetValue(r.TeacherUserId, out var tn) ? tn : null,
                    StartUtc = current.StartUtc,
                    EndUtc = current.EndUtc,
                    ProposedSlotId = r.ProposedSlotId,
                    ProposedStartUtc = proposed.StartUtc,
                    ProposedEndUtc = proposed.EndUtc,
                    ProposedByRole = r.ProposedByRole,
                    CanApprove = !r.ProposedSlotDeleted && !r.ProposedSlotTaken
                        && proposed.StartUtc >= now.AddHours(Policy.RescheduleMinNoticeHours)
                        && now <= current.StartUtc.AddHours(-Policy.RescheduleMinNoticeHours)
                };
            }).ToList()
        };
    }

    public Task<BookingResultDto> ParentApproveRescheduleAsync(int parentUserId, int bookingId, int proposedSlotId, CancellationToken ct = default)
        => ParentDecideRescheduleAsync(parentUserId, bookingId, proposedSlotId, approve: true, ct);

    public Task<BookingResultDto> ParentRejectRescheduleAsync(int parentUserId, int bookingId, int proposedSlotId, CancellationToken ct = default)
        => ParentDecideRescheduleAsync(parentUserId, bookingId, proposedSlotId, approve: false, ct);

    /// <param name="seenSlotId">Velinin ekranda gördüğü önerilen slot: öneri o arada değiştiyse karar geçmez (tarih/saat onayın parçası).</param>
    private async Task<BookingResultDto> ParentDecideRescheduleAsync(int parentUserId, int bookingId, int seenSlotId, bool approve, CancellationToken ct)
    {
        // Yetki önce: randevu yoksa ya da çağıranın bu öğrenciyle Active bağlantısı yoksa aynı 404 (varlık sızdırılmaz).
        var booking = await QueryLifecycleBookingAsync(bookingId, ct);
        if (booking == null)
            return ParentChildNotFound();
        var link = await FindActiveChildLinkAsync(parentUserId, booking.StudentId, ct);
        if (link == null)
            return ParentChildNotFound();

        if (booking.Status != BookingStatus.Approved || booking.ProposedSlotId is not int proposedSlotId
            || proposedSlotId != seenSlotId || booking.RescheduleAcceptedAt == null)
            return LifecycleFail(BookingErrorCodes.NoPendingReschedule, "booking.reschedule.noPending", conflict: true);

        var slot = await LoadProposedSlotAsync(proposedSlotId, ct);
        var now = UtcNow();

        void Audit() => _context.ParentAccessAudits.Add(new ParentAccessAudit
        {
            ParentId = link.ParentId,
            StudentId = booking.StudentId,
            Endpoint = approve ? ParentAccessEndpoints.ChildRescheduleApprove : ParentAccessEndpoints.ChildRescheduleReject,
            ResourceId = booking.Id,
            At = now
        });

        // Kilit sırası veli onayıyla aynı: müsaitlik → öğretmen → veli bağlantıları. Bağlantı az önce koptuysa karar yok.
        var linkGone = false;
        async Task<bool> LinkStillActiveAsync()
        {
            linkGone = !(await ParentNotificationSupport.ActiveParentIdsLockedAsync(_context, booking.StudentId, ct)).Contains(link.ParentId);
            return !linkGone;
        }

        void Configure(BookingLifecycleEvent e)
        {
            if (slot != null)
                SetNewSlot(e, slot.Date, slot.StartTime, slot.EndTime);
            e.NotifyTeacher = true;
            e.NotifyStudent = true;
            e.ActorParentUserId = parentUserId;
        }

        if (!approve)
        {
            var written = await WriteLifecycleAsync(booking, parentUserId, ParentActor, BookingLifecycleKinds.RescheduleParentRejected,
                update: async q =>
                {
                    if (!await LinkStillActiveAsync())
                        return 0;

                    var affected = await ClearProposalAsync(
                        q.Where(b => b.Status == BookingStatus.Approved && b.ProposedSlotId == proposedSlotId && b.RescheduleAcceptedAt != null),
                        parentUserId, now, ct);
                    if (affected > 0)
                        Audit();
                    return affected;
                },
                Configure, now, ct);
            if (linkGone)
                return ParentChildNotFound();
            if (!written)
                return LifecycleFail(BookingErrorCodes.NoPendingReschedule, "booking.reschedule.noPending", conflict: true);

            _logger.LogInformation("Erteleme veli tarafından reddedildi. BookingId={BookingId}", bookingId);
            return await LifecycleSuccessAsync(bookingId, "booking.reschedule.parentRejected", ct);
        }

        var windowFailure = RescheduleWindowFailure(booking, slot, now);
        if (windowFailure != null)
            return windowFailure;

        // Ödeme kancası (#408): onay durum değişikliğinden ÖNCE tek kapıdan geçer.
        var gate = await _parentApprovalGate.AuthorizeAsync(new BookingParentApprovalContext(
            BookingParentApprovalKind.ApproveReschedule, booking.Id, booking.StudentId, booking.TeacherId,
            proposedSlotId, parentUserId), ct);
        if (!gate.Allowed)
            return GateFailure(gate);

        var failure = await WriteProposedSlotAsync(booking, parentUserId, ParentActor, BookingLifecycleKinds.RescheduleParentApproved,
            proposedSlotId,
            apply: async q =>
            {
                if (!await LinkStillActiveAsync())
                    return 0;

                var affected = await MoveToProposedSlotAsync(
                    q.Where(b => b.RescheduleAcceptedAt != null), proposedSlotId, parentUserId, now, ct);
                if (affected > 0)
                    Audit();
                return affected;
            },
            Configure, now, ct);

        if (linkGone)
            return ParentChildNotFound();
        if (failure != null)
            return failure;

        _logger.LogInformation("Erteleme veli tarafından onaylandı. BookingId={BookingId}", bookingId);
        return await LifecycleSuccessAsync(bookingId, "booking.reschedule.parentApproved", ct);
    }
}
