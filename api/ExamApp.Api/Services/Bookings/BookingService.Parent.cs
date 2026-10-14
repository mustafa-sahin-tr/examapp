using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos.Bookings;
using ExamApp.Api.Services.Parents;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.Bookings;

/// <summary>
/// issue #439 — özel derste veli onayı: velinin onay bekleyen talepleri listelemesi, onaylaması/reddetmesi ve çocuk adına doğrudan
/// talep açması. Yetki: çağıranın talebin öğrencisiyle Active bağlantısı (<see cref="ParentChildAccess.GrantsAccess"/>) — birincil
/// olması gerekmez; aksi her durum 404 (varlık sızdırılmaz). İlk karar kazanır: durum geçişleri koşullu UPDATE
/// (<c>WHERE Status = AwaitingParent</c>); kaybeden 409 <see cref="BookingErrorCodes.AlreadyDecided"/>.
/// </summary>
public partial class BookingService
{
    private sealed record ActiveChildLink(int ParentId, int StudentUserId);

    /// <summary>Velinin (user id) bu öğrenciyle Active bağlantısı (iki taraf da silinmemiş); yoksa null.</summary>
    private Task<ActiveChildLink?> FindActiveChildLinkAsync(int parentUserId, int studentId, CancellationToken ct)
        => _context.ParentStudentLinks
            .AsNoTracking()
            .Where(l => l.Parent.UserId == parentUserId && l.StudentId == studentId)
            .Where(ParentChildAccess.GrantsAccess)
            .OrderBy(l => l.Id)
            .Select(l => new ActiveChildLink(l.ParentId, l.Student.UserId))
            .FirstOrDefaultAsync(ct);

    public async Task<ParentBookingRequestListDto> GetParentBookingRequestsAsync(int parentUserId, CancellationToken ct = default)
    {
        var studentIds = await _context.ParentStudentLinks
            .AsNoTracking()
            .Where(l => l.Parent.UserId == parentUserId)
            .Where(ParentChildAccess.GrantsAccess)
            .Select(l => l.StudentId)
            .Distinct()
            .ToListAsync(ct);
        if (studentIds.Count == 0)
            return new ParentBookingRequestListDto();

        var now = UtcNow();
        // issue #376 deseni: slotu silinmiş talep de görünür. Öğretmen slotu silince (AwaitingParentSlotRelease) talep aynı
        // transaction'da Expired olur ve listeden düşer; bu yalnız eski/kilit dışı veri için savunmadır.
        // Süresi dolmuş ama süpürme işinin henüz kapatmadığı talepler listelenmez (karar verilemez).
        var rows = await _context.Bookings
            .WithSoftDeletedSlots()
            .AsNoTracking()
            .Where(b => studentIds.Contains(b.StudentId) && b.Status == BookingStatus.AwaitingParent)
            .Where(BookingParentApproval.OpenAt(now))
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
                b.AvailabilitySlotId,
                b.AvailabilitySlot.Date,
                b.AvailabilitySlot.StartTime,
                b.AvailabilitySlot.EndTime,
                b.CreatedAt
            })
            .ToListAsync(ct);

        var names = await ResolveUserNamesAsync(rows.SelectMany(r => new[] { r.StudentUserId, r.TeacherUserId }), ct);

        return new ParentBookingRequestListDto
        {
            Items = rows.Select(r =>
            {
                var range = SlotTimeRange.From(r.Date, r.StartTime, r.EndTime);
                return new ParentBookingRequestDto
                {
                    Id = r.Id,
                    StudentId = r.StudentId,
                    StudentName = names.TryGetValue(r.StudentUserId, out var sn) ? sn : null,
                    TeacherId = r.TeacherId,
                    TeacherName = names.TryGetValue(r.TeacherUserId, out var tn) ? tn : null,
                    AvailabilitySlotId = r.AvailabilitySlotId,
                    Date = r.Date,
                    StartTime = r.StartTime,
                    EndTime = r.EndTime,
                    StartUtc = range.StartUtc,
                    EndUtc = range.EndUtc,
                    CreatedAt = r.CreatedAt,
                    ExpiresAt = BookingParentApproval.ExpiresAt(r.CreatedAt, r.Date, r.StartTime)
                };
            }).ToList()
        };
    }

    public async Task<BookingResultDto> CreateBookingForChildAsync(
        int parentUserId, int studentId, CreateBookingDto dto, CancellationToken ct = default)
    {
        var link = await FindActiveChildLinkAsync(parentUserId, studentId, ct);
        if (link == null)
            return ParentChildNotFound();

        // Öğrenci talebiyle aynı çekirdek (slot/öğretmen doğrulaması, kilitler, çakışma); AwaitingParent atlanır.
        return await CreateBookingCoreAsync(new BookingCreation(
            studentId,
            link.StudentUserId,
            dto.AvailabilitySlotId,
            BookingCreationKind.ParentDirect,
            ActorUserId: parentUserId,
            ParentId: link.ParentId), ct);
    }

    public Task<BookingResultDto> ParentApproveBookingAsync(int parentUserId, int bookingId, CancellationToken ct = default)
        => ParentDecideAsync(parentUserId, bookingId, approve: true, note: null, ct);

    public Task<BookingResultDto> ParentRejectBookingAsync(
        int parentUserId, int bookingId, string? note, CancellationToken ct = default)
        => ParentDecideAsync(parentUserId, bookingId, approve: false, note, ct);

    private enum ParentDecisionOutcome
    {
        None,
        Done,
        AlreadyDecided,
        LinkGone,
        TeacherUnavailable,
        SlotDeleted
    }

    private async Task<BookingResultDto> ParentDecideAsync(
        int parentUserId, int bookingId, bool approve, string? note, CancellationToken ct)
    {
        var now = UtcNow();

        // issue #439 review: not #105 kuralıyla temizlenir (NUL/C0/C1 kontrol → 400, bidi/biçim karakterleri ayıklanır). Doğrulama
        // veri okumaz; kapıdan önce yapılması bir şey sızdırmaz.
        string? storedNote = null;
        if (!approve && !BookingParentApproval.TrySanitizeNote(note, out storedNote))
            return new BookingResultDto
            {
                Success = false,
                ErrorCode = BookingErrorCodes.InvalidNote,
                Message = _localizer["booking.parent.noteInvalid"]
            };

        // Slotu soft-delete edilmiş talep de bulunur (yalnız slot filtresi kapanır; #376 deseni).
        var booking = await _context.Bookings
            .WithSoftDeletedSlots()
            .AsNoTracking()
            .Where(b => b.Id == bookingId)
            .Select(b => new
            {
                b.Id,
                b.Status,
                b.StudentId,
                StudentUserId = b.Student.UserId,
                b.TeacherId,
                TeacherUserId = b.Teacher.UserId,
                b.AvailabilitySlotId,
                SlotDeleted = b.AvailabilitySlot.IsDeleted,
                b.AvailabilitySlot.Date,
                b.AvailabilitySlot.StartTime,
                b.AvailabilitySlot.EndTime,
                b.CreatedAt
            })
            .FirstOrDefaultAsync(ct);

        // Yetki önce: talep yoksa ya da çağıranın bu öğrenciyle Active bağlantısı yoksa aynı 404 (varlık sızdırılmaz).
        if (booking == null)
            return ParentChildNotFound();
        var link = await FindActiveChildLinkAsync(parentUserId, booking.StudentId, ct);
        if (link == null)
            return ParentChildNotFound();

        if (booking.Status != BookingStatus.AwaitingParent)
            return AlreadyDecidedFailure(booking.Status);

        // Süresi dolmuş talep (süpürme henüz kapatmadı) karara bağlanamaz.
        if (BookingParentApproval.IsExpired(booking.CreatedAt, booking.Date, booking.StartTime, now))
            return new BookingResultDto
            {
                Success = false,
                Conflict = true,
                ErrorCode = BookingErrorCodes.RequestExpired,
                Message = _localizer["booking.parent.requestExpired"]
            };

        if (approve && booking.SlotDeleted)
            return new BookingResultDto
            {
                Success = false,
                Conflict = true,
                ErrorCode = BookingErrorCodes.RequestSlotDeleted,
                Message = _localizer["booking.request.slotDeleted"]
            };

        // Ödeme kancası (#408): onay durum değişikliğinden ÖNCE tek kapıdan geçer.
        if (approve)
        {
            var gate = await _parentApprovalGate.AuthorizeAsync(new BookingParentApprovalContext(
                BookingParentApprovalKind.ApproveRequest, booking.Id, booking.StudentId, booking.TeacherId,
                booking.AvailabilitySlotId, parentUserId), ct);
            if (!gate.Allowed)
                return GateFailure(gate);
        }

        var parents = await ParentNotificationSupport.ActiveParentsAsync(_context, booking.StudentId, ct);
        var users = await TryLookupUsersAsync(
            new[] { booking.StudentUserId, booking.TeacherUserId, parentUserId }.Concat(parents.Select(p => p.ParentUserId)),
            "Veli randevu kararı", bookingId, ct);

        _context.SetCurrentUser(parentUserId);
        var outcome = ParentDecisionOutcome.None;

        var strategy = _context.Database.CreateExecutionStrategy();
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                _context.ChangeTracker.Clear();
                outcome = ParentDecisionOutcome.None;
                await using var tx = await _context.Database.BeginTransactionAsync(ct);

                // issue #439 re-review (yarış): kilit sırası talep oluşturmayla aynı — öğretmen müsaitlik advisory kilidi → öğretmen
                // satırı (FOR SHARE) → veli bağlantıları (FOR SHARE). Müsaitlik kilidi slot silme yollarıyla (tekil + seri) serileşir:
                // silme önce gelirse slotu burada silinmiş görürüz (onay yok); onay önce gelirse silme talebi Pending görür ve 409 verir.
                // Aksi halde silinmiş slotta Pending kalabilir (#376 kuralı) ve öğretmene bildirim giderdi.
                await _context.Database.AcquireTeacherAvailabilityLockAsync(booking.TeacherId, ct);
                if (approve && !await _context.TeacherAvailabilitySlots.AsNoTracking().AnyAsync(s => s.Id == booking.AvailabilitySlotId, ct))
                {
                    outcome = ParentDecisionOutcome.SlotDeleted;
                    return; // commit yok → rollback
                }

                // Öğrenci talebindeki kuralla aynı: öğretmen hâlâ talep alabilir mi (FOR SHARE, askı ile serileşir).
                var teacherBookable = !approve || await LockBookableTeacherAsync(booking.TeacherId, ct);

                // Bağlantı koparma (koşullu UPDATE) ile serileşir: koparılmış veli karar veremez, ona bildirim gitmez.
                var lockedParentIds = await ParentNotificationSupport.ActiveParentIdsLockedAsync(_context, booking.StudentId, ct);
                if (!lockedParentIds.Contains(link.ParentId))
                {
                    outcome = ParentDecisionOutcome.LinkGone;
                    return; // commit yok → rollback
                }

                var recipientUserIds = parents
                    .Where(p => lockedParentIds.Contains(p.ParentId))
                    .Select(p => p.ParentUserId)
                    .ToList();

                int affected;
                if (approve)
                {
                    if (!teacherBookable)
                    {
                        // Askı / bağımsızlık kaybı: talep "öğretmen müsait değil" yoluyla (aynı koşullu UPDATE) kapanır ve öğrenci +
                        // veliler bilgilendirilir. Başka bir nedenle talep alamıyorsa (ör. başvuru onayı geri çekildi) talep
                        // AwaitingParent kalır ve süre aşımıyla kapanır.
                        // issue #439 review: öğretmene hiç ulaşmamış talep Expired olur (öğretmen listesine düşmez); bildirim aynı.
                        var row = new PendingBookingRow(booking.Id, booking.TeacherId, booking.TeacherUserId, booking.StudentId,
                            booking.StudentUserId, booking.Date, booking.StartTime, booking.EndTime, BookingStatus.AwaitingParent);
                        await TeacherUnavailableBookingRejection.RejectAsync(
                            _context, new[] { row }, now, parentUserId,
                            id => FullNameOf(users, id),
                            id => KeycloakIdOf(users, id),
                            new Dictionary<int, List<int>> { [booking.StudentId] = recipientUserIds },
                            ct,
                            studentNameOf: id => FullNameOf(users, id));
                        await _context.SaveChangesAsync(ct);
                        await tx.CommitAsync(ct);
                        outcome = ParentDecisionOutcome.TeacherUnavailable;
                        return;
                    }

                    affected = await _context.Bookings
                        .Where(b => b.Id == bookingId && b.Status == BookingStatus.AwaitingParent)
                        .ExecuteUpdateAsync(set => set
                            .SetProperty(b => b.Status, BookingStatus.Pending)
                            .SetProperty(b => b.ParentApprovedAt, now)
                            .SetProperty(b => b.ParentApprovedBy, parentUserId)
                            .SetProperty(b => b.UpdateTime, now)
                            .SetProperty(b => b.UpdateUserId, parentUserId), ct);
                }
                else
                {
                    affected = await _context.Bookings
                        .Where(b => b.Id == bookingId && b.Status == BookingStatus.AwaitingParent)
                        .ExecuteUpdateAsync(set => set
                            .SetProperty(b => b.Status, BookingStatus.ParentRejected)
                            .SetProperty(b => b.ParentRejectedBy, parentUserId)
                            .SetProperty(b => b.DecisionAt, now)
                            .SetProperty(b => b.RejectionReason, storedNote)
                            .SetProperty(b => b.UpdateTime, now)
                            .SetProperty(b => b.UpdateUserId, parentUserId), ct);
                }

                if (affected == 0)
                {
                    // Başka veli / süpürme / öğretmen askısı önce davrandı → ilk karar kazanır, event yazılmaz.
                    outcome = ParentDecisionOutcome.AlreadyDecided;
                    return;
                }

                if (approve)
                {
                    // Talep şimdi öğretmene ulaşır: mevcut öğretmen bildirimi (öğrenci talebiyle aynı event).
                    AddOutbox(new BookingRequestCreatedEvent
                    {
                        BookingId = booking.Id,
                        TeacherId = booking.TeacherId,
                        TeacherUserId = booking.TeacherUserId,
                        TargetKeycloakId = KeycloakIdOf(users, booking.TeacherUserId),
                        StudentId = booking.StudentId,
                        StudentName = FullNameOf(users, booking.StudentUserId),
                        AvailabilitySlotId = booking.AvailabilitySlotId,
                        Date = booking.Date,
                        StartTime = booking.StartTime,
                        EndTime = booking.EndTime,
                        RequestedAt = booking.CreatedAt
                    }, now);
                }

                AddOutbox(new BookingParentDecisionEvent
                {
                    EventId = Guid.NewGuid(),
                    BookingId = booking.Id,
                    Decision = approve ? BookingParentDecisions.Approved : BookingParentDecisions.Rejected,
                    StudentId = booking.StudentId,
                    StudentUserId = booking.StudentUserId,
                    StudentKeycloakId = KeycloakIdOf(users, booking.StudentUserId),
                    StudentName = FullNameOf(users, booking.StudentUserId),
                    Parents = BookingParentApproval.Recipients(recipientUserIds, id => KeycloakIdOf(users, id)),
                    DecidedByParentUserId = parentUserId,
                    DecidedByParentName = users.ContainsKey(parentUserId) ? FullNameOf(users, parentUserId) : null,
                    TeacherId = booking.TeacherId,
                    TeacherName = FullNameOf(users, booking.TeacherUserId),
                    Date = booking.Date,
                    StartTime = booking.StartTime,
                    EndTime = booking.EndTime,
                    Note = storedNote,
                    DecidedAt = now
                }, now);

                // issue #439 review: velinin çocuk adına yazma işlemi erişim kaydına (her karar ayrı satır; ResetChildPassword deseni).
                _context.ParentAccessAudits.Add(new ParentAccessAudit
                {
                    ParentId = link.ParentId,
                    StudentId = booking.StudentId,
                    Endpoint = approve ? ParentAccessEndpoints.ChildBookingApprove : ParentAccessEndpoints.ChildBookingReject,
                    ResourceId = booking.Id,
                    At = now
                });

                await _context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                outcome = ParentDecisionOutcome.Done;
            });
        }
        catch (TeacherAvailabilityLockTimeoutException ex)
        {
            _context.ChangeTracker.Clear();
            _logger.LogWarning(ex, "Veli randevu onayı: öğretmen satırı kilidi zaman aşımı. BookingId={BookingId}", bookingId);
            return new BookingResultDto { Success = false, Conflict = true, Message = _localizer["booking.slot.busy"] };
        }

        switch (outcome)
        {
            case ParentDecisionOutcome.LinkGone:
                return ParentChildNotFound();
            case ParentDecisionOutcome.SlotDeleted:
                // Ön okumadaki kontrolle aynı yanıt: talep onaylanamaz (slot silme yolu talebi zaten Expired yapıp bildirdi).
                return new BookingResultDto
                {
                    Success = false,
                    Conflict = true,
                    ErrorCode = BookingErrorCodes.RequestSlotDeleted,
                    Message = _localizer["booking.request.slotDeleted"]
                };
            case ParentDecisionOutcome.TeacherUnavailable:
                _logger.LogInformation(
                    "Veli onayı reddedildi: öğretmen artık talep alamıyor. BookingId={BookingId} TeacherId={TeacherId}",
                    bookingId, booking.TeacherId);
                return new BookingResultDto
                {
                    Success = false,
                    Conflict = true,
                    ErrorCode = BookingErrorCodes.TeacherUnavailable,
                    Message = _localizer["booking.parent.teacherUnavailable"]
                };
            case ParentDecisionOutcome.AlreadyDecided:
            {
                var current = await _context.Bookings.AsNoTracking()
                    .Where(b => b.Id == bookingId)
                    .Select(b => (BookingStatus?)b.Status)
                    .FirstOrDefaultAsync(ct);
                return current == null ? ParentChildNotFound() : AlreadyDecidedFailure(current.Value);
            }
        }

        var result = await QueryBookingsAsync(b => b.Id == bookingId, 0, 1, ct);
        return new BookingResultDto
        {
            Success = true,
            ObjectId = bookingId,
            Message = _localizer[approve ? "booking.parent.approved" : "booking.parent.rejected"],
            Booking = result.Items.FirstOrDefault()
        };
    }

    /// <summary>issue #439: talep artık veli onayı beklemiyor (başka veli / süre aşımı / öğretmen askısı) → 409.</summary>
    private BookingResultDto AlreadyDecidedFailure(BookingStatus current) => new()
    {
        Success = false,
        Conflict = true,
        ErrorCode = BookingErrorCodes.AlreadyDecided,
        // issue #439 review: ham enum adı istemciye basılmaz; güncel durum talep listesinde zaten görünür.
        Message = _localizer["booking.parent.alreadyDecided"]
    };
}
