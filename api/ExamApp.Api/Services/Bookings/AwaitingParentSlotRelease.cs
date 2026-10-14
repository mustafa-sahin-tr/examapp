using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Parents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.Bookings;

/// <summary>
/// issue #439 review: öğretmen, yalnızca veli onayı bekleyen (AwaitingParent) bir talebin tuttuğu slotu silebilir — talep henüz
/// öğretmene ulaşmadığı için öğretmeni bağlamaz. Silme yolları (tekil <c>DeleteSlotAsync</c> + seri
/// <c>RecurringAvailabilityService.DeleteRuleAsync</c>) öğretmen müsaitlik kilidi altında bu sınıfla talebi kapatır:
/// <list type="bullet">
/// <item>Durum: AwaitingParent → <see cref="BookingStatus.Expired"/> (öğretmene görünmez; askı yoluyla aynı,
/// <see cref="TeacherUnavailableBookingRejection.ClosedStatusFor"/>).</item>
/// <item>Bildirim: öğretmen askısıyla AYNI <c>BookingDecisionEvent</c> (<c>TeacherUnavailable=true</c>, öğrenci + Active veliler) —
/// öğrenci açısından sonuç aynıdır ("öğretmen bu saatte artık müsait değil"); veli süre aşımı (<c>BookingParentDecisionEvent</c>
/// Expired) kullanılmaz çünkü veliden karar beklenmiyordu.</item>
/// </list>
/// Pending/Approved talep varsa silme eskisi gibi reddedilir (<see cref="BlockingStatuses"/>).
/// </summary>
internal static class AwaitingParentSlotRelease
{
    /// <summary>Slot silmeyi engelleyen (öğretmene ulaşmış) aktif durumlar.</summary>
    public static readonly BookingStatus[] BlockingStatuses = { BookingStatus.Pending, BookingStatus.Approved };

    /// <summary>
    /// Transaction'dan ÖNCE (kilitsiz, best-effort): silinecek slotlardaki olası AwaitingParent taleplerin öğrenci/öğretmen/veli
    /// ad ve sub'ları. Talep yoksa auth-api'ye gidilmez.
    /// </summary>
    public static async Task<IReadOnlyDictionary<int, NotificationUser>> PrepareAsync(
        AppDbContext context, IAuthApiClient? authApi, IQueryable<Booking> candidates, ILogger? logger, CancellationToken ct)
    {
        var rows = await candidates
            .WithSoftDeletedSlots()
            .AsNoTracking()
            .Where(b => b.Status == BookingStatus.AwaitingParent)
            .Select(b => new { b.StudentId, StudentUserId = b.Student.UserId, TeacherUserId = b.Teacher.UserId })
            .ToListAsync(ct);
        if (rows.Count == 0)
            return new Dictionary<int, NotificationUser>();

        var parents = await BookingParentApproval.ActiveParentUserIdsByStudentAsync(context, rows.Select(r => r.StudentId), ct);
        return await ParentNotificationSupport.LookupAsync(
            authApi,
            rows.SelectMany(r => new[] { r.StudentUserId, r.TeacherUserId }).Concat(parents.Values.SelectMany(v => v)),
            logger, ct);
    }

    /// <summary>
    /// Öğretmen müsaitlik kilidi + transaction İÇİNDE: verilen slotlardaki AwaitingParent talepleri koşullu olarak Expired yapar ve
    /// bildirim outbox'larını context'e ekler (SaveChanges/commit çağıranda).
    /// <para>
    /// issue #439 re-review (savunma): okunan AwaitingParent satırlarının HEPSİ kapatılamadıysa (koşullu UPDATE 0 satır — araya
    /// kilit dışı bir yazıcı girip talebi örn. Pending yaptı) <c>false</c> döner; çağıran silmeyi iptal eder (rollback, 409
    /// SlotHasActiveBooking). Böylece silinmiş slotta aktif talep kalmaz.
    /// </para>
    /// </summary>
    public static async Task<bool> ReleaseAsync(
        AppDbContext context, IReadOnlyCollection<int> slotIds, DateTime nowUtc, int actorUserId,
        IReadOnlyDictionary<int, NotificationUser> users, CancellationToken ct)
    {
        if (slotIds.Count == 0)
            return true;

        var rows = await TeacherUnavailableBookingRejection
            .SelectPending(context.Bookings.Where(b => slotIds.Contains(b.AvailabilitySlotId) && b.Status == BookingStatus.AwaitingParent))
            .AsNoTracking()
            .ToListAsync(ct);
        if (rows.Count == 0)
            return true;

        var lockedParents = await BookingParentApproval.ActiveParentUserIdsLockedAsync(context, rows.Select(r => r.StudentId), ct);
        var closed = await TeacherUnavailableBookingRejection.RejectAsync(
            context, rows, nowUtc, actorUserId > 0 ? actorUserId : null,
            id => ParentNotificationSupport.Of(users, id).DisplayName,
            id => ParentNotificationSupport.Of(users, id).KeycloakId,
            lockedParents,
            ct,
            studentNameOf: id => ParentNotificationSupport.Of(users, id).DisplayName,
            requireTeacherUnavailable: false);
        return closed.Count == rows.Count;
    }
}
