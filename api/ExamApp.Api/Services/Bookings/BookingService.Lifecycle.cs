using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos.Bookings;
using ExamApp.Api.Services.Parents;
using ExamApp.Api.Services.Whiteboard;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.Bookings;

/// <summary>
/// issue #315: iptal, erteleme ve no-show. Kurallar <see cref="BookingPolicyOptions"/>'tan gelir.
/// <list type="bullet">
/// <item><b>İptal:</b> öğrenci Pending/AwaitingParent talebini her zaman geri çekebilir; onaylı randevuyu öğrenci ya da öğretmen
/// başlangıca <c>CancellationMinNoticeHours</c> kalana kadar iptal edebilir (öğretmen Pending talebi zaten reddeder).
/// Durum <see cref="BookingStatus.Cancelled"/> olur, slot müsaitliğe döner (filtreli unique index dışı), bekleyen erteleme
/// önerisi düşer, açık tahta kapanır; görüşme odası Approved şartıyla zaten kapanır.</item>
/// <item><b>Erteleme:</b> onaylı randevuda taraflardan biri aynı öğretmenin boş, gelecekteki, AYNI SÜRELİ bir slotunu önerir
/// (velinin onayladığı dersin süresi değişmez; veliler öneriden haberdar edilir); karşı taraf kabul edene kadar randevu eski
/// slotta kalır (öneri slotu tutmaz). Öneri sayısı (<c>MaxProposalsPerBooking</c>) ve kabul sayısı sınırlıdır. Kabulde öğretmen müsaitlik kilidi altında slotun hâlâ boş
/// olduğu doğrulanır ve randevu yeni slota taşınır. Öneren taraf önerisini geri çekebilir.</item>
/// <item><b>No-show:</b> ders bitişinden sonra <c>NoShowReportWindowDays</c> içinde taraflardan biri karşı tarafı "katılmadı"
/// olarak işaretler; ilk bildirim kalıcıdır. Durum Approved kalır.</item>
/// </list>
/// Her işlem koşullu UPDATE + outbox (<see cref="BookingLifecycleEvent"/>) ile tek transaction'da yazılır; arada başka bir yoldan
/// değişen randevuda (0 satır) event yazılmaz.
/// </summary>
public partial class BookingService
{
    private BookingPolicyOptions Policy => _policyOptions.Value;

    private sealed record LifecycleBooking(
        int Id,
        BookingStatus Status,
        BookingStatus? CancelledFromStatus,
        int TeacherId,
        int TeacherUserId,
        int StudentId,
        int StudentUserId,
        int AvailabilitySlotId,
        DateOnly Date,
        TimeOnly StartTime,
        TimeOnly EndTime,
        int? ProposedSlotId,
        string? ProposedByRole,
        int RescheduleCount,
        int RescheduleProposalCount,
        string? NoShowParty)
    {
        public SlotTimeRange Range => SlotTimeRange.From(Date, StartTime, EndTime);
    }

    /// <summary>
    /// Randevuyu çağıranın tarafıyla yükler. Taraf değilse ya da öğretmene hiç ulaşmamış talepse (AwaitingParent / Expired /
    /// ParentRejected / AwaitingParent'tan iptal) öğretmen için 404 — varlık sızdırılmaz.
    /// </summary>
    private async Task<(LifecycleBooking? Booking, string? Party, BookingResultDto? Failure)> LoadLifecycleBookingAsync(
        int callerUserId, int bookingId, CancellationToken ct)
    {
        var booking = await _context.Bookings
            .WithSoftDeletedSlots()
            .AsNoTracking()
            .Where(b => b.Id == bookingId)
            .Select(b => new LifecycleBooking(
                b.Id, b.Status, b.CancelledFromStatus, b.TeacherId, b.Teacher.UserId, b.StudentId, b.Student.UserId,
                b.AvailabilitySlotId, b.AvailabilitySlot.Date, b.AvailabilitySlot.StartTime, b.AvailabilitySlot.EndTime,
                b.ProposedSlotId, b.ProposedByRole, b.RescheduleCount, b.RescheduleProposalCount, b.NoShowParty))
            .FirstOrDefaultAsync(ct);

        string? party = booking == null ? null
            : booking.StudentUserId == callerUserId ? BookingParty.Student
            : booking.TeacherUserId == callerUserId ? BookingParty.Teacher
            : null;

        if (booking == null || party == null || (party == BookingParty.Teacher && !IsTeacherVisible(booking.Status, booking.CancelledFromStatus)))
            return (null, null, LifecycleFail(BookingErrorCodes.NotFound, "booking.request.notFound", notFound: true));

        return (booking, party, null);
    }

    /// <summary>Öğretmenin görebildiği randevu: Pending/Approved/Rejected ya da öğretmene ulaşmış talebin iptali.</summary>
    internal static bool IsTeacherVisible(BookingStatus status, BookingStatus? cancelledFrom)
        => TeacherVisibleStatuses.Contains(status)
           || (status == BookingStatus.Cancelled && cancelledFrom != BookingStatus.AwaitingParent);

    private static string OtherParty(string party) => party == BookingParty.Teacher ? BookingParty.Student : BookingParty.Teacher;

    private BookingResultDto LifecycleFail(string errorCode, string messageKey, bool notFound = false, bool forbidden = false,
        bool conflict = false, object? arg = null) => new()
    {
        Success = false,
        NotFound = notFound,
        Forbidden = forbidden,
        Conflict = conflict,
        ErrorCode = errorCode,
        Message = arg == null ? _localizer[messageKey] : _localizer[messageKey, arg]
    };

    /// <summary>Filtreli unique index ihlali (ExecuteUpdate sağlayıcı istisnasını sarmadan fırlatabilir).</summary>
    private static bool IsUniqueViolation(Exception ex)
        => ex is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation }
           || ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation };

    // ------------------------------------------------------------------
    // İptal
    // ------------------------------------------------------------------

    public async Task<BookingResultDto> CancelBookingAsync(int callerUserId, int bookingId, string? reason, CancellationToken ct = default)
    {
        if (!BookingParentApproval.TrySanitizeNote(reason, out var storedReason))
            return LifecycleFail(BookingErrorCodes.InvalidNote, "booking.cancel.reasonInvalid");

        var (booking, party, failure) = await LoadLifecycleBookingAsync(callerUserId, bookingId, ct);
        if (failure != null)
            return failure;

        // Öğrenci: henüz onaylanmamış talebi geri çeker ya da onaylı randevuyu iptal eder. Öğretmen: yalnız onaylı randevu
        // (Pending talep için ret ucu var).
        var cancellable = party == BookingParty.Student
            ? booking!.Status is BookingStatus.Pending or BookingStatus.Approved or BookingStatus.AwaitingParent
            : booking!.Status == BookingStatus.Approved;
        if (!cancellable)
            return LifecycleFail(BookingErrorCodes.InvalidState, "booking.cancel.invalidState", conflict: true);

        var now = UtcNow();
        if (booking.Status == BookingStatus.Approved
            && now > booking.Range.StartUtc.AddHours(-Policy.CancellationMinNoticeHours))
            return LifecycleFail(BookingErrorCodes.CancellationWindowClosed, "booking.cancel.windowClosed", conflict: true,
                arg: Policy.CancellationMinNoticeHours);

        var previous = booking.Status;
        var bookingStatus = booking.Status;
        var slotId = booking.AvailabilitySlotId;
        var written = await WriteLifecycleAsync(booking, callerUserId, party!, BookingLifecycleKinds.Cancelled,
            update: q => q
                // Slot koşulu (security review D2): arada kabul edilen erteleme randevuyu taşıdıysa bildirim süresi eski
                // başlangıca göre doğrulanmış olur → 0 satır, kullanıcı yeniden dener.
                .Where(b => b.Status == bookingStatus && b.AvailabilitySlotId == slotId)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(b => b.Status, BookingStatus.Cancelled)
                    .SetProperty(b => b.CancelledAt, now)
                    .SetProperty(b => b.CancelledByUserId, callerUserId)
                    .SetProperty(b => b.CancelledByRole, party)
                    .SetProperty(b => b.CancellationReason, storedReason)
                    .SetProperty(b => b.CancelledFromStatus, previous)
                    .SetProperty(b => b.ProposedSlotId, (int?)null)
                    .SetProperty(b => b.ProposedByRole, (string?)null)
                    .SetProperty(b => b.ProposedByUserId, (int?)null)
                    .SetProperty(b => b.ProposedAt, (DateTime?)null)
                    .SetProperty(b => b.UpdateTime, now)
                    .SetProperty(b => b.UpdateUserId, callerUserId), ct),
            configure: e =>
            {
                e.Reason = storedReason;
                // Öğretmene hiç ulaşmamış (veli onayı bekleyen) talebin geri çekilmesi öğretmene bildirilmez.
                e.NotifyTeacher = party == BookingParty.Student && previous != BookingStatus.AwaitingParent;
                e.NotifyStudent = party == BookingParty.Teacher;
            },
            now, ct);

        if (!written)
            return LifecycleFail(BookingErrorCodes.InvalidState, "booking.cancel.invalidState", conflict: true);

        _logger.LogInformation("Randevu iptal edildi. BookingId={BookingId}, By={Party}, PreviousStatus={Previous}",
            bookingId, party, previous);

        // Açık tahta hemen kapanır (temizlik turunu beklemeden); görüşme odası yeni token vermez (Approved şartı).
        if (previous == BookingStatus.Approved && _whiteboardCloser != null)
            await CloseWhiteboardQuietlyAsync(bookingId, ct);

        return await LifecycleSuccessAsync(bookingId, "booking.cancel.done", ct);
    }

    // ------------------------------------------------------------------
    // Erteleme
    // ------------------------------------------------------------------

    public async Task<BookingResultDto> ProposeRescheduleAsync(int callerUserId, int bookingId, int newSlotId, CancellationToken ct = default)
    {
        var (booking, party, failure) = await LoadLifecycleBookingAsync(callerUserId, bookingId, ct);
        if (failure != null)
            return failure;

        if (booking!.Status != BookingStatus.Approved)
            return LifecycleFail(BookingErrorCodes.InvalidState, "booking.reschedule.invalidState", conflict: true);
        if (booking.ProposedSlotId != null)
            return LifecycleFail(BookingErrorCodes.RescheduleAlreadyProposed, "booking.reschedule.alreadyProposed", conflict: true);
        if (booking.RescheduleCount >= Policy.MaxReschedulesPerBooking)
            return LifecycleFail(BookingErrorCodes.RescheduleLimitReached, "booking.reschedule.limitReached", conflict: true,
                arg: Policy.MaxReschedulesPerBooking);
        if (booking.RescheduleProposalCount >= Policy.MaxProposalsPerBooking)
            return LifecycleFail(BookingErrorCodes.RescheduleProposalLimitReached, "booking.reschedule.proposalLimitReached",
                conflict: true, arg: Policy.MaxProposalsPerBooking);

        var now = UtcNow();
        if (now > booking.Range.StartUtc.AddHours(-Policy.RescheduleMinNoticeHours))
            return LifecycleFail(BookingErrorCodes.RescheduleWindowClosed, "booking.reschedule.windowClosed", conflict: true,
                arg: Policy.RescheduleMinNoticeHours);

        // Aynı öğretmenin silinmemiş, gelecekteki, mevcut slottan farklı slotu (soft-delete filtresi açık).
        var slot = await _context.TeacherAvailabilitySlots
            .AsNoTracking()
            .Where(s => s.Id == newSlotId && s.TeacherId == booking.TeacherId)
            .Select(s => new { s.Id, s.Date, s.StartTime, s.EndTime })
            .FirstOrDefaultAsync(ct);
        // Yeni slot da en az bildirim süresi kadar ileride olmalı (code review U1): aksi halde ders birkaç dakika sonrasına
        // taşınıp iptal penceresi hemen kapanırdı.
        if (slot == null || slot.Id == booking.AvailabilitySlotId
            || ToUtc(slot.Date, slot.StartTime) < now.AddHours(Policy.RescheduleMinNoticeHours))
            return LifecycleFail(BookingErrorCodes.ProposedSlotUnavailable, "booking.reschedule.slotUnavailable");

        // Süre aynı kalmalı (security review O1): veli belirli bir dersi onayladı; erteleme yalnız zamanı değiştirir, süreyi
        // (ve #408 ile ücreti) değiştirmez.
        var newRange = SlotTimeRange.From(slot.Date, slot.StartTime, slot.EndTime);
        if (newRange.EndUtc - newRange.StartUtc != booking.Range.EndUtc - booking.Range.StartUtc)
            return LifecycleFail(BookingErrorCodes.ProposedSlotDurationMismatch, "booking.reschedule.durationMismatch");

        if (await _context.Bookings.AsNoTracking().AnyAsync(b => b.AvailabilitySlotId == slot.Id && ActiveStatuses.Contains(b.Status), ct))
            return LifecycleFail(BookingErrorCodes.ProposedSlotUnavailable, "booking.reschedule.slotUnavailable", conflict: true);

        var count = booking.RescheduleCount;
        var proposals = booking.RescheduleProposalCount;
        var written = await WriteLifecycleAsync(booking, callerUserId, party!, BookingLifecycleKinds.RescheduleProposed,
            update: q => q
                .Where(b => b.Status == BookingStatus.Approved && b.ProposedSlotId == null && b.RescheduleCount == count
                            && b.RescheduleProposalCount == proposals)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(b => b.RescheduleProposalCount, proposals + 1)
                    .SetProperty(b => b.ProposedSlotId, slot.Id)
                    .SetProperty(b => b.ProposedByRole, party)
                    .SetProperty(b => b.ProposedByUserId, callerUserId)
                    .SetProperty(b => b.ProposedAt, now)
                    .SetProperty(b => b.UpdateTime, now)
                    .SetProperty(b => b.UpdateUserId, callerUserId), ct),
            configure: e =>
            {
                SetNewSlot(e, slot.Date, slot.StartTime, slot.EndTime);
                NotifyCounterparty(e, party!);
            },
            now, ct);

        if (!written)
            return LifecycleFail(BookingErrorCodes.RescheduleAlreadyProposed, "booking.reschedule.alreadyProposed", conflict: true);

        _logger.LogInformation("Erteleme önerildi. BookingId={BookingId}, By={Party}, ProposedSlotId={SlotId}", bookingId, party, slot.Id);
        return await LifecycleSuccessAsync(bookingId, "booking.reschedule.proposed", ct);
    }

    public async Task<BookingResultDto> RespondRescheduleAsync(int callerUserId, int bookingId, bool accept, CancellationToken ct = default)
    {
        var (booking, party, failure) = await LoadLifecycleBookingAsync(callerUserId, bookingId, ct);
        if (failure != null)
            return failure;

        if (booking!.Status != BookingStatus.Approved || booking.ProposedSlotId is not int proposedSlotId)
            return LifecycleFail(BookingErrorCodes.NoPendingReschedule, "booking.reschedule.noPending", conflict: true);

        var ownProposal = booking.ProposedByRole == party;
        if (ownProposal && accept)
            return LifecycleFail(BookingErrorCodes.CannotRespondOwnProposal, "booking.reschedule.ownProposal", forbidden: true);

        // Silinmiş slot da okunur (olay metni için); kabulde ayrıca kilit altında "silinmemiş + boş" doğrulanır.
        var slot = await _context.TeacherAvailabilitySlots
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.Id == proposedSlotId)
            .Select(s => new { s.Id, s.Date, s.StartTime, s.EndTime })
            .FirstOrDefaultAsync(ct);

        var now = UtcNow();
        string kind;
        if (accept)
        {
            if (now > booking.Range.StartUtc.AddHours(-Policy.RescheduleMinNoticeHours))
                return LifecycleFail(BookingErrorCodes.RescheduleWindowClosed, "booking.reschedule.windowClosed", conflict: true,
                    arg: Policy.RescheduleMinNoticeHours);
            if (slot == null || ToUtc(slot.Date, slot.StartTime) < now.AddHours(Policy.RescheduleMinNoticeHours))
                return LifecycleFail(BookingErrorCodes.ProposedSlotUnavailable, "booking.reschedule.slotUnavailable", conflict: true);
            kind = BookingLifecycleKinds.RescheduleAccepted;
        }
        else
        {
            kind = ownProposal ? BookingLifecycleKinds.RescheduleWithdrawn : BookingLifecycleKinds.RescheduleDeclined;
        }

        var slotTaken = false;
        var teacherUnavailable = false;
        bool written;
        try
        {
            written = await WriteLifecycleAsync(booking, callerUserId, party!, kind,
                update: async q =>
                {
                    // Execution strategy yeniden denerse önceki denemenin bayrakları taşınmasın.
                    slotTaken = false;
                    teacherUnavailable = false;
                    if (!accept)
                    {
                        return await q
                            .Where(b => b.Status == BookingStatus.Approved && b.ProposedSlotId == proposedSlotId)
                            .ExecuteUpdateAsync(set => set
                                .SetProperty(b => b.ProposedSlotId, (int?)null)
                                .SetProperty(b => b.ProposedByRole, (string?)null)
                                .SetProperty(b => b.ProposedByUserId, (int?)null)
                                .SetProperty(b => b.ProposedAt, (DateTime?)null)
                                .SetProperty(b => b.UpdateTime, now)
                                .SetProperty(b => b.UpdateUserId, callerUserId), ct);
                    }

                    // Slot silme / yeni talep yollarıyla serileşir (#323/#376 öğretmen müsaitlik kilidi): slot hâlâ var ve
                    // başka aktif randevusu yok. Unique index yine son güvencedir (DbUpdateException → 409).
                    await _context.Database.AcquireTeacherAvailabilityLockAsync(booking.TeacherId, ct);
                    var free = await _context.TeacherAvailabilitySlots.AsNoTracking().AnyAsync(s => s.Id == proposedSlotId, ct)
                               && !await _context.Bookings.AsNoTracking().AnyAsync(
                                   b => b.AvailabilitySlotId == proposedSlotId && b.Id != bookingId && ActiveStatuses.Contains(b.Status), ct);
                    if (!free)
                    {
                        slotTaken = true;
                        return 0;
                    }

                    // Öğretmen hâlâ randevu alabilir mi (onaylı, askıda değil, bağımsız — security review D1); talep oluşturmayla
                    // aynı satır kilidi (FOR SHARE), askı ile serileşir.
                    if (!await LockBookableTeacherAsync(booking.TeacherId, ct))
                    {
                        teacherUnavailable = true;
                        return 0;
                    }

                    return await q
                        .Where(b => b.Status == BookingStatus.Approved && b.ProposedSlotId == proposedSlotId)
                        .ExecuteUpdateAsync(set => set
                            .SetProperty(b => b.AvailabilitySlotId, proposedSlotId)
                            .SetProperty(b => b.RescheduleCount, b => b.RescheduleCount + 1)
                            .SetProperty(b => b.ProposedSlotId, (int?)null)
                            .SetProperty(b => b.ProposedByRole, (string?)null)
                            .SetProperty(b => b.ProposedByUserId, (int?)null)
                            .SetProperty(b => b.ProposedAt, (DateTime?)null)
                            .SetProperty(b => b.UpdateTime, now)
                            .SetProperty(b => b.UpdateUserId, callerUserId), ct);
                },
                configure: e =>
                {
                    if (slot != null)
                        SetNewSlot(e, slot.Date, slot.StartTime, slot.EndTime);
                    // Kabul/ret öneren tarafa, geri çekme karşı tarafa bildirilir.
                    NotifyCounterparty(e, party!);
                },
                now, ct);
        }
        catch (TeacherAvailabilityLockTimeoutException ex)
        {
            _logger.LogWarning(ex, "Erteleme kabulü: öğretmen müsaitlik kilidi zaman aşımı. BookingId={BookingId}", bookingId);
            return new BookingResultDto { Success = false, Conflict = true, Message = _localizer["booking.slot.busy"] };
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            // Filtreli unique index: önerilen slot aynı anda başka bir talebe gitti.
            _logger.LogInformation(ex, "Erteleme kabulü: önerilen slot dolu (unique index). BookingId={BookingId}", bookingId);
            slotTaken = true;
            written = false;
        }

        if (slotTaken)
            return LifecycleFail(BookingErrorCodes.ProposedSlotUnavailable, "booking.reschedule.slotUnavailable", conflict: true);
        if (teacherUnavailable)
            return LifecycleFail(BookingErrorCodes.TeacherUnavailable, "booking.reschedule.teacherUnavailable", conflict: true);
        if (!written)
            return LifecycleFail(BookingErrorCodes.NoPendingReschedule, "booking.reschedule.noPending", conflict: true);

        _logger.LogInformation("Erteleme yanıtlandı. BookingId={BookingId}, By={Party}, Kind={Kind}", bookingId, party, kind);
        return await LifecycleSuccessAsync(bookingId, kind switch
        {
            BookingLifecycleKinds.RescheduleAccepted => "booking.reschedule.accepted",
            BookingLifecycleKinds.RescheduleWithdrawn => "booking.reschedule.withdrawn",
            _ => "booking.reschedule.declined"
        }, ct);
    }

    // ------------------------------------------------------------------
    // No-show
    // ------------------------------------------------------------------

    public async Task<BookingResultDto> ReportNoShowAsync(int callerUserId, int bookingId, CancellationToken ct = default)
    {
        var (booking, party, failure) = await LoadLifecycleBookingAsync(callerUserId, bookingId, ct);
        if (failure != null)
            return failure;

        if (booking!.Status != BookingStatus.Approved)
            return LifecycleFail(BookingErrorCodes.InvalidState, "booking.noShow.invalidState", conflict: true);
        if (booking.NoShowParty != null)
            return LifecycleFail(BookingErrorCodes.NoShowAlreadyReported, "booking.noShow.alreadyReported", conflict: true);

        var now = UtcNow();
        var end = booking.Range.EndUtc;
        if (now < end || now > end.AddDays(Policy.NoShowReportWindowDays))
            return LifecycleFail(BookingErrorCodes.NoShowWindowClosed, "booking.noShow.windowClosed", conflict: true,
                arg: Policy.NoShowReportWindowDays);

        var absent = OtherParty(party!);
        var noShowSlotId = booking.AvailabilitySlotId;
        var written = await WriteLifecycleAsync(booking, callerUserId, party!, BookingLifecycleKinds.NoShowReported,
            update: q => q
                .Where(b => b.Status == BookingStatus.Approved && b.NoShowParty == null && b.AvailabilitySlotId == noShowSlotId)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(b => b.NoShowParty, absent)
                    .SetProperty(b => b.NoShowReportedByUserId, callerUserId)
                    .SetProperty(b => b.NoShowReportedAt, now)
                    .SetProperty(b => b.UpdateTime, now)
                    .SetProperty(b => b.UpdateUserId, callerUserId), ct),
            configure: e =>
            {
                e.NoShowParty = absent;
                NotifyCounterparty(e, party!);
            },
            now, ct);

        if (!written)
        {
            // 0 satır: başka bir bildirim önce geldi ya da randevu arada taşındı/iptal edildi — doğru kodu dön.
            var alreadyReported = await _context.Bookings.AsNoTracking()
                .AnyAsync(b => b.Id == bookingId && b.NoShowParty != null, ct);
            return alreadyReported
                ? LifecycleFail(BookingErrorCodes.NoShowAlreadyReported, "booking.noShow.alreadyReported", conflict: true)
                : LifecycleFail(BookingErrorCodes.InvalidState, "booking.noShow.invalidState", conflict: true);
        }

        _logger.LogInformation("No-show bildirildi. BookingId={BookingId}, By={Party}, Absent={Absent}", bookingId, party, absent);
        return await LifecycleSuccessAsync(bookingId, "booking.noShow.reported", ct);
    }

    // ------------------------------------------------------------------
    // Ortak yazım
    // ------------------------------------------------------------------

    private static void SetNewSlot(BookingLifecycleEvent e, DateOnly date, TimeOnly start, TimeOnly end)
    {
        e.NewDate = date;
        e.NewStartTime = start;
        e.NewEndTime = end;
    }

    private static void NotifyCounterparty(BookingLifecycleEvent e, string actorParty)
    {
        e.NotifyTeacher = actorParty == BookingParty.Student;
        e.NotifyStudent = actorParty == BookingParty.Teacher;
    }

    /// <summary>
    /// Koşullu UPDATE (<paramref name="update"/>, randevu id'si ile önceden filtrelenmiş sorgu alır; etkilenen satır sayısını
    /// döner) + öğrencinin Active velileri (FOR SHARE) + <see cref="BookingLifecycleEvent"/> outbox satırı tek transaction'da.
    /// 0 satır → rollback, event yok, false.
    /// </summary>
    private async Task<bool> WriteLifecycleAsync(
        LifecycleBooking booking,
        int callerUserId,
        string party,
        string kind,
        Func<IQueryable<Booking>, Task<int>> update,
        Action<BookingLifecycleEvent> configure,
        DateTime now,
        CancellationToken ct)
    {
        // Bildirim alanları (ad + sub) best-effort, yazımdan önce (DecideAsync deseni).
        var parents = await ParentNotificationSupport.ActiveParentsAsync(_context, booking.StudentId, ct);
        var users = await TryLookupUsersAsync(
            new[] { booking.TeacherUserId, booking.StudentUserId }.Concat(parents.Select(p => p.ParentUserId)),
            "Randevu " + kind, booking.Id, ct);

        var lifecycleEvent = new BookingLifecycleEvent
        {
            EventId = Guid.NewGuid(),
            BookingId = booking.Id,
            Kind = kind,
            ActorRole = party,
            TeacherId = booking.TeacherId,
            TeacherUserId = booking.TeacherUserId,
            TeacherName = FullNameOf(users, booking.TeacherUserId),
            TeacherKeycloakId = KeycloakIdOf(users, booking.TeacherUserId),
            StudentId = booking.StudentId,
            StudentUserId = booking.StudentUserId,
            StudentName = FullNameOf(users, booking.StudentUserId),
            StudentKeycloakId = KeycloakIdOf(users, booking.StudentUserId),
            Date = booking.Date,
            StartTime = booking.StartTime,
            EndTime = booking.EndTime,
            OccurredAt = now
        };
        configure(lifecycleEvent);

        _context.SetCurrentUser(callerUserId);

        var affected = 0;
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _context.ChangeTracker.Clear();
            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            affected = await update(_context.Bookings.Where(b => b.Id == booking.Id));
            if (affected == 0)
            {
                await tx.RollbackAsync(ct);
                return;
            }

            var lockedParentIds = await ParentNotificationSupport.ActiveParentIdsLockedAsync(_context, booking.StudentId, ct);
            lifecycleEvent.Parents = BookingParentApproval.Recipients(
                parents.Where(p => lockedParentIds.Contains(p.ParentId)).Select(p => p.ParentUserId),
                id => KeycloakIdOf(users, id));

            AddOutbox(lifecycleEvent, now);
            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        return affected > 0;
    }

    private async Task CloseWhiteboardQuietlyAsync(int bookingId, CancellationToken ct)
    {
        try
        {
            await _whiteboardCloser!.CloseAsync(bookingId, WhiteboardCloseReasons.BookingCancelled, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // İptal yazıldı; tahta en geç temizlik turunda / hub yeniden doğrulamasında kapanır.
            _logger.LogWarning(ex, "İptal sonrası tahta kapatılamadı. BookingId={BookingId}", bookingId);
        }
    }

    private async Task<BookingResultDto> LifecycleSuccessAsync(int bookingId, string messageKey, CancellationToken ct)
    {
        var result = await QueryBookingsAsync(b => b.Id == bookingId, 0, 1, ct);
        return new BookingResultDto
        {
            Success = true,
            ObjectId = bookingId,
            Message = _localizer[messageKey],
            Booking = result.Items.FirstOrDefault()
        };
    }
}
